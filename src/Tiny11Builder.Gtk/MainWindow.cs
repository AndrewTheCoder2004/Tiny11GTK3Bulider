using System.Globalization;
using System.Runtime.InteropServices;
using Gtk;
using Tiny11Builder.Portable;

namespace Tiny11Builder.Gtk;

internal sealed class MainWindow : Window
{
    private readonly PortableBuildEngine engine = new();
    private readonly Notebook steps = new();

    // Navigation & Sidebar
    private readonly ListBox stepRail = new();
    private readonly Label currentStepLabel = new("Step 1 of 6: Welcome");
    private readonly Label selectedImageBadge = new("No image selected");
    private readonly Label selectedImageDetails = new("Select source media in Step 2");
    private readonly Label toolStatusLabel = new(PortableBuildEngine.GetToolRequirements());
    private readonly Button backButton = new("← Back");
    private readonly Button nextButton = new("Next →");
    private readonly Button startButton = new("⚡ Build ISO");
    private readonly Button cancelButton = new("✕ Cancel");
    private readonly InfoBar noticeBar = new();
    private readonly Label noticeLabel = new();

    // Step 1: Mode
    private readonly RadioButton modeTiny11 = new("Tiny11 (Recommended)");
    private readonly RadioButton modeMinimal;
    private readonly RadioButton modeCustom;

    // Step 2: Source & Image
    private readonly Entry sourcePath = new();
    private readonly Button inspectButton = new("Inspect Editions");
    private readonly Button downloadButton = new("Download Latest Windows 11 from Microsoft");
    private readonly ComboBoxText downloadArchChoice = new();
    private readonly ProgressBar downloadProgress = new();
    private readonly Label downloadSpeedLabel = new("");
    private readonly ComboBoxText editionChoice = new();
    private IReadOnlyList<WindowsEdition> editions = [];
    private int? preferredEdition;

    // Step 3: Components & Apps
    private readonly Dictionary<string, CheckButton> removalCheckboxes = new();
    private readonly Label removalCountLabel = new("0 items selected for removal");

    // Step 4: Settings & Tweaks
    private readonly CheckButton checkBypassHardware = new("Bypass hardware requirements (TPM 2.0, Secure Boot, RAM, CPU, Storage)");
    private readonly CheckButton checkLocalAccount = new("Enable local account creation (Bypass Microsoft Account in OOBE)");
    private readonly CheckButton checkCompactOs = new("Enable Compact OS installation (compress system binaries)");
    private readonly CheckButton checkDisableAds = new("Suppress ads, suggestions, and sponsored apps");
    private readonly CheckButton checkDisableTelemetry = new("Disable diagnostic data, telemetry, and advertising ID");
    private readonly CheckButton checkDisableCopilot = new("Disable Windows Copilot and Recall by policy");
    private readonly CheckButton checkDisableWidgets = new("Disable Widgets, Chat, and New Teams auto-install");
    private readonly CheckButton checkDisableBitLocker = new("Disable automatic BitLocker device encryption");
    private readonly CheckButton checkDisableReservedStorage = new("Disable reserved storage for updates");
    private readonly Entry workspacePath = new();
    private readonly Entry outputPath = new();

    // Step 5: Review
    private readonly TextView reviewTextView = new();

    // Step 6: Build Progress
    private readonly ProgressBar buildProgressBar = new();
    private readonly Label buildStatusLabel = new("Ready to start.");
    private readonly TextView buildLog = new();
    private readonly Box buildSuccessBox = new(Orientation.Vertical, 8);
    private readonly Entry sha256Entry = new();
    private readonly Button copyHashButton = new("Copy SHA-256");

    private CancellationTokenSource? operation;
    private bool busy;
    private bool closeWhenCancelled;

    public MainWindow() : base("Tiny11 Builder — Portable GTK3 (Linux, macOS, BSD)")
    {
        SetDefaultSize(1100, 740);
        SetPosition(WindowPosition.Center);
        BorderWidth = 14;

        // Initialize radio group
        modeMinimal = new RadioButton(modeTiny11, "Tiny11 Minimal");
        modeCustom = new RadioButton(modeTiny11, "Custom Configuration");

        // Apply GTK3 CSS styling
        ApplyTheme();

        // Default paths
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultWorkspace = Path.Combine(home, "Tiny11Builder");
        workspacePath.Text = defaultWorkspace;
        outputPath.Text = Path.Combine(defaultWorkspace, "Tiny11-portable.iso");

        // Main layout container: Horizontal split (Sidebar | Content)
        var root = new Box(Orientation.Horizontal, 16);
        Add(root);

        // Sidebar
        root.PackStart(BuildSidebar(), false, false, 0);

        // Content Area
        var contentArea = new Box(Orientation.Vertical, 10);
        root.PackStart(contentArea, true, true, 0);

        // Shell Top Bar
        contentArea.PackStart(BuildShellTopBar(), false, false, 0);

        // InfoBar / Notice
        noticeBar.MessageArea.Add(noticeLabel);
        noticeBar.ShowCloseButton = true;
        noticeBar.NoShowAll = true;
        noticeBar.Response += (_, _) => noticeBar.Hide();
        contentArea.PackStart(noticeBar, false, false, 0);

        // Notebook pages
        steps.ShowTabs = false;
        steps.ShowBorder = false;
        contentArea.PackStart(steps, true, true, 0);

        AddPage("Welcome", BuildWelcomePage());
        AddPage("Windows Image", BuildSourcePage());
        AddPage("Components", BuildComponentsPage());
        AddPage("Settings", BuildSettingsPage());
        AddPage("Review", BuildReviewPage());
        AddPage("Build ISO", BuildProgressPage());

        // Event wiring
        backButton.Clicked += (_, _) => MovePage(-1);
        nextButton.Clicked += (_, _) => MovePage(1);
        startButton.Clicked += async (_, _) => await StartBuildAsync();
        cancelButton.Clicked += (_, _) => operation?.Cancel();
        steps.SwitchPage += (_, args) => OnPageChanged((int)args.PageNum);

        modeTiny11.Toggled += (_, _) => { if (modeTiny11.Active) ApplyPreset("tiny11"); };
        modeMinimal.Toggled += (_, _) => { if (modeMinimal.Active) ApplyPreset("minimal"); };
        modeCustom.Toggled += (_, _) => { if (modeCustom.Active) ApplyPreset("custom"); };

        sourcePath.Changed += (_, _) =>
        {
            editions = [];
            preferredEdition = null;
            editionChoice.RemoveAll();
            UpdateImageBadge();
        };

        editionChoice.Changed += (_, _) => UpdateImageBadge();

        DeleteEvent += (_, args) =>
        {
            if (busy)
            {
                closeWhenCancelled = true;
                operation?.Cancel();
                ShowNotice("Cancelling active native operation before exiting...", MessageType.Warning);
                args.RetVal = true;
            }
            else
            {
                Application.Quit();
            }
        };

        // Initialize presets & state
        ApplyPreset("tiny11");
        UpdateNavigation(0);
    }

