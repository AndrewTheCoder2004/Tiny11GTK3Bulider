using System.Diagnostics;
using System.Text;

namespace Tiny11Builder.Portable;

internal static class NativeProcess
{
    private static readonly string[] WellKnownUnixPaths =
    [
        "/usr/local/bin",
        "/opt/homebrew/bin",
        "/usr/pkg/bin",
        "/opt/local/bin",
        "/usr/bin",
        "/bin",
        "/usr/sbin",
        "/sbin"
    ];

    public static string FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim())
            .ToList();

        if (!OperatingSystem.IsWindows())
        {
            foreach (var unixDir in WellKnownUnixPaths)
            {
                if (!directories.Contains(unixDir, StringComparer.Ordinal))
                    directories.Add(unixDir);
            }
        }

        var names = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? new[] { name, name + ".exe" }
            : new[] { name };

        foreach (var directory in directories)
        {
            foreach (var candidateName in names)
            {
                try
                {
                    var candidate = Path.Combine(directory, candidateName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // Ignore invalid path characters or permission issues in search
                }
            }
        }

        throw new FileNotFoundException($"Required tool '{name}' was not found on PATH or in standard system directories. Install it using your operating system's package manager.");
    }

    public static async Task<string> RunAsync(string executable, IEnumerable<string> arguments,
        Action<string>? output, CancellationToken cancellationToken, string? standardInput = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            CreateNoWindow = true
        };
        start.Environment["LC_ALL"] = "C";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException($"Could not start '{executable}'. Confirm that the required native tool is installed and on PATH.", error);
        }

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });

        var stdout = ReadLinesAsync(process.StandardOutput, output);
        var stderr = ReadLinesAsync(process.StandardError, output);
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
            catch (IOException) { }
            throw;
        }

        var capturedOut = await stdout.ConfigureAwait(false);
        var capturedError = await stderr.ConfigureAwait(false);
        var combined = string.Join(Environment.NewLine, new[] { capturedOut, capturedError }.Where(value => value.Length > 0));
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(executable)} exited with code {process.ExitCode}.{Environment.NewLine}{combined}");
        return combined;
    }

    private static async Task<string> ReadLinesAsync(StreamReader reader, Action<string>? output)
    {
        var captured = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (captured.Length > 0) captured.AppendLine();
            captured.Append(line);
            try { output?.Invoke(line); }
            catch { }
        }
        return captured.ToString();
    }
}
