using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Tiny11Builder.Portable;

public sealed record WindowsEdition(int Index, string Name, string Description, string Architecture,
    string Version, string Edition, string InstallationType, long Size)
{
    public override string ToString() => $"{Name}  ·  {Architecture}  ·  {Version}";
}

public sealed record PortableBuildRequest(
    string SourcePath,
    string WorkspacePath,
    string OutputPath,
    int ImageIndex,
    IReadOnlyList<string> SelectedRemovals,
    bool BypassHardwareRequirements = true,
    bool EnableLocalAccount = true,
    bool CompactInstallation = true,
    bool DisableSponsoredApps = true,
    bool DisableTelemetry = true,
    bool DisableCopilot = true,
    bool DisableWidgets = true,
    bool DisableBitLockerDeviceEncryption = true,
    bool DisableReservedStorage = true)
{
    public PortableBuildRequest(
        string sourcePath,
        string workspacePath,
        string outputPath,
        int imageIndex,
        bool removeEdgeBinaries,
        bool removeOneDriveSetup)
        : this(
            sourcePath,
            workspacePath,
            outputPath,
            imageIndex,
            CreateLegacyRemovals(removeEdgeBinaries, removeOneDriveSetup))
    {
    }

    private static IReadOnlyList<string> CreateLegacyRemovals(bool edge, bool oneDrive)
    {
        var list = new List<string>();
        if (edge) list.Add("edge-files");
        if (oneDrive) list.Add("onedrive-files");
        return list;
    }

    public bool RemoveEdgeBinaries => SelectedRemovals.Contains("edge-files");
    public bool RemoveOneDriveSetup => SelectedRemovals.Contains("onedrive-files");
}

public sealed record PortableBuildResult(string IsoPath, string Sha256);

public sealed class PortableBuildEngine
{
    private const long MinimumFreeBytes = 24L * 1024 * 1024 * 1024;

    public async Task<IReadOnlyList<WindowsEdition>> InspectAsync(string sourcePath, string workspacePath,
        Action<string>? progress, CancellationToken cancellationToken)
    {
        ValidatePlatform();
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("A Windows ISO or media folder is required.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(workspacePath)) throw new ArgumentException("A workspace folder is required.", nameof(workspacePath));
        var source = Path.GetFullPath(sourcePath);
        var workspace = Path.GetFullPath(workspacePath);
        if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("Choose a Windows ISO or installation-media folder.", source);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Source media is a symbolic link or reparse point: {source}");
        if (IsInside(workspace, source)) throw new IOException("Choose a workspace outside the source media folder.");
        Directory.CreateDirectory(workspace);
        var temporary = Path.Combine(workspace, "inspect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var media = await PrepareMediaAsync(source, temporary, progress, cancellationToken).ConfigureAwait(false);
            var wim = await FindWimAsync(media, temporary, progress, cancellationToken).ConfigureAwait(false);
            return await ReadEditionsAsync(wim.Path, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(temporary);
        }
    }

    public async Task<PortableBuildResult> BuildAsync(PortableBuildRequest request, Action<string>? progress, CancellationToken cancellationToken)
    {
        ValidatePlatform();
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourcePath)) throw new ArgumentException("A Windows ISO or media folder is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.WorkspacePath)) throw new ArgumentException("A workspace folder is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutputPath)) throw new ArgumentException("An output ISO path is required.", nameof(request));
        var source = Path.GetFullPath(request.SourcePath);
        var workspace = Path.GetFullPath(request.WorkspacePath);
        var output = Path.GetFullPath(request.OutputPath);
        if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("Choose a Windows ISO or installation-media folder.", source);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Source media is a symbolic link or reparse point: {source}");
        if (source.Equals(output, PathComparison)) throw new IOException("The output ISO cannot replace the source media.");
        if (IsInside(output, source) || IsInside(workspace, source))
            throw new IOException("The workspace and output must be outside the source media folder.");
        if (Path.GetExtension(output).Equals(".iso", StringComparison.OrdinalIgnoreCase) is false)
            throw new IOException("Choose an output filename ending in .iso.");
        if (request.ImageIndex < 1) throw new ArgumentOutOfRangeException(nameof(request.ImageIndex));

        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var drive = DriveInfo.GetDrives().Where(item => workspace.StartsWith(item.Name, PathComparison))
            .OrderByDescending(item => item.Name.Length).FirstOrDefault();
        var free = drive?.AvailableFreeSpace ?? new DriveInfo(Path.GetPathRoot(workspace) ?? workspace).AvailableFreeSpace;
        if (free < MinimumFreeBytes)
            throw new IOException("At least 24 GiB of free workspace space is recommended for staging Windows installation media.");