    private Widget BuildSidebar()
    {
        var sidebar = new Box(Orientation.Vertical, 12) { WidthRequest = 260 };
        sidebar.StyleContext.AddClass("sidebar");

        // Branding
        var brandBox = new Box(Orientation.Vertical, 2);
        var brandTitle = new Label("Tiny11 Builder") { Xalign = 0 };
        brandTitle.StyleContext.AddClass("brand-title");
        var brandSub = new Label("Portable Linux · macOS · BSD") { Xalign = 0 };
        brandSub.StyleContext.AddClass("brand-sub");
        brandBox.PackStart(brandTitle, false, false, 0);
        brandBox.PackStart(brandSub, false, false, 0);
        sidebar.PackStart(brandBox, false, false, 4);

        // Step rail (List)
        stepRail.SelectionMode = SelectionMode.Single;
        stepRail.StyleContext.AddClass("step-rail");

        string[] stepTitles =
        [
            "1. Welcome & Mode",
            "2. Windows Image",
            "3. Components & Apps",
            "4. Settings & Tweaks",
            "5. Review & Recipes",
            "6. Build ISO"
        ];

        foreach (var title in stepTitles)
        {
            var row = new ListBoxRow();
            var lbl = new Label(title) { Xalign = 0, MarginStart = 8, MarginTop = 6, MarginBottom = 6 };
            row.Add(lbl);
            stepRail.Add(row);
        }

        stepRail.RowActivated += (_, args) =>
        {
            if (!busy) steps.CurrentPage = args.Row.Index;
        };

        sidebar.PackStart(stepRail, false, false, 0);

        // Image badge card
        var card = new Box(Orientation.Vertical, 4) { BorderWidth = 10 };
        card.StyleContext.AddClass("card");
        var cardHeader = new Label("SELECTED IMAGE") { Xalign = 0 };
        cardHeader.StyleContext.AddClass("card-header-label");
        selectedImageBadge.Xalign = 0;
        selectedImageBadge.LineWrap = true;
        selectedImageBadge.StyleContext.AddClass("badge-title");
        selectedImageDetails.Xalign = 0;
        selectedImageDetails.LineWrap = true;
        selectedImageDetails.StyleContext.AddClass("badge-detail");
        var untouchedLabel = new Label("Source media is never modified.") { Xalign = 0 };
        untouchedLabel.StyleContext.AddClass("badge-sub");

        card.PackStart(cardHeader, false, false, 0);
        card.PackStart(selectedImageBadge, false, false, 0);
        card.PackStart(selectedImageDetails, false, false, 0);
        card.PackStart(untouchedLabel, false, false, 0);
        sidebar.PackStart(card, false, false, 0);

        // Spacer
        sidebar.PackStart(new Box(Orientation.Vertical, 0), true, true, 0);

        // Tool status
        toolStatusLabel.Xalign = 0;
        toolStatusLabel.LineWrap = true;
        toolStatusLabel.StyleContext.AddClass("tool-status");
        sidebar.PackStart(toolStatusLabel, false, false, 0);

        // Upstream link
        var linkButton = new LinkButton("https://github.com/ntdevlabs/tiny11builder", "Upstream GitHub Project ↗")
        {
            Xalign = 0
        };
        sidebar.PackStart(linkButton, false, false, 0);

        return sidebar;
    }

    private Widget BuildShellTopBar()
    {
        var topBar = new Box(Orientation.Horizontal, 10);
        topBar.StyleContext.AddClass("topbar");

        topBar.PackStart(backButton, false, false, 0);
        currentStepLabel.Xalign = 0;
        currentStepLabel.StyleContext.AddClass("step-indicator");
        topBar.PackStart(currentStepLabel, true, true, 8);

        topBar.PackStart(cancelButton, false, false, 0);
        topBar.PackStart(nextButton, false, false, 0);
        topBar.PackStart(startButton, false, false, 0);

        return topBar;
    }

    private Widget BuildWelcomePage()
    {
        var box = PageBox();
        box.PackStart(PageHeading("Build a Debloated Windows 11 Image"), false, false, 0);
        box.PackStart(PageBody("Tiny11 Builder allows you to create a lightweight, private, and portable Windows 11 installation image directly on Linux, macOS, and BSD without requiring Windows or virtual machines."), false, false, 0);

        var modeCard = new Box(Orientation.Vertical, 8) { BorderWidth = 12 };
        modeCard.StyleContext.AddClass("card");
        var modeHeader = new Label("Choose Build Mode:") { Xalign = 0 };
        modeHeader.StyleContext.AddClass("card-title");
        modeCard.PackStart(modeHeader, false, false, 0);

        modeTiny11.StyleContext.AddClass("mode-radio");
        modeMinimal.StyleContext.AddClass("mode-radio");
        modeCustom.StyleContext.AddClass("mode-radio");

        modeCard.PackStart(modeTiny11, false, false, 0);
        modeCard.PackStart(new Label("  • Recommended: Removes Edge, OneDrive, promotional apps, telemetry, and enables hardware bypasses.") { Xalign = 0 }, false, false, 0);

        modeCard.PackStart(modeMinimal, false, false, 0);
        modeCard.PackStart(new Label("  • Aggressive: Removes non-essential accessories (Clock, Media, QuickAssist) for minimum footprint.") { Xalign = 0 }, false, false, 0);

        modeCard.PackStart(modeCustom, false, false, 0);
        modeCard.PackStart(new Label("  • Fully customizable: Select individual packages, system binaries, and policy tweaks.") { Xalign = 0 }, false, false, 0);

        box.PackStart(modeCard, false, false, 0);

        var noticeCard = new Box(Orientation.Vertical, 6) { BorderWidth = 10 };
        noticeCard.StyleContext.AddClass("info-card");
        noticeCard.PackStart(new Label("Native POSIX Tools Used:") { Xalign = 0, StyleContext = { "card-title" } }, false, false, 0);
        noticeCard.PackStart(new Label("This portable edition uses wimlib-imagex for direct WIM package removals, autounattend.xml and SetupComplete.cmd injection, and xorriso for dual-boot BIOS/UEFI ISO creation.") { Xalign = 0, LineWrap = true }, false, false, 0);
        box.PackStart(noticeCard, false, false, 0);

        return box;
    }

