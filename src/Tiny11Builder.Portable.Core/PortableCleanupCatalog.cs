namespace Tiny11Builder.Portable;

public enum PortableCleanupCategory
{
    BundledApps,
    SystemBinaries,
    ScheduledTasks
}

public sealed record PortableCleanupOption(
    string Id,
    string Title,
    string Description,
    PortableCleanupCategory Category,
    IReadOnlyList<string> TargetPrefixes,
    bool InTiny11,
    bool InMinimal,
    bool InBalanced);

public static class PortableCleanupCatalog
{
    public static IReadOnlyList<PortableCleanupOption> Options { get; } =
    [
        // Bundled Apps (from original tiny11maker script & WinUI catalog)
        new(
            "copilot-app",
            "Copilot and Windows AI apps",
            "Remove preinstalled Copilot apps and AI shortcuts.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.Copilot", "Microsoft.Windows.Copilot", "Microsoft.549981C3F5F10"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "outlook",
            "New Outlook and Mail",
            "Remove New Outlook and the retired Mail / Calendar apps.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.OutlookForWindows", "microsoft.windowscommunicationsapps"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "teams",
            "Microsoft Teams",
            "Remove personal Teams desktop clients.",
            PortableCleanupCategory.BundledApps,
            ["MSTeams", "MicrosoftTeams", "Microsoft.Windows.Teams"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "xbox",
            "Xbox apps and Gaming Overlays",
            "Remove Xbox App, Game Bar, and gaming speech/overlay stubs.",
            PortableCleanupCategory.BundledApps,
            [
                "Microsoft.GamingApp", "Microsoft.XboxApp", "Microsoft.Xbox.TCUI",
                "Microsoft.XboxGameOverlay", "Microsoft.XboxGamingOverlay",
                "Microsoft.XboxIdentityProvider", "Microsoft.XboxSpeechToTextOverlay"
            ],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "news-weather",
            "News, Weather and Bing Search",
            "Remove MSN News, Weather, and Bing feed apps.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.BingSearch"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "office-promos",
            "Microsoft 365 promotions",
            "Remove Office Hub and Office promotional push notifications.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.MicrosoftOfficeHub", "Microsoft.OfficePushNotificationUtility"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "clipchamp",
            "Clipchamp video editor",
            "Remove the bundled web video editor.",
            PortableCleanupCategory.BundledApps,
            ["Clipchamp.Clipchamp"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "solitaire",
            "Solitaire Collection",
            "Remove bundled casual games.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.MicrosoftSolitaireCollection"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "phone",
            "Phone Link and Cross-Device",
            "Remove phone integration apps and mobile link stubs.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.YourPhone", "Microsoft.Windows.CrossDevice"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "feedback",
            "Feedback Hub, Tips and Get Help",
            "Remove diagnostic feedback and promotional introductory apps.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.WindowsFeedbackHub", "Microsoft.GetHelp", "Microsoft.Getstarted"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "devhome",
            "Dev Home and Power Automate",
            "Remove Dev Home and Power Automate Desktop.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.Windows.DevHome", "Microsoft.PowerAutomateDesktop"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "social",
            "People, To Do, Family and Skype",
            "Remove contacts, personal organizer, and consumer Skype apps.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.People", "Microsoft.Todos", "Microsoft.SkypeApp", "MicrosoftCorporationII.MicrosoftFamily"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "maps",
            "Windows Maps and Mixed Reality",
            "Remove offline maps and Mixed Reality portal clients.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.WindowsMaps", "Microsoft.MixedReality.Portal"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "media",
            "Media Player and Movies & TV",
            "Remove Zune media playback applications (codecs remain).",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.ZuneMusic", "Microsoft.ZuneVideo"],
            InTiny11: true, InMinimal: true, InBalanced: false),

        new(
            "utilities",
            "Clock, Sound Recorder and Sticky Notes",
            "Remove bundled personal desk accessories.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.WindowsAlarms", "Microsoft.WindowsSoundRecorder", "Microsoft.MicrosoftStickyNotes"],
            InTiny11: false, InMinimal: true, InBalanced: false),

        new(
            "quickassist",
            "Quick Assist",
            "Remove Microsoft's remote-assistance client.",
            PortableCleanupCategory.BundledApps,
            ["MicrosoftCorporationII.QuickAssist"],
            InTiny11: false, InMinimal: true, InBalanced: false),

        new(
            "paint",
            "Paint, 3D Viewer and OneNote",
            "Remove Paint, 3D Viewer and OneNote Windows App.",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.Paint", "Microsoft.MSPaint", "Microsoft.Microsoft3DViewer", "Microsoft.Office.OneNote"],
            InTiny11: false, InMinimal: false, InBalanced: false),

        new(
            "camera",
            "Windows Camera",
            "Remove bundled Camera app (camera drivers are kept).",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.WindowsCamera"],
            InTiny11: false, InMinimal: false, InBalanced: false),

        new(
            "terminal",
            "Windows Terminal",
            "Remove modern Terminal (standard Command Prompt and PowerShell remain).",
            PortableCleanupCategory.BundledApps,
            ["Microsoft.WindowsTerminal"],
            InTiny11: false, InMinimal: false, InBalanced: false),

        new(
            "oem",
            "OEM and audio promotional apps",
            "Remove Intel management and Dolby promotional apps (not drivers).",
            PortableCleanupCategory.BundledApps,
            ["AppUp.IntelManagementandSecurityStatus", "DolbyLaboratories.DolbyAccess", "DolbyLaboratories.DolbyDigitalPlusDecoderOEM"],
            InTiny11: true, InMinimal: true, InBalanced: true),

        // System Binaries
        new(
            "edge-files",
            "Microsoft Edge & WebView2 files",
            "Delete Edge browser, EdgeCore, and WebView2 files from the image.",
            PortableCleanupCategory.SystemBinaries,
            [],
            InTiny11: true, InMinimal: true, InBalanced: true),

        new(
            "onedrive-files",
            "OneDrive setup binaries",
            "Remove OneDriveSetup.exe from System32 and SysWOW64.",
            PortableCleanupCategory.SystemBinaries,
            [],
            InTiny11: true, InMinimal: true, InBalanced: true),

        // Scheduled Tasks
        new(
            "tasks-telemetry",
            "Telemetry & Appraiser scheduled tasks",
            "Delete Compatibility Appraiser, Customer Experience (CEIP), and Chkdsk Proxy tasks.",
            PortableCleanupCategory.ScheduledTasks,
            [],
            InTiny11: true, InMinimal: true, InBalanced: true)
    ];

    public static HashSet<string> GetPreset(string preset) => preset.ToLowerInvariant() switch
    {
        "tiny11" or "original" => Options.Where(o => o.InTiny11).Select(o => o.Id).ToHashSet(),
        "minimal" or "core" => Options.Where(o => o.InMinimal).Select(o => o.Id).ToHashSet(),
        "balanced" => Options.Where(o => o.InBalanced).Select(o => o.Id).ToHashSet(),
        "all" => Options.Select(o => o.Id).ToHashSet(),
        "none" => [],
        _ => Options.Where(o => o.InTiny11).Select(o => o.Id).ToHashSet()
    };
}