        var xorriso = NativeProcess.FindExecutable("xorriso");
        var wimlib = NativeProcess.FindExecutable("wimlib-imagex");
        var job = Path.Combine(workspace, "build-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(job);
        var media = Path.Combine(job, "media");
        var temporaryIso = output + ".partial-" + Guid.NewGuid().ToString("N") + ".iso";
        try
        {
            progress?.Invoke("Preparing a private copy of the source media.");
            await PrepareMediaAsync(source, media, progress, cancellationToken).ConfigureAwait(false);
            ValidateWindowsMedia(media);
            var sourceImage = await FindWimAsync(media, job, progress, cancellationToken).ConfigureAwait(false);
            var editions = await ReadEditionsAsync(sourceImage.Path, progress, cancellationToken).ConfigureAwait(false);
            var selected = editions.SingleOrDefault(image => image.Index == request.ImageIndex)
                ?? throw new InvalidDataException($"Image index {request.ImageIndex} is not present in the selected media.");
            EnsureSupportedEdition(selected);

            // Step 1: Export chosen edition
            var outputWim = Path.Combine(job, "install.wim");
            progress?.Invoke($"Exporting {selected.Name} ({selected.Architecture}) as a single-edition install.wim.");
            await NativeProcess.RunAsync(wimlib,
                ["export", sourceImage.Path, request.ImageIndex.ToString(CultureInfo.InvariantCulture), outputWim, "--compress=LZX", "--recompress", "--check"],
                progress, cancellationToken).ConfigureAwait(false);

            // Step 2: Hardware bypass in boot.wim (WinPE Setup)
            if (request.BypassHardwareRequirements)
            {
                var bootWim = Path.Combine(media, "sources", "boot.wim");
                if (File.Exists(bootWim))
                {
                    progress?.Invoke("Injecting hardware bypass commands into Windows Setup WinPE boot image.");
                    await InjectWinPeBypassAsync(wimlib, bootWim, job, progress, cancellationToken).ConfigureAwait(false);
                }
            }

            // Step 3: Unattended setup configuration (autounattend.xml)
            progress?.Invoke("Generating autounattend.xml for unattended bypasses and local account support.");
            var unattendXml = PortableUnattendBuilder.Create(
                selected.Architecture,
                request.BypassHardwareRequirements,
                request.EnableLocalAccount,
                request.CompactInstallation);

            // Save to root of media
            var mediaUnattend = Path.Combine(media, "autounattend.xml");
            await File.WriteAllTextAsync(mediaUnattend, unattendXml, cancellationToken).ConfigureAwait(false);

            // Save temp copy for injection into install.wim sysprep
            var stagedUnattend = Path.Combine(job, "sysprep_autounattend.xml");
            await File.WriteAllTextAsync(stagedUnattend, unattendXml, cancellationToken).ConfigureAwait(false);

            // Step 4: Post-install configuration script (SetupComplete.cmd)
            progress?.Invoke("Generating SetupComplete.cmd for post-install tweaks and telemetry suppression.");
            var setupCompleteCmd = PortableSetupCompleteBuilder.Create(
                request.BypassHardwareRequirements,
                request.EnableLocalAccount,
                request.DisableSponsoredApps,
                request.DisableTelemetry,
                request.DisableCopilot,
                request.DisableWidgets,
                request.DisableBitLockerDeviceEncryption,
                request.DisableReservedStorage,
                request.SelectedRemovals.Contains("tasks-telemetry"));

            var stagedSetupComplete = Path.Combine(job, "SetupComplete.cmd");
            await File.WriteAllTextAsync(stagedSetupComplete, setupCompleteCmd, cancellationToken).ConfigureAwait(false);

            // Step 5: Gather package and binary removals
            progress?.Invoke("Analyzing image packages and generating removal list.");
            var updateCommands = await BuildWimUpdateCommandsAsync(
                wimlib, outputWim, request, stagedUnattend, stagedSetupComplete, progress, cancellationToken).ConfigureAwait(false);

            if (updateCommands.Count > 0)
            {
                progress?.Invoke($"Applying {updateCommands.Count} image updates (removals and unattend configuration).");
                await NativeProcess.RunAsync(wimlib, ["update", outputWim, "1", "--check"], progress, cancellationToken,
                    string.Join(Environment.NewLine, updateCommands) + Environment.NewLine).ConfigureAwait(false);

                progress?.Invoke("Optimizing and verifying modified install.wim.");
                await NativeProcess.RunAsync(wimlib, ["optimize", outputWim, "--check"], progress, cancellationToken).ConfigureAwait(false);
            }

            // Step 6: Verify exported WIM
            var finalEditions = await ReadEditionsAsync(outputWim, progress, cancellationToken).ConfigureAwait(false);
            if (finalEditions.Count != 1 || finalEditions[0].Index != 1)
                throw new InvalidDataException("The exported WIM did not verify as a single-image installation file.");

            // Step 7: Replace source image in media sources directory
            var sources = FindChild(media, "sources") ?? throw new DirectoryNotFoundException("The media has no sources directory.");
            foreach (var old in Directory.EnumerateFiles(sources, "*", SearchOption.TopDirectoryOnly)
                         .Where(path => Path.GetFileName(path).StartsWith("install", StringComparison.OrdinalIgnoreCase) &&
                             new[] { ".wim", ".esd", ".swm" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
                File.Delete(old);

            var installTarget = Path.Combine(sources, "install.wim");
            if (new FileInfo(outputWim).Length <= 3500L * 1024 * 1024)
            {
                File.Move(outputWim, installTarget);
            }
            else
            {
                progress?.Invoke("Splitting the installation image into Setup-compatible SWM parts (FAT32 compatible).");
                await NativeProcess.RunAsync(wimlib, ["split", outputWim, Path.Combine(sources, "install.swm"), "3500", "--check"],
                    progress, cancellationToken).ConfigureAwait(false);
            }

            // Step 8: Build bootable ISO with xorriso
            progress?.Invoke("Creating the bootable Windows ISO with xorriso (BIOS + UEFI dual-boot).");
            var isoArgs = CreateIsoArguments(media, temporaryIso);
            await NativeProcess.RunAsync(xorriso, isoArgs, progress, cancellationToken).ConfigureAwait(false);
            var result = new FileInfo(temporaryIso);
            if (!result.Exists || result.Length < 16 * 1024 * 1024)
                throw new IOException("xorriso did not produce a complete ISO image.");

            // Step 9: Validate ISO structure and calculate SHA-256
            progress?.Invoke("Validating ISO/UDF volume descriptor and calculating SHA-256 digest.");
            string sha256;
            await using (var stream = File.OpenRead(temporaryIso))
            {
                await IsoImageStructure.ValidateAsync(stream, cancellationToken).ConfigureAwait(false);
                sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Move(temporaryIso, output, overwrite: true);
            progress?.Invoke($"Build complete: {output}");
            return new(output, sha256);
        }
        catch
        {
            TryDeleteFile(temporaryIso);
            throw;
        }
        finally
        {
            TryDeleteDirectory(job);
        }
    }

    private static async Task InjectWinPeBypassAsync(string wimlib, string bootWim, string jobDir,
        Action<string>? progress, CancellationToken cancellationToken)
    {
        try
        {
            var startnetPath = Path.Combine(jobDir, "startnet.cmd");
            var startnetContent =
                "@echo off\r\n" +
                "reg add \"HKLM\\SYSTEM\\Setup\\LabConfig\" /v BypassCPUCheck /t REG_DWORD /d 1 /f >nul 2>&1\r\n" +
                "reg add \"HKLM\\SYSTEM\\Setup\\LabConfig\" /v BypassRAMCheck /t REG_DWORD /d 1 /f >nul 2>&1\r\n" +
                "reg add \"HKLM\\SYSTEM\\Setup\\LabConfig\" /v BypassSecureBootCheck /t REG_DWORD /d 1 /f >nul 2>&1\r\n" +
                "reg add \"HKLM\\SYSTEM\\Setup\\LabConfig\" /v BypassStorageCheck /t REG_DWORD /d 1 /f >nul 2>&1\r\n" +
                "reg add \"HKLM\\SYSTEM\\Setup\\LabConfig\" /v BypassTPMCheck /t REG_DWORD /d 1 /f >nul 2>&1\r\n" +
                "reg add \"HKLM\\SYSTEM\\Setup\\MoSetup\" /v AllowUpgradesWithUnsupportedTPMOrCPU /t REG_DWORD /d 1 /f >nul 2>&1\r\n" +
                "wpeinit\r\n";
            await File.WriteAllTextAsync(startnetPath, startnetContent, cancellationToken).ConfigureAwait(false);

            var cmd = $"add \"{startnetPath}\" \"/Windows/System32/startnet.cmd\"" + Environment.NewLine;
            // Boot index 2 is the Setup PE environment
            await NativeProcess.RunAsync(wimlib, ["update", bootWim, "2", "--check"], progress, cancellationToken, cmd).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            progress?.Invoke($"Notice: Could not inject WinPE bypass into boot.wim (continuing with autounattend.xml bypass): {ex.Message}");
        }
    }

    private static async Task<List<string>> BuildWimUpdateCommandsAsync(
        string wimlib,
        string outputWim,
        PortableBuildRequest request,
        string stagedUnattend,
        string stagedSetupComplete,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var commands = new List<string>();

        // 1. Injections: autounattend.xml and SetupComplete.cmd
        commands.Add($"add \"{stagedUnattend}\" \"/Windows/System32/Sysprep/autounattend.xml\"");
        commands.Add($"add \"{stagedSetupComplete}\" \"/Windows/Setup/Scripts/SetupComplete.cmd\"");

        // 2. Edge & WebView removals
        if (request.RemoveEdgeBinaries)
        {
            foreach (var path in new[]
            {
                "/Program Files/Microsoft/Edge",
                "/Program Files/Microsoft/EdgeCore",
                "/Program Files/Microsoft/EdgeUpdate",
                "/Program Files/Microsoft/EdgeWebView",
                "/Program Files (x86)/Microsoft/Edge",
                "/Program Files (x86)/Microsoft/EdgeCore",
                "/Program Files (x86)/Microsoft/EdgeUpdate",
                "/Program Files (x86)/Microsoft/EdgeWebView",
                "/Windows/System32/Microsoft-Edge-Webview"
            })
            {
                commands.Add($"delete --recursive --force \"{path}\"");
            }
        }

        // 3. OneDrive Setup removals
        if (request.RemoveOneDriveSetup)
        {
            commands.Add("delete --force \"/Windows/System32/OneDriveSetup.exe\"");
            commands.Add("delete --force \"/Windows/SysWOW64/OneDriveSetup.exe\"");
        }

        // 4. Bundled Apps (WindowsApps packages)
        var selectedAppOptions = PortableCleanupCatalog.Options
            .Where(o => o.Category == PortableCleanupCategory.BundledApps && request.SelectedRemovals.Contains(o.Id))
            .ToList();

        if (selectedAppOptions.Count > 0)
        {
            var prefixesToRemove = selectedAppOptions
                .SelectMany(o => o.TargetPrefixes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            try
            {
                // Query installed packages in /Program Files/WindowsApps
                var windowsAppsList = await NativeProcess.RunAsync(
                    wimlib, ["dir", outputWim, "1", "/Program Files/WindowsApps", "--path-only"],
                    null, cancellationToken).ConfigureAwait(false);

                var lines = windowsAppsList.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var cleanPath = line.Trim();
                    var folderName = Path.GetFileName(cleanPath);
                    if (string.IsNullOrWhiteSpace(folderName)) continue;

                    if (prefixesToRemove.Any(prefix => folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                                                       folderName.Contains(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        commands.Add($"delete --recursive --force \"{cleanPath}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                progress?.Invoke($"Notice: Could not list /Program Files/WindowsApps: {ex.Message}");
            }
        }

        // 5. Scheduled Tasks
        if (request.SelectedRemovals.Contains("tasks-telemetry"))
        {
            commands.Add("delete --force \"/Windows/System32/Tasks/Microsoft/Windows/Application Experience/Microsoft Compatibility Appraiser\"");
            commands.Add("delete --force \"/Windows/System32/Tasks/Microsoft/Windows/Application Experience/ProgramDataUpdater\"");
            commands.Add("delete --force \"/Windows/System32/Tasks/Microsoft/Windows/Chkdsk/Proxy\"");
            commands.Add("delete --force \"/Windows/System32/Tasks/Microsoft/Windows/Windows Error Reporting/QueueReporting\"");
            commands.Add("delete --recursive --force \"/Windows/System32/Tasks/Microsoft/Windows/Customer Experience Improvement Program\"");
        }

        return commands;
    }

    public static string GetToolRequirements()
    {
        var missing = new List<string>();
        foreach (var tool in new[] { "wimlib-imagex", "xorriso" })
        {
            try { _ = NativeProcess.FindExecutable(tool); }
            catch (FileNotFoundException) { missing.Add(tool); }
        }

        var osName = OperatingSystem.IsLinux() ? "Linux" :
                     OperatingSystem.IsMacOS() ? "macOS" :
                     OperatingSystem.IsFreeBSD() ? "FreeBSD" :
                     Environment.OSVersion.Platform == PlatformID.Unix ? "Unix/BSD" : "Windows";

        return missing.Count == 0
            ? $"Ready on {osName}: wimlib-imagex and xorriso are available."
            : $"Missing native tools on {osName}: {string.Join(", ", missing)}. Install via your system package manager (e.g. apt, brew, pkg).";
    }

    private static void ValidatePlatform()
    {
        // Fully supported on Linux, macOS, FreeBSD, NetBSD, OpenBSD, and Windows (with native tool binaries).
    }

    private static async Task<string> PrepareMediaAsync(string source, string destination, Action<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        if (Directory.Exists(source))
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Source media is a symbolic link or reparse point: {source}");
            progress?.Invoke("Copying installation-media files into the private workspace.");
            await CopyDirectoryAsync(source, destination, cancellationToken).ConfigureAwait(false);
            return destination;
        }
        if (!Path.GetExtension(source).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a Windows ISO or a folder containing extracted Windows installation media.");
        var xorriso = NativeProcess.FindExecutable("xorriso");
        progress?.Invoke("Extracting the source ISO with xorriso.");
        await NativeProcess.RunAsync(xorriso, ["-osirrox", "on", "-indev", source, "-extract", "/", destination], progress, cancellationToken)
            .ConfigureAwait(false);
        return destination;
    }

    private static async Task<(string Path, bool Temporary)> FindWimAsync(string media, string temporaryDirectory,
        Action<string>? progress, CancellationToken cancellationToken)
    {
        var sources = FindChild(media, "sources") ?? throw new DirectoryNotFoundException("The media has no sources directory.");
        var wim = FindChild(sources, "install.wim");
        if (wim is not null) return (wim, false);
        var esd = FindChild(sources, "install.esd");
        if (esd is not null) return (esd, false);
        var split = FindChild(sources, "install.swm");
        if (split is null) throw new FileNotFoundException("The media has no sources/install.wim, install.esd or install.swm file.");
        var parts = Directory.EnumerateFiles(sources, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Regex.IsMatch(Path.GetFileName(path), @"^install\d*\.swm$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .OrderBy(path => SwmPartNumber(path)).ToArray();
        if (parts.Length == 0) throw new InvalidDataException("The split installation image is incomplete.");
        var joined = Path.Combine(temporaryDirectory, "joined-install.wim");
        progress?.Invoke("Joining split install.swm parts for image inspection/export.");
        await NativeProcess.RunAsync(NativeProcess.FindExecutable("wimlib-imagex"), ["join", joined, .. parts, "--check"], progress, cancellationToken)
            .ConfigureAwait(false);
        return (joined, true);
    }

    private static async Task<IReadOnlyList<WindowsEdition>> ReadEditionsAsync(string imagePath, Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var executable = NativeProcess.FindExecutable("wimlib-imagex");
        var text = await NativeProcess.RunAsync(executable, ["info", imagePath], progress, cancellationToken)
            .ConfigureAwait(false);
        var indexes = Regex.Matches(text, @"(?m)^(?:Image )?Index:\s*(\d+)\s*$")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().Order().ToArray();
        if (indexes.Length == 0)
        {
            indexes = Regex.Matches(text, @"(?m)^Image (\d+):\s*$")
                .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
                .Distinct().Order().ToArray();
        }
        if (indexes.Length == 0) throw new InvalidDataException("wimlib-imagex did not return any image indexes.");
        var result = new List<WindowsEdition>();
        foreach (var index in indexes)
        {
            var detail = await NativeProcess.RunAsync(executable,
                ["info", imagePath, index.ToString(CultureInfo.InvariantCulture)], progress, cancellationToken).ConfigureAwait(false);
            string Field(string name)
            {
                var matches = Regex.Matches(detail, $@"(?m)^{Regex.Escape(name)}:\s*(.*?)\s*$");
                return matches.Count > 0 ? matches[^1].Groups[1].Value.Trim() : "";
            }
            long.TryParse(Field("Total Bytes"), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes);
            var version = Field("Version");
            if (version.Length == 0) version = Field("Product Version");
            result.Add(new(index, Field("Name"), Field("Description"), Field("Architecture"), version,
                Field("Edition ID"), Field("Installation Type"), bytes));
        }
        if (result.Count == 0) throw new InvalidDataException("wimlib-imagex did not return any installation editions.");
        return result;
    }

    private static void EnsureSupportedEdition(WindowsEdition edition)
    {
        if (edition.InstallationType.Contains("server", StringComparison.OrdinalIgnoreCase) ||
            edition.Name.Contains("server", StringComparison.OrdinalIgnoreCase) ||
            edition.Edition.Contains("server", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Windows Server images are not supported. Choose a Windows 11 client edition.");
        if (!Version.TryParse(edition.Version, out var version) || version.Major != 10 || version.Build < 22000)
            throw new InvalidDataException($"The selected image version '{edition.Version}' is not a verifiable Windows 11 build.");
        if (!edition.InstallationType.Equals("Client", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only Windows client installation editions are supported.");
        if (!edition.Architecture.Equals("x86_64", StringComparison.OrdinalIgnoreCase) &&
            !edition.Architecture.Equals("amd64", StringComparison.OrdinalIgnoreCase) &&
            !edition.Architecture.Equals("arm64", StringComparison.OrdinalIgnoreCase) &&
            !edition.Architecture.Equals("aarch64", StringComparison.OrdinalIgnoreCase) &&
            !edition.Architecture.Equals("x64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{edition.Architecture} is not supported. Choose an x64 or ARM64 Windows client edition.");
    }

    private static string[] CreateIsoArguments(string media, string output)
    {
        var bootDirectory = FindChild(media, "boot");
        var efiDirectory = FindChild(media, "efi");
        var bios = bootDirectory is null ? null : FindChild(bootDirectory, "etfsboot.com");
        string? efiBoot = null;
        if (efiDirectory is not null && FindChild(efiDirectory, "microsoft") is { } microsoft &&
            FindChild(microsoft, "boot") is { } efiBootDirectory)
            efiBoot = FindChild(efiBootDirectory, "efisys.bin");
        if (bios is null && efiBoot is null)
            throw new InvalidDataException("No Windows BIOS or UEFI boot image was found in the staged media.");
        var args = new List<string> { "-as", "mkisofs", "-iso-level", "3", "-udf", "-J", "-joliet-long", "-relaxed-filenames", "-V", "TINY11_PORTABLE" };
        if (bios is not null)
        {
            var relativeBios = IsoRelativePath(media, bios);
            var catalogDirectory = Path.GetDirectoryName(relativeBios)?.Replace(Path.DirectorySeparatorChar, '/') ?? "boot";
            args.AddRange(["-b", relativeBios, "-no-emul-boot", "-boot-load-size", "8", "-c", $"{catalogDirectory}/boot.catalog"]);
        }
        if (efiBoot is not null)
        {
            if (bios is not null) args.Add("-eltorito-alt-boot");
            args.AddRange(["-e", IsoRelativePath(media, efiBoot), "-no-emul-boot"]);
        }
        args.AddRange(["-o", output, media]);
        return args.ToArray();
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Source media contains a symbolic link or reparse point: {entry}");
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(target);
                await CopyDirectoryAsync(entry, target, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await using var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 1024, useAsync: true);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    1024 * 1024, useAsync: true);
                await input.CopyToAsync(output, 1024 * 1024, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsInside(string path, string parent)
    {
        if (!Directory.Exists(parent)) return false;
        var fullParent = Path.GetFullPath(parent);
        var fullPath = Path.GetFullPath(path);
        if (fullPath.Equals(fullParent, PathComparison)) return true;
        if (!fullParent.EndsWith(Path.DirectorySeparatorChar) && !fullParent.EndsWith(Path.AltDirectorySeparatorChar))
            fullParent += Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullParent, PathComparison);
    }

    private static int SwmPartNumber(string path)
    {
        var match = Regex.Match(Path.GetFileName(path), @"^install(?:(\d+))?\.swm$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var part) ? part : 1;
    }

    private static string? FindChild(string directory, string name) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFileSystemEntries(directory)
                .FirstOrDefault(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase))
            : null;

    private static string IsoRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static void ValidateWindowsMedia(string media)
    {
        var sources = FindChild(media, "sources");
        if (FindChild(media, "setup.exe") is null || sources is null || FindChild(sources, "boot.wim") is null)
            throw new InvalidDataException("The selected folder is not complete Windows installation media (setup.exe or sources/boot.wim is missing).");
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