    private Widget BuildSourcePage()
    {
        var box = PageBox();
        box.PackStart(PageHeading("Select or Download Windows Media"), false, false, 0);
        box.PackStart(PageBody("Choose an existing Windows 11 ISO file, an extracted installation folder, or download directly from Microsoft."), false, false, 0);

        // Local media picker
        var localBox = new Box(Orientation.Vertical, 8) { BorderWidth = 12 };
        localBox.StyleContext.AddClass("card");
        localBox.PackStart(new Label("Local Windows 11 Installation Media:") { Xalign = 0, StyleContext = { "card-title" } }, false, false, 0);
        localBox.PackStart(sourcePath, false, false, 0);

        var browseRow = new Box(Orientation.Horizontal, 8);
        var chooseIso = new Button("Choose ISO File…");
        var chooseFolder = new Button("Choose Extracted Folder…");
        chooseIso.Clicked += (_, _) => ChooseSource(FileChooserAction.Open);
        chooseFolder.Clicked += (_, _) => ChooseSource(FileChooserAction.SelectFolder);
        inspectButton.Clicked += async (_, _) => await InspectAsync();

        browseRow.PackStart(chooseIso, false, false, 0);
        browseRow.PackStart(chooseFolder, false, false, 0);
        browseRow.PackStart(inspectButton, false, false, 0);
        localBox.PackStart(browseRow, false, false, 0);
        box.PackStart(localBox, false, false, 0);

        // Downloader section
        var downloadCard = new Box(Orientation.Vertical, 8) { BorderWidth = 12 };
        downloadCard.StyleContext.AddClass("card");
        downloadCard.PackStart(new Label("Official Microsoft Windows 11 Downloader:") { Xalign = 0, StyleContext = { "card-title" } }, false, false, 0);
        downloadCard.PackStart(new Label("Directly downloads the latest official Windows 11 ISO from Microsoft's servers with automatic hash verification and resumable downloads.") { Xalign = 0, LineWrap = true }, false, false, 0);

        var downloadControls = new Box(Orientation.Horizontal, 8);
        downloadArchChoice.Append("x64", "x64 (Intel/AMD 64-bit)");
        downloadArchChoice.Append("arm64", "ARM64 (Apple Silicon / Snapdragon)");
        downloadArchChoice.ActiveId = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        downloadControls.PackStart(downloadArchChoice, false, false, 0);

        downloadButton.Clicked += async (_, _) => await DownloadLatestAsync();
        downloadControls.PackStart(downloadButton, false, false, 0);
        downloadCard.PackStart(downloadControls, false, false, 0);

        downloadProgress.NoShowAll = true;
        downloadSpeedLabel.NoShowAll = true;
        downloadCard.PackStart(downloadProgress, false, false, 0);
        downloadCard.PackStart(downloadSpeedLabel, false, false, 0);
        box.PackStart(downloadCard, false, false, 0);

        // Editions picker
        var editionCard = new Box(Orientation.Vertical, 8) { BorderWidth = 12 };
        editionCard.StyleContext.AddClass("card");
        editionCard.PackStart(new Label("Discovered Windows Editions in Selected Media:") { Xalign = 0, StyleContext = { "card-title" } }, false, false, 0);
        editionCard.PackStart(editionChoice, false, false, 0);
        box.PackStart(editionCard, false, false, 0);

        return box;
    }

    private Widget BuildComponentsPage()
    {
        var box = PageBox();
        box.PackStart(PageHeading("Remove Components & Bundled Apps"), false, false, 0);
        box.PackStart(PageBody("Select which apps and binaries to purge from the exported image. Tiny11 removes these files and applies clean post-install removals."), false, false, 0);

        // Presets toolbar
        var presetRow = new Box(Orientation.Horizontal, 8);
        var btnTiny11 = new Button("Tiny11 Preset");
        var btnMinimal = new Button("Minimal Preset");
        var btnBalanced = new Button("Balanced Preset");
        var btnAll = new Button("Select All");
        var btnNone = new Button("Clear All");

        btnTiny11.Clicked += (_, _) => ApplyPreset("tiny11");
        btnMinimal.Clicked += (_, _) => ApplyPreset("minimal");
        btnBalanced.Clicked += (_, _) => ApplyPreset("balanced");
        btnAll.Clicked += (_, _) => ApplyPreset("all");
        btnNone.Clicked += (_, _) => ApplyPreset("none");

        presetRow.PackStart(btnTiny11, false, false, 0);
        presetRow.PackStart(btnMinimal, false, false, 0);
        presetRow.PackStart(btnBalanced, false, false, 0);
        presetRow.PackStart(btnAll, false, false, 0);
        presetRow.PackStart(btnNone, false, false, 0);
        box.PackStart(presetRow, false, false, 0);

        removalCountLabel.Xalign = 0;
        removalCountLabel.StyleContext.AddClass("count-label");
        box.PackStart(removalCountLabel, false, false, 0);

        // Scrolled checkboxes
        var scroller = new ScrolledWindow { ShadowType = ShadowType.In, HeightRequest = 340 };
        scroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        var checkContainer = new Box(Orientation.Vertical, 6) { BorderWidth = 8 };

        // Group by category
        var categories = new[]
        {
            (PortableCleanupCategory.BundledApps, "Bundled Applications"),
            (PortableCleanupCategory.SystemBinaries, "System Binaries & Browsers"),
            (PortableCleanupCategory.ScheduledTasks, "Scheduled Telemetry Tasks")
        };

        foreach (var (cat, catTitle) in categories)
        {
            var header = new Label(catTitle) { Xalign = 0 };
            header.StyleContext.AddClass("category-header");
            checkContainer.PackStart(header, false, false, 4);

            foreach (var opt in PortableCleanupCatalog.Options.Where(o => o.Category == cat))
            {
                var check = new CheckButton($"{opt.Title} — {opt.Description}");
                check.Toggled += (_, _) => UpdateRemovalCount();
                removalCheckboxes[opt.Id] = check;
                checkContainer.PackStart(check, false, false, 1);
            }
        }

        scroller.Add(checkContainer);
        box.PackStart(scroller, true, true, 0);

        return box;
    }

