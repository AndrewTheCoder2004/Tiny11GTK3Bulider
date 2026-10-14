using System.Text.Json;
using Tiny11Builder.Portable;
using Xunit;

namespace Tiny11Builder.Portable.Tests;

public class PortableRecipeTests
{
    [Fact]
    public void Recipe_defaults_are_correct()
    {
        var recipe = new PortableRecipe();
        Assert.Equal(2, recipe.SchemaVersion);
        Assert.Equal("tiny11", recipe.Mode);
        Assert.True(recipe.BypassHardwareRequirements);
        Assert.True(recipe.EnableLocalAccount);
        Assert.True(recipe.CompactInstallation);
        Assert.True(recipe.DisableSponsoredApps);
        Assert.True(recipe.DisableTelemetry);
        Assert.True(recipe.DisableCopilot);
        Assert.True(recipe.DisableWidgets);
        Assert.True(recipe.DisableBitLockerDeviceEncryption);
        Assert.True(recipe.DisableReservedStorage);
    }

    [Fact]
    public void Recipe_serialization_round_trip()
    {
        var original = new PortableRecipe
        {
            Mode = "custom",
            SourcePath = "/path/to/windows11.iso",
            WorkspacePath = "/path/to/workspace",
            OutputPath = "/path/to/output.iso",
            ImageIndex = 3,
            SelectedRemovals = ["copilot-app", "edge-files", "onedrive-files"],
            BypassHardwareRequirements = true,
            EnableLocalAccount = true,
            CompactInstallation = false
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<PortableRecipe>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Mode, deserialized.Mode);
        Assert.Equal(original.SourcePath, deserialized.SourcePath);
        Assert.Equal(original.WorkspacePath, deserialized.WorkspacePath);
        Assert.Equal(original.OutputPath, deserialized.OutputPath);
        Assert.Equal(original.ImageIndex, deserialized.ImageIndex);
        Assert.Equal(original.SelectedRemovals, deserialized.SelectedRemovals);
        Assert.True(deserialized.RemoveEdgeBinaries);
        Assert.True(deserialized.RemoveOneDriveSetup);
        Assert.False(deserialized.CompactInstallation);
    }

    [Fact]
    public void Schema_v1_backward_compatibility()
    {
        var v1Json = """
        {
            "schemaVersion": 1,
            "mode": "tiny11",
            "sourcePath": "/tmp/source.iso",
            "workspacePath": "/tmp/workspace",
            "outputPath": "/tmp/out.iso",
            "imageIndex": 1,
            "removeEdgeBinaries": true,
            "removeOneDriveSetup": true
        }
        """;

        var recipe = JsonSerializer.Deserialize<PortableRecipe>(v1Json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(recipe);
        Assert.True(recipe.RemoveEdgeBinaries);
        Assert.True(recipe.RemoveOneDriveSetup);
        Assert.Contains("edge-files", recipe.SelectedRemovals);
        Assert.Contains("onedrive-files", recipe.SelectedRemovals);
    }

    [Fact]
    public void Recipe_validation_throws_on_empty_paths()
    {
        var invalid = new PortableRecipe { SourcePath = "", WorkspacePath = "", OutputPath = "" };
        Assert.Throws<InvalidDataException>(() => invalid.Validate());
    }
}

public class PortableCleanupCatalogTests
{
    [Fact]
    public void Presets_return_correct_option_counts()
    {
        var tiny11 = PortableCleanupCatalog.GetPreset("tiny11");
        var minimal = PortableCleanupCatalog.GetPreset("minimal");
        var balanced = PortableCleanupCatalog.GetPreset("balanced");
        var all = PortableCleanupCatalog.GetPreset("all");
        var none = PortableCleanupCatalog.GetPreset("none");

        Assert.NotEmpty(tiny11);
        Assert.NotEmpty(minimal);
        Assert.NotEmpty(balanced);
        Assert.NotEmpty(all);
        Assert.Empty(none);

        // Minimal preset removes more or equal to tiny11
        Assert.True(minimal.Count >= tiny11.Count);
        // All contains everything
        Assert.Equal(PortableCleanupCatalog.Options.Count, all.Count);
    }

    [Fact]
    public void Edge_and_OneDrive_are_present_in_catalog()
    {
        var edge = PortableCleanupCatalog.Options.FirstOrDefault(o => o.Id == "edge-files");
        var onedrive = PortableCleanupCatalog.Options.FirstOrDefault(o => o.Id == "onedrive-files");

        Assert.NotNull(edge);
        Assert.NotNull(onedrive);
        Assert.Equal(PortableCleanupCategory.SystemBinaries, edge.Category);
        Assert.Equal(PortableCleanupCategory.SystemBinaries, onedrive.Category);
    }

