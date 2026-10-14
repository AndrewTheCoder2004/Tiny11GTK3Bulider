using System.Text.Json;

namespace Tiny11Builder.Portable;

public sealed record PortableRecipe
{
    public int SchemaVersion { get; init; } = 2;
    public string Mode { get; init; } = "tiny11";
    public string SourcePath { get; init; } = "";
    public string WorkspacePath { get; init; } = "";
    public string OutputPath { get; init; } = "";
    public int ImageIndex { get; init; } = 1;

    public List<string> SelectedRemovals { get; init; } = [];

    public bool BypassHardwareRequirements { get; init; } = true;
    public bool EnableLocalAccount { get; init; } = true;
    public bool CompactInstallation { get; init; } = true;
    public bool DisableSponsoredApps { get; init; } = true;
    public bool DisableTelemetry { get; init; } = true;
    public bool DisableCopilot { get; init; } = true;
    public bool DisableWidgets { get; init; } = true;
    public bool DisableBitLockerDeviceEncryption { get; init; } = true;
    public bool DisableReservedStorage { get; init; } = true;

    // Legacy schema version 1 compatibility helpers
    public bool RemoveEdgeBinaries
    {
        get => SelectedRemovals.Contains("edge-files");
        init
        {
            if (value && !SelectedRemovals.Contains("edge-files")) SelectedRemovals.Add("edge-files");
            else if (!value) SelectedRemovals.Remove("edge-files");
        }
    }

    public bool RemoveOneDriveSetup
    {
        get => SelectedRemovals.Contains("onedrive-files");
        init
        {
            if (value && !SelectedRemovals.Contains("onedrive-files")) SelectedRemovals.Add("onedrive-files");
            else if (!value) SelectedRemovals.Remove("onedrive-files");
        }
    }

    public static PortableRecipe Read(string path)
    {
        var json = File.ReadAllText(path);
        var recipe = JsonSerializer.Deserialize<PortableRecipe>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The recipe file is empty.");
        recipe.Validate();
        return recipe;
    }

    public void Write(string path)
    {
        Validate();
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Validate()
    {
        if (SchemaVersion < 1 || SchemaVersion > 2 ||
            string.IsNullOrWhiteSpace(SourcePath) ||
            string.IsNullOrWhiteSpace(WorkspacePath) ||
            string.IsNullOrWhiteSpace(OutputPath) ||
            ImageIndex < 1)
        {
            throw new InvalidDataException("This portable recipe has invalid settings or an unsupported version.");
        }
    }
}