    private Widget BuildSettingsPage()
    {
        var box = PageBox();
        box.PackStart(PageHeading("Windows Setup & Tweaks"), false, false, 0);
        box.PackStart(PageBody("Configure unattended install settings, hardware bypasses, and system debloat policies."), false, false, 0);

        var scroller = new ScrolledWindow { ShadowType = ShadowType.None, HeightRequest = 380 };
        scroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        var settingsBox = new Box(Orientation.Vertical, 8) { BorderWidth = 6 };

        var bypassCard = new Box(Orientation.Vertical, 6) { BorderWidth = 10 };
        bypassCard.StyleContext.AddClass("card");
        bypassCard.PackStart(new Label("Unattended Setup & Hardware Bypasses:") { Xalign = 0, StyleContext = { "card-title" } }, false, false, 0);
        checkBypassHardware.Active = true;
        checkLocalAccount.Active = true;
        checkCompactOs.Active = true;
        bypassCard.PackStart(checkBypassHardware, false, false, 0);
        bypassCard.PackStart(checkLocalAccount, false, false, 0);
        bypassCard.PackStart(checkCompactOs, false, false, 0);
        settingsBox.PackStart(bypassCard, false, false, 0);

        var privacyCard = new Box(Orientation.Vertical, 6) { BorderWidth = 10 };
        privacyCard.StyleContext.AddClass("card");
        privacyCard.PackStart(new Label("Privacy, Telemetry & AI Tweaks:") { Xalign = 0, StyleContext = { "card-title" } }, false, false, 0);
        checkDisableAds.Active = true;
        checkDisableTelemetry.Active = true;
        checkDisableCopilot.Active = true;
        checkDisableWidgets.Active = true;
        checkDisableBitLocker.Active = true;
        checkDisableReservedStorage.Active = true;
        privacyCard.PackStart(checkDisableAds, false, false, 0);
        privacyCard.PackStart(checkDisableTelemetry, false, false, 0);
        privacyCard.PackStart(checkDisableCopilot, false, false, 0);
        privacyCard.PackStart(checkDisableWidgets, false, false, 0);
        privacyCard.PackStart(checkDisableBitLocker, false, false, 0);
        privacyCard.PackStart(checkDisableReservedStorage, false, false, 0);
        settingsBox.PackStart(privacyCard, false, false, 0);

        var pathsCard = new Box(Orientation.Vertical, 8) { BorderWidth = 10 };
        pathsCard.StyleContext.AddClass("card");
        pathsCard.PackStart(new Label("Build Paths:") { Xalign = 0, StyleContext = { "card-title" } }, false, false, 0);

        pathsCard.PackStart(new Label("Workspace Staging Directory (at least 24 GiB free recommended):") { Xalign = 0 }, false, false, 0);
        var wsRow = new Box(Orientation.Horizontal, 6);
        wsRow.PackStart(workspacePath, true, true, 0);
        var wsBrowse = new Button("Browse…");
        wsBrowse.Clicked += (_, _) => ChooseDirectory(workspacePath, "Choose Workspace Folder");
        wsRow.PackStart(wsBrowse, false, false, 0);
        pathsCard.PackStart(wsRow, false, false, 0);

        pathsCard.PackStart(new Label("Output ISO Destination:") { Xalign = 0 }, false, false, 0);
        var outRow = new Box(Orientation.Horizontal, 6);
        outRow.PackStart(outputPath, true, true, 0);
        var outBrowse = new Button("Browse…");
        outBrowse.Clicked += (_, _) => ChooseOutputIso();
        outRow.PackStart(outBrowse, false, false, 0);
        pathsCard.PackStart(outRow, false, false, 0);

        settingsBox.PackStart(pathsCard, false, false, 0);
        scroller.Add(settingsBox);
        box.PackStart(scroller, true, true, 0);

        return box;
    }

    private Widget BuildReviewPage()
    {
        var box = PageBox();
        box.PackStart(PageHeading("Review Configuration & Recipes"), false, false, 0);
        box.PackStart(PageBody("Verify your build settings before creating the ISO image. You can also save or load recipes."), false, false, 0);

        var recipeRow = new Box(Orientation.Horizontal, 8);
        var saveRecipe = new Button("Save Recipe (JSON)…");
        var loadRecipe = new Button("Load Recipe (JSON)…");
        saveRecipe.Clicked += (_, _) => SaveRecipe();
        loadRecipe.Clicked += (_, _) => LoadRecipe();
        recipeRow.PackStart(saveRecipe, false, false, 0);
        recipeRow.PackStart(loadRecipe, false, false, 0);
        box.PackStart(recipeRow, false, false, 0);

        reviewTextView.Editable = false;
        reviewTextView.CursorVisible = false;
        reviewTextView.WrapMode = WrapMode.WordChar;
        reviewTextView.StyleContext.AddClass("review-log");

        var scroller = new ScrolledWindow { ShadowType = ShadowType.In };
        scroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        scroller.Add(reviewTextView);
        box.PackStart(scroller, true, true, 0);

        return box;
    }