    [Fact]
    public void App_prefixes_are_defined_for_bundled_apps()
    {
        var apps = PortableCleanupCatalog.Options.Where(o => o.Category == PortableCleanupCategory.BundledApps).ToList();
        Assert.NotEmpty(apps);
        foreach (var app in apps)
        {
            Assert.NotEmpty(app.TargetPrefixes);
        }
    }
}

public class PortableUnattendBuilderTests
{
    [Fact]
    public void Generates_valid_unattend_xml_with_hardware_bypass()
    {
        var xml = PortableUnattendBuilder.Create(
            architecture: "x64",
            bypassHardwareRequirements: true,
            enableLocalAccount: true,
            compactInstallation: true);

        Assert.Contains("xmlns=\"urn:schemas-microsoft-com:unattend\"", xml);
        Assert.Contains("processorArchitecture=\"amd64\"", xml);
        Assert.Contains("<Compact>true</Compact>", xml);
        Assert.Contains("<HideOnlineAccountScreens>true</HideOnlineAccountScreens>", xml);
        Assert.Contains("BypassTPMCheck", xml);
        Assert.Contains("BypassSecureBootCheck", xml);
        Assert.Contains("BypassCPUCheck", xml);
        Assert.Contains("BypassRAMCheck", xml);
        Assert.Contains("BypassStorageCheck", xml);
    }

    [Fact]
    public void Maps_arm64_architecture_correctly()
    {
        var xml = PortableUnattendBuilder.Create(
            architecture: "arm64",
            bypassHardwareRequirements: false,
            enableLocalAccount: false,
            compactInstallation: false);

        Assert.Contains("processorArchitecture=\"arm64\"", xml);
        Assert.Contains("<Compact>false</Compact>", xml);
        Assert.DoesNotContain("<HideOnlineAccountScreens>true</HideOnlineAccountScreens>", xml);
        Assert.DoesNotContain("BypassTPMCheck", xml);
    }
}

public class PortableSetupCompleteBuilderTests
{
    [Fact]
    public void Generates_batch_script_with_selected_tweaks()
    {
        var script = PortableSetupCompleteBuilder.Create(
            bypassHardwareRequirements: true,
            enableLocalAccount: true,
            disableSponsoredApps: true,
            disableTelemetry: true,
            disableCopilot: true,
            disableWidgets: true,
            disableBitLockerDeviceEncryption: true,
            disableReservedStorage: true,
            removeScheduledTasks: true);

        Assert.StartsWith("@echo off", script);
        Assert.Contains("BypassTPMCheck", script);
        Assert.Contains("BypassNRO", script);
        Assert.Contains("DisableWindowsConsumerFeatures", script);
        Assert.Contains("AllowTelemetry", script);
        Assert.Contains("TurnOffWindowsCopilot", script);
        Assert.Contains("AllowNewsAndInterests", script);
        Assert.Contains("PreventDeviceEncryption", script);
        Assert.Contains("ShippedWithReserves", script);
        Assert.Contains("reg load HKLM\\TEMP_DEFAULT", script);
        Assert.Contains("reg unload HKLM\\TEMP_DEFAULT", script);
        Assert.Contains("Microsoft Compatibility Appraiser", script);
        Assert.EndsWith("exit /b 0\r\n", script);
    }
}

public class IsoImageStructureTests
{
    [Fact]
    public async Task Validates_iso_volume_descriptor()
    {
        // 16 sectors of 2048 = 32768 bytes, then at 32768 + 1: "CD001"
        var buffer = new byte[2048 * 18];
        var magic = System.Text.Encoding.ASCII.GetBytes("CD001");
        Array.Copy(magic, 0, buffer, 2048 * 16 + 1, magic.Length);

        using var stream = new MemoryStream(buffer);
        await IsoImageStructure.ValidateAsync(stream, CancellationToken.None);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task Throws_on_invalid_or_truncated_stream()
    {
        using var smallStream = new MemoryStream(new byte[100]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            IsoImageStructure.ValidateAsync(smallStream, CancellationToken.None));
    }
}

public class OfficialMediaTests
{
    [Fact]
    public void Validates_microsoft_official_urls()
    {
        var valid = "https://software.download.prss.microsoft.com/dbazure/Win11_24H2_English_x64.iso";
        var uri = OfficialIsoDownloader.ValidateUrl(valid);
        Assert.Equal("software.download.prss.microsoft.com", uri.Host);

        Assert.Throws<InvalidDataException>(() => OfficialIsoDownloader.ValidateUrl("http://insecure.microsoft.com/win11.iso"));
        Assert.Throws<InvalidDataException>(() => OfficialIsoDownloader.ValidateUrl("https://thirdparty.com/win11.iso"));
        Assert.Throws<InvalidDataException>(() => OfficialIsoDownloader.ValidateUrl("https://software-download.microsoft.com/win11.exe"));
    }

    [Fact]
    public void Validates_sha256_hashes()
    {
        var valid = "A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2";
        Assert.Equal(valid, OfficialIsoDownloader.ValidateHash(valid));
        Assert.Null(OfficialIsoDownloader.ValidateHash(""));
        Assert.Null(OfficialIsoDownloader.ValidateHash(null));
        Assert.Throws<InvalidDataException>(() => OfficialIsoDownloader.ValidateHash("too_short"));
        Assert.Throws<InvalidDataException>(() => OfficialIsoDownloader.ValidateHash(new string('Z', 64)));
    }
}