    private Widget BuildProgressPage()
    {
        var box = PageBox();
        box.PackStart(PageHeading("Building ISO Image"), false, false, 0);

        buildStatusLabel.Xalign = 0;
        buildStatusLabel.StyleContext.AddClass("card-title");
        box.PackStart(buildStatusLabel, false, false, 0);

        buildProgressBar.PulseStep = 0.05;
        box.PackStart(buildProgressBar, false, false, 0);

        buildLog.Editable = false;
        buildLog.CursorVisible = false;
        buildLog.WrapMode = WrapMode.WordChar;
        buildLog.StyleContext.AddClass("build-log");

        var scroller = new ScrolledWindow { ShadowType = ShadowType.In };
        scroller.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
        scroller.Add(buildLog);
        box.PackStart(scroller, true, true, 0);

        // Success box
        buildSuccessBox.NoShowAll = true;
        buildSuccessBox.BorderWidth = 10;
        buildSuccessBox.StyleContext.AddClass("card");
        var successTitle = new Label("Build Complete!") { Xalign = 0 };
        successTitle.StyleContext.AddClass("success-title");
        buildSuccessBox.PackStart(successTitle, false, false, 0);

        var hashRow = new Box(Orientation.Horizontal, 8);
        hashRow.PackStart(new Label("SHA-256 Digest:"), false, false, 0);
        sha256Entry.IsEditable = false;
        hashRow.PackStart(sha256Entry, true, true, 0);
        copyHashButton.Clicked += (_, _) =>
        {
            var clipboard = Clipboard.Get(Gdk.Atom.Intern("CLIPBOARD", false));
            clipboard.Text = sha256Entry.Text;
            ShowNotice("SHA-256 copied to clipboard.", MessageType.Info);
        };
        hashRow.PackStart(copyHashButton, false, false, 0);
        buildSuccessBox.PackStart(hashRow, false, false, 0);

        box.PackStart(buildSuccessBox, false, false, 0);

        return box;
    }

    private void ApplyPreset(string preset)
    {
        var selected = PortableCleanupCatalog.GetPreset(preset);
        foreach (var (id, check) in removalCheckboxes)
        {
            check.Active = selected.Contains(id);
        }
        UpdateRemovalCount();
    }

    private void UpdateRemovalCount()
    {
        var count = removalCheckboxes.Values.Count(c => c.Active);
        removalCountLabel.Text = $"{count} component(s) / app(s) selected for removal";
    }

    private List<string> GetSelectedRemovalIds() =>
        removalCheckboxes.Where(kvp => kvp.Value.Active).Select(kvp => kvp.Key).ToList();

    private void UpdateImageBadge()
    {
        var selectedEdition = GetSelectedEdition();
        if (selectedEdition is not null)
        {
            selectedImageBadge.Text = selectedEdition.Name;
            selectedImageDetails.Text = $"{selectedEdition.Architecture} · {selectedEdition.Version} · Index {selectedEdition.Index}";
        }
        else if (File.Exists(sourcePath.Text) || Directory.Exists(sourcePath.Text))
        {
            selectedImageBadge.Text = Path.GetFileName(sourcePath.Text);
            selectedImageDetails.Text = "Inspect editions to select target image";
        }
        else
        {
            selectedImageBadge.Text = "No image selected";
            selectedImageDetails.Text = "Select source media in Step 2";
        }
    }

    private WindowsEdition? GetSelectedEdition()
    {
        if (int.TryParse(editionChoice.ActiveId, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return editions.FirstOrDefault(e => e.Index == index);
        return null;
    }

    private async Task InspectAsync()
    {
        if (string.IsNullOrWhiteSpace(sourcePath.Text))
        {
            ShowNotice("Select a Windows ISO or extracted media folder first.", MessageType.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(workspacePath.Text))
        {
            ShowNotice("Specify a workspace staging folder first.", MessageType.Warning);
            return;
        }

        SetBusy(true, "Inspecting Windows installation media…");
        try
        {
            using var source = NewOperation();
            editions = await engine.InspectAsync(sourcePath.Text, workspacePath.Text, LogProgress, source.Token);
            await OnUiAsync(() =>
            {
                editionChoice.RemoveAll();
                foreach (var ed in editions)
                {
                    editionChoice.Append(ed.Index.ToString(CultureInfo.InvariantCulture), ed.ToString());
                }

                if (editions.Count > 0)
                {
                    var chosen = editions.FirstOrDefault(item => item.Index == preferredEdition) ?? editions[0];
                    editionChoice.ActiveId = chosen.Index.ToString(CultureInfo.InvariantCulture);
                    UpdateImageBadge();
                    ShowNotice($"Found {editions.Count} Windows edition(s). Selected: {chosen.Name}", MessageType.Info);
                }
            });
        }
        catch (OperationCanceledException)
        {
            await OnUiAsync(() => ShowNotice("Inspection cancelled.", MessageType.Warning));
        }
        catch (Exception error)
        {
            await OnUiAsync(() => ShowNotice($"Inspection failed: {error.Message}", MessageType.Error));
        }
        finally
        {
            await OnUiAsync(FinishOperation);
        }
    }

    private async Task DownloadLatestAsync()
    {
        if (string.IsNullOrWhiteSpace(workspacePath.Text))
        {
            ShowNotice("Choose a workspace staging folder first.", MessageType.Warning);
            return;
        }

        var arch = downloadArchChoice.ActiveId ?? "x64";
        SetBusy(true, $"Connecting to Microsoft software download servers ({arch})…");
        downloadProgress.Fraction = 0;
        downloadProgress.Show();
        downloadSpeedLabel.Text = "Connecting…";
        downloadSpeedLabel.Show();

        try
        {
            using var source = NewOperation();
            var downloadsDir = Path.Combine(Path.GetFullPath(workspacePath.Text), "downloads");
            Directory.CreateDirectory(downloadsDir);

            using var catalogClient = MicrosoftMediaCatalog.CreateClient();
            var media = await new MicrosoftMediaCatalog(catalogClient).ResolveAsync(arch,
                CultureInfo.CurrentUICulture, LogProgress, source.Token);

            var destination = Path.Combine(downloadsDir, media.FileName);

            using var downloadClient = OfficialIsoDownloader.CreateClient();
            var result = await new OfficialIsoDownloader(downloadClient).DownloadAsync(
                media, destination, msg =>
                {
                    LogProgress(msg);
                    GLib.Idle.Add(() =>
                    {
                        downloadSpeedLabel.Text = msg;
                        downloadProgress.Pulse();
                        return false;
                    });
                }, source.Token);

            await OnUiAsync(() =>
            {
                sourcePath.Text = result.Path;
                downloadProgress.Fraction = 1.0;
                downloadSpeedLabel.Text = "Download completed successfully!";
                ShowNotice($"Downloaded official ISO: {media.FileName}. Click 'Inspect Editions' to proceed.", MessageType.Info);
            });
        }
        catch (OperationCanceledException)
        {
            await OnUiAsync(() => ShowNotice("Download cancelled. Partial file saved and resumable.", MessageType.Warning));
        }
        catch (Exception error)
        {
            await OnUiAsync(() => ShowNotice($"Download failed: {error.Message}", MessageType.Error));
        }
        finally
        {
            await OnUiAsync(FinishOperation);
        }
    }

    private async Task StartBuildAsync()
    {
        var edition = GetSelectedEdition();
        if (edition is null)
        {
            ShowNotice("Inspect media and select a Windows edition first in Step 2.", MessageType.Warning);
            steps.CurrentPage = 1;
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath.Text))
        {
            ShowNotice("Specify an output ISO path in Step 4.", MessageType.Warning);
            steps.CurrentPage = 3;
            return;
        }

        if (File.Exists(outputPath.Text))
        {
            using var confirm = new MessageDialog(this, DialogFlags.Modal, MessageType.Question, ButtonsType.YesNo,
                "The output ISO file already exists. Overwrite it?");
            if ((ResponseType)confirm.Run() != ResponseType.Yes)
            {
                confirm.Destroy();
                return;
            }
            confirm.Destroy();
        }

        buildLog.Buffer.Text = "";
        buildSuccessBox.Hide();
        steps.CurrentPage = 5;
        SetBusy(true, "Building ISO image. Cancellation safely halts native tools.");

        try
        {
            using var source = NewOperation();
            var request = new PortableBuildRequest(
                sourcePath.Text,
                workspacePath.Text,
                outputPath.Text,
                edition.Index,
                GetSelectedRemovalIds(),
                checkBypassHardware.Active,
                checkLocalAccount.Active,
                checkCompactOs.Active,
                checkDisableAds.Active,
                checkDisableTelemetry.Active,
                checkDisableCopilot.Active,
                checkDisableWidgets.Active,
                checkDisableBitLocker.Active,
                checkDisableReservedStorage.Active);

            var result = await engine.BuildAsync(request, LogProgress, source.Token);

            await OnUiAsync(() =>
            {
                buildProgressBar.Fraction = 1.0;
                buildStatusLabel.Text = "Build complete! Bootable ISO created successfully.";
                sha256Entry.Text = result.Sha256;
                buildSuccessBox.ShowAll();
                ShowNotice($"Build complete: {result.IsoPath}", MessageType.Info);
            });
        }
        catch (OperationCanceledException)
        {
            await OnUiAsync(() =>
            {
                buildStatusLabel.Text = "Build cancelled by user.";
                ShowNotice("Build cancelled. Staged files cleaned up.", MessageType.Warning);
            });
        }
        catch (Exception error)
        {
            await OnUiAsync(() =>
            {
                buildStatusLabel.Text = "Build failed.";
                ShowNotice($"Build failed: {error.Message}", MessageType.Error);
                LogProgress("ERROR: " + error);
            });
        }
        finally
        {
            await OnUiAsync(FinishOperation);
        }
    }

    private void UpdateReviewText()
    {
        var edition = GetSelectedEdition();
        var removals = GetSelectedRemovalIds();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("===============================================================");
        sb.AppendLine("                   TINY11 BUILD CONFIGURATION REVIEW           ");
        sb.AppendLine("===============================================================");
        sb.AppendLine();
        sb.AppendLine($"Platform Target: Linux, macOS, BSD (Native POSIX Tools)");
        sb.AppendLine($"Source Media:    {sourcePath.Text}");
        sb.AppendLine($"Edition Target:  {(edition is not null ? $"{edition.Name} ({edition.Architecture}) · Index {edition.Index}" : "Not selected")}");
        sb.AppendLine($"Output ISO:      {outputPath.Text}");
        sb.AppendLine($"Workspace Path:  {workspacePath.Text}");
        sb.AppendLine();
        sb.AppendLine("------------------ UNATTENDED & POLICY TWEAKS ------------------");
        sb.AppendLine($" • Hardware Requirements Bypass (TPM/CPU/RAM/SecureBoot): {(checkBypassHardware.Active ? "ENABLED" : "Disabled")}");
        sb.AppendLine($" • Local Account Support (BypassNRO in OOBE):             {(checkLocalAccount.Active ? "ENABLED" : "Disabled")}");
        sb.AppendLine($" • Compact OS Installation:                              {(checkCompactOs.Active ? "ENABLED" : "Disabled")}");
        sb.AppendLine($" • Disable Sponsored Apps & Cloud Content:                {(checkDisableAds.Active ? "ENABLED" : "Disabled")}");
        sb.AppendLine($" • Disable Telemetry & Diagnostic Logging:               {(checkDisableTelemetry.Active ? "ENABLED" : "Disabled")}");
        sb.AppendLine($" • Disable Copilot & Recall AI:                          {(checkDisableCopilot.Active ? "ENABLED" : "Disabled")}");
        sb.AppendLine($" • Disable Widgets & Chat:                               {(checkDisableWidgets.Active ? "ENABLED" : "Disabled")}");
        sb.AppendLine($" • Disable Automatic Device Encryption:                  {(checkDisableBitLocker.Active ? "ENABLED" : "Disabled")}");
        sb.AppendLine($" • Disable Reserved Storage:                             {(checkDisableReservedStorage.Active ? "ENABLED" : "Disabled")}");
        sb.AppendLine();
        sb.AppendLine($"---------------- COMPONENTS & APPS REMOVED ({removals.Count}) ----------------");
        foreach (var id in removals)
        {
            var opt = PortableCleanupCatalog.Options.FirstOrDefault(o => o.Id == id);
            if (opt is not null)
                sb.AppendLine($" • {opt.Title} ({opt.Category})");
            else
                sb.AppendLine($" • {id}");
        }
        sb.AppendLine();
        sb.AppendLine("===============================================================");
        sb.AppendLine("Click 'Build ISO' to begin image generation.");

        reviewTextView.Buffer.Text = sb.ToString();
    }

    private void SaveRecipe()
    {
        var edition = GetSelectedEdition();
        if (edition is null)
        {
            ShowNotice("Inspect media and select an edition before saving a recipe.", MessageType.Warning);
            return;
        }

        using var dialog = new FileChooserDialog("Save Portable Recipe", this, FileChooserAction.Save,
            "Cancel", ResponseType.Cancel, "Save", ResponseType.Accept);
        dialog.CurrentName = "tiny11-recipe.json";
        var filter = new FileFilter { Name = "JSON Recipe (*.json)" };
        filter.AddPattern("*.json");
        dialog.AddFilter(filter);

        if ((ResponseType)dialog.Run() == ResponseType.Accept)
        {
            var recipe = new PortableRecipe
            {
                Mode = modeTiny11.Active ? "tiny11" : (modeMinimal.Active ? "minimal" : "custom"),
                SourcePath = sourcePath.Text,
                WorkspacePath = workspacePath.Text,
                OutputPath = outputPath.Text,
                ImageIndex = edition.Index,
                SelectedRemovals = GetSelectedRemovalIds(),
                BypassHardwareRequirements = checkBypassHardware.Active,
                EnableLocalAccount = checkLocalAccount.Active,
                CompactInstallation = checkCompactOs.Active,
                DisableSponsoredApps = checkDisableAds.Active,
                DisableTelemetry = checkDisableTelemetry.Active,
                DisableCopilot = checkDisableCopilot.Active,
                DisableWidgets = checkDisableWidgets.Active,
                DisableBitLockerDeviceEncryption = checkDisableBitLocker.Active,
                DisableReservedStorage = checkDisableReservedStorage.Active
            };
            recipe.Write(dialog.Filename);
            ShowNotice($"Recipe saved to {dialog.Filename}", MessageType.Info);
        }
        dialog.Destroy();
    }

    private void LoadRecipe()
    {
        using var dialog = new FileChooserDialog("Load Portable Recipe", this, FileChooserAction.Open,
            "Cancel", ResponseType.Cancel, "Open", ResponseType.Accept);
        var filter = new FileFilter { Name = "JSON Recipe (*.json)" };
        filter.AddPattern("*.json");
        dialog.AddFilter(filter);

        if ((ResponseType)dialog.Run() == ResponseType.Accept)
        {
            try
            {
                var recipe = PortableRecipe.Read(dialog.Filename);
                sourcePath.Text = recipe.SourcePath;
                workspacePath.Text = recipe.WorkspacePath;
                outputPath.Text = recipe.OutputPath;
                preferredEdition = recipe.ImageIndex;

                checkBypassHardware.Active = recipe.BypassHardwareRequirements;
                checkLocalAccount.Active = recipe.EnableLocalAccount;
                checkCompactOs.Active = recipe.CompactInstallation;
                checkDisableAds.Active = recipe.DisableSponsoredApps;
                checkDisableTelemetry.Active = recipe.DisableTelemetry;
                checkDisableCopilot.Active = recipe.DisableCopilot;
                checkDisableWidgets.Active = recipe.DisableWidgets;
                checkDisableBitLocker.Active = recipe.DisableBitLockerDeviceEncryption;
                checkDisableReservedStorage.Active = recipe.DisableReservedStorage;

                foreach (var (id, check) in removalCheckboxes)
                {
                    check.Active = recipe.SelectedRemovals.Contains(id);
                }
                UpdateRemovalCount();

                steps.CurrentPage = 1;
                ShowNotice("Recipe loaded. Click 'Inspect Editions' to verify source media.", MessageType.Info);
            }
            catch (Exception ex)
            {
                ShowNotice($"Failed to load recipe: {ex.Message}", MessageType.Error);
            }
        }
        dialog.Destroy();
    }

    private void MovePage(int delta)
    {
        var next = steps.CurrentPage + delta;
        if (next < 0 || next >= steps.NPages) return;
        steps.CurrentPage = next;
    }

    private void OnPageChanged(int page)
    {
        stepRail.SelectRow(stepRail.GetRowAtIndex(page));
        currentStepLabel.Text = page switch
        {
            0 => "Step 1 of 6: Welcome & Mode Selection",
            1 => "Step 2 of 6: Windows Installation Media",
            2 => "Step 3 of 6: Component & App Removal",
            3 => "Step 4 of 6: Setup & Policy Tweaks",
            4 => "Step 5 of 6: Configuration Review",
            5 => "Step 6 of 6: Building ISO Image",
            _ => $"Step {page + 1}"
        };

        if (page == 4) UpdateReviewText();
        UpdateNavigation(page);
    }

    private void UpdateNavigation(int page)
    {
        backButton.Sensitive = !busy && page > 0;
        nextButton.Visible = page < 5;
        nextButton.Sensitive = !busy;
        startButton.Visible = page == 4;
        startButton.Sensitive = !busy;
        cancelButton.Visible = busy;
    }

    private void SetBusy(bool value, string statusMsg)
    {
        busy = value;
        steps.Sensitive = !value;
        stepRail.Sensitive = !value;
        buildStatusLabel.Text = statusMsg;
        UpdateNavigation(steps.CurrentPage);
    }

    private void FinishOperation()
    {
        SetBusy(false, "Ready.");
        if (closeWhenCancelled)
        {
            closeWhenCancelled = false;
            Application.Quit();
        }
    }

    private CancellationTokenSource NewOperation()
    {
        operation?.Dispose();
        operation = new CancellationTokenSource();
        return operation;
    }

    private void LogProgress(string message) => GLib.Idle.Add(() =>
    {
        var buffer = buildLog.Buffer;
        buffer.Text += message + Environment.NewLine;
        buildLog.ScrollToIter(buffer.EndIter, 0, false, 0, 0);
        buildProgressBar.Pulse();
        return false;
    });

    private void ShowNotice(string message, MessageType type)
    {
        noticeLabel.Text = message;
        noticeBar.MessageType = type;
        noticeBar.Show();
    }

    private void ChooseSource(FileChooserAction action)
    {
        var title = action == FileChooserAction.Open ? "Choose Windows 11 ISO" : "Choose Extracted Windows Folder";
        using var dialog = new FileChooserDialog(title, this, action, "Cancel", ResponseType.Cancel, "Select", ResponseType.Accept);
        if (action == FileChooserAction.Open)
        {
            var filter = new FileFilter { Name = "ISO Images (*.iso)" };
            filter.AddPattern("*.iso");
            dialog.AddFilter(filter);
        }
        if ((ResponseType)dialog.Run() == ResponseType.Accept)
        {
            sourcePath.Text = dialog.Filename;
            UpdateImageBadge();
        }
        dialog.Destroy();
    }

    private void ChooseDirectory(Entry target, string title)
    {
        using var dialog = new FileChooserDialog(title, this, FileChooserAction.SelectFolder,
            "Cancel", ResponseType.Cancel, "Select", ResponseType.Accept);
        if ((ResponseType)dialog.Run() == ResponseType.Accept) target.Text = dialog.Filename;
        dialog.Destroy();
    }

    private void ChooseOutputIso()
    {
        using var dialog = new FileChooserDialog("Save Output ISO", this, FileChooserAction.Save,
            "Cancel", ResponseType.Cancel, "Save", ResponseType.Accept);
        dialog.CurrentName = "Tiny11-custom.iso";
        var filter = new FileFilter { Name = "ISO Image (*.iso)" };
        filter.AddPattern("*.iso");
        dialog.AddFilter(filter);
        if ((ResponseType)dialog.Run() == ResponseType.Accept)
        {
            var chosen = dialog.Filename;
            outputPath.Text = chosen.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ? chosen : chosen + ".iso";
        }
        dialog.Destroy();
    }

    public void RefreshVisibility() => UpdateNavigation(steps.CurrentPage);

    private static Task OnUiAsync(Action action)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        GLib.Idle.Add(() =>
        {
            try { action(); tcs.SetResult(true); }
            catch (Exception ex) { tcs.SetException(ex); }
            return false;
        });
        return tcs.Task;
    }

    private static Box PageBox() => new(Orientation.Vertical, 14) { BorderWidth = 16 };
    private static Label PageHeading(string text) => new(text) { Xalign = 0, StyleContext = { "page-heading" } };
    private static Label PageBody(string text) => new(text) { Xalign = 0, LineWrap = true, StyleContext = { "page-body" } };
    private void AddPage(string title, Widget page) => steps.AppendPage(page, new Label(title));

    private void ApplyTheme()
    {
        const string css = """
            window {
                background-color: #1e1e24;
                color: #e0e0e0;
                font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
            }
            .sidebar {
                background-color: #16161a;
                border-radius: 8px;
                padding: 12px;
            }
            .brand-title {
                font-size: 20px;
                font-weight: bold;
                color: #ffffff;
            }
            .brand-sub {
                font-size: 11px;
                color: #72757e;
                margin-bottom: 8px;
            }
            .step-rail {
                background-color: transparent;
                border: none;
            }
            .step-rail row {
                border-radius: 6px;
                padding: 4px;
                color: #94a1b2;
            }
            .step-rail row:selected {
                background-color: #7f5af0;
                color: #ffffff;
                font-weight: bold;
            }
            .topbar {
                background-color: #16161a;
                border-radius: 6px;
                padding: 8px 12px;
            }
            .step-indicator {
                font-size: 14px;
                font-weight: bold;
                color: #fffffe;
            }
            .card {
                background-color: #242629;
                border-radius: 8px;
                border: 1px solid #32353b;
                padding: 12px;
            }
            .info-card {
                background-color: #182230;
                border-radius: 8px;
                border: 1px solid #233e61;
                padding: 10px;
            }
            .card-title {
                font-size: 14px;
                font-weight: bold;
                color: #fffffe;
            }
            .badge-title {
                font-size: 13px;
                font-weight: bold;
                color: #2cb67d;
            }
            .badge-detail {
                font-size: 11px;
                color: #94a1b2;
            }
            .badge-sub {
                font-size: 10px;
                color: #72757e;
            }
            .page-heading {
                font-size: 22px;
                font-weight: 300;
                color: #fffffe;
            }
            .page-body {
                font-size: 13px;
                color: #94a1b2;
                margin-bottom: 8px;
            }
            .category-header {
                font-size: 13px;
                font-weight: bold;
                color: #7f5af0;
                margin-top: 6px;
            }
            .count-label {
                font-size: 12px;
                color: #2cb67d;
                font-weight: bold;
            }
            .tool-status {
                font-size: 11px;
                color: #72757e;
            }
            .success-title {
                font-size: 16px;
                font-weight: bold;
                color: #2cb67d;
            }
            .build-log, .review-log {
                font-family: monospace;
                font-size: 12px;
                background-color: #111114;
                color: #d1d5db;
                border-radius: 6px;
                padding: 8px;
            }
            button {
                border-radius: 6px;
                padding: 6px 14px;
                font-weight: 500;
            }
            entry {
                border-radius: 6px;
                padding: 6px;
            }
            progressbar trough {
                border-radius: 4px;
                min-height: 8px;
                background-color: #32353b;
            }
            progressbar progress {
                border-radius: 4px;
                background-color: #7f5af0;
            }
        """;

        try
        {
            var provider = new CssProvider();
            provider.LoadFromData(css);
            StyleContext.AddProviderForScreen(Gdk.Screen.Default, provider, StyleProviderPriority.Application);
        }
        catch
        {
            // Ignore styling errors on minimalist GTK themes
        }
    }
}
