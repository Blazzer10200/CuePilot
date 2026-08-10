using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace WorkflowLooper;

internal sealed class MainForm : Form
{
    private const int RecordHotkeyId = 7101;
    private const int PlaybackHotkeyId = 7102;
    private const int EmergencyHotkeyId = 7103;
    private const int CueCaptureHotkeyId = 7104;
    private const int WmNcHitTest = 0x0084;
    private const int HtClient = 1;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private readonly GlobalRecorder recorder = new();
    private readonly PlaybackEngine playback = new();
    private readonly AdaptiveRoutineEngine routine = new();
    private readonly RhythmCalibrator calibrator = new();
    private readonly AppSettings loadedSettings = SettingsStore.Load();
    private AppSettings appSettings;
    private AppSettings pendingSettings;
    private readonly bool allowGlobalHotkeys;
    private CancellationTokenSource? playbackCancellation;
    private TaskCompletionSource<bool>? cueCaptureCompletion;
    private WorkflowPattern? currentPattern;
    private string? currentPath;
    private readonly Stack<string> undoHistory = new();
    private readonly Stack<string> redoHistory = new();

    private readonly TableLayoutPanel root = new();
    private readonly Panel titleBar = new();
    private readonly Panel sidebar = new();
    private readonly Panel pageHost = new();
    private readonly PatternListControl patternList = new();
    private readonly Label statusLabel = new();
    private readonly Label statusDetail = new();
    private readonly Label footerLabel = new();
    private readonly Label libraryCount = new();
    private readonly ToolTip toolTip = new() { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 5_000 };

    private readonly Dictionary<AppPage, Panel> pages = [];
    private readonly Dictionary<AppPage, ThemedButton> navButtons = [];
    private AppPage activePage = AppPage.Studio;
    private Panel? outgoingPage;
    private Panel? incomingPage;
    private int pageAnimationFrame;
    private int pageAnimationDirection;
    private readonly System.Windows.Forms.Timer pageAnimationTimer = new() { Interval = 15 };

    private readonly TextBox nameBox = new();
    private readonly Label patternSummary = new();
    private readonly Label patternPathLabel = new();
    private readonly StepperControl loopStepper = new() { Minimum = 0, Maximum = 999, Value = 1 };
    private readonly StepperControl speedStepper = new() { Minimum = 25, Maximum = 400, Step = 5, Value = 100, Suffix = "%" };
    private readonly ToggleSwitch trackCursorToggle = new();
    private readonly Label patternTargetLabel = new();
    private readonly ThemedButton recordButton = new();
    private readonly ThemedButton playButton = new();

    private readonly DataGridView eventGrid = new();
    private readonly StepperControl normalizeInterval = NumberInput(20, 5_000, 150, 5);
    private readonly StepperControl normalizeHold = NumberInput(1, 1_000, 25, 1);
    private readonly Label analysisLabel = new();
    private bool refreshingEditor;

    private readonly Label routineStateLabel = new();
    private readonly Label routineDetailLabel = new();
    private readonly Label routineTargetLabel = new();
    private readonly Label cueStatusLabel = new();
    private readonly StepperControl tapIntervalInput = NumberInput(20, 5_000, 150, 5);
    private readonly StepperControl holdInput = NumberInput(1, 1_000, 25, 1);
    private readonly StepperControl triggerHoldInput = NumberInput(0, 2_000, 80, 10);
    private readonly StepperControl maximumDurationInput = NumberInput(5, 3_600, 210, 5);
    private readonly StepperControl collectDelayInput = NumberInput(0, 10_000, 250, 25);
    private readonly StepperControl cooldownInput = NumberInput(0, 300, 5, 1);
    private readonly StepperControl similarityInput = NumberInput(20, 95, 86, 1);
    private readonly ToggleSwitch physicalFinishToggle = new() { Checked = true };
    private readonly ToggleSwitch collectTimeoutToggle = new() { Checked = true };
    private readonly ToggleSwitch visualCueToggle = new();
    private readonly ThemedButton armRoutineButton = new();

    private HotkeyAction? capturedHotkeyAction;
    private readonly Dictionary<HotkeyAction, Label> hotkeyLabels = [];
    private readonly Label hotkeyHintLabel = new();

    internal MainForm(bool registerGlobalHotkeys = true)
    {
        allowGlobalHotkeys = registerGlobalHotkeys;
        appSettings = loadedSettings.Copy();
        pendingSettings = appSettings.Copy();
        ConfigureWindow();
        BuildInterface();
        WireEvents();
        LoadRoutineControls();
        RefreshLibrary();
        TryLoadMostRecentPattern();
        RefreshHotkeyText();
        SetStatus("READY", "Record a workflow, edit its rhythm, or arm a triggered routine.", AppTheme.Accent);
    }

    private void ConfigureWindow()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Workflow Looper";
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(1180, 780);
        MinimumSize = new Size(1060, 720);
        BackColor = AppTheme.Canvas;
        ForeColor = AppTheme.Text;
        Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;
    }

    private void BuildInterface()
    {
        root.Dock = DockStyle.Fill;
        root.BackColor = AppTheme.Canvas;
        root.ColumnCount = 1;
        root.RowCount = 3;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);

        BuildTitleBar();
        BuildWorkspace();
        BuildFooter();
        pages[AppPage.Studio] = BuildStudioPage();
        pages[AppPage.Editor] = BuildEditorPage();
        pages[AppPage.Routine] = BuildRoutinePage();
        pages[AppPage.Settings] = BuildSettingsPage();
        foreach (var page in pages.Values)
        {
            pageHost.Controls.Add(page);
            page.Visible = false;
        }

        ShowPageInstant(AppPage.Studio);
    }

    private void BuildTitleBar()
    {
        titleBar.Dock = DockStyle.Fill;
        titleBar.BackColor = AppTheme.Surface;
        titleBar.Padding = new Padding(14, 0, 0, 0);
        root.Controls.Add(titleBar, 0, 0);

        var mark = new Panel { BackColor = AppTheme.Accent, Location = new Point(15, 14), Size = new Size(10, 10) };
        var caption = MakeLabel("WORKFLOW LOOPER  ·  3.0", AppTheme.Muted, 8.5F, true);
        caption.Location = new Point(36, 8);
        caption.Size = new Size(260, 24);
        titleBar.Controls.Add(mark);
        titleBar.Controls.Add(caption);

        var liveStatus = new TableLayoutPanel { Dock = DockStyle.Right, Width = 390, ColumnCount = 2, BackColor = AppTheme.Surface, Padding = new Padding(8, 0, 12, 0) };
        liveStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        liveStatus.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Font = new Font("Segoe UI Semibold", 8F);
        statusLabel.TextAlign = ContentAlignment.MiddleRight;
        statusDetail.Dock = DockStyle.Fill;
        statusDetail.ForeColor = AppTheme.Muted;
        statusDetail.Font = new Font("Segoe UI", 7.5F);
        statusDetail.TextAlign = ContentAlignment.MiddleLeft;
        statusDetail.AutoEllipsis = true;
        statusDetail.Padding = new Padding(8, 0, 0, 0);
        liveStatus.Controls.Add(statusLabel, 0, 0);
        liveStatus.Controls.Add(statusDetail, 1, 0);
        titleBar.Controls.Add(liveStatus);

        var minimize = MakeButton(string.Empty, ButtonTone.Icon, ButtonGlyph.Minimize);
        minimize.Dock = DockStyle.Right;
        minimize.Width = 46;
        minimize.AccessibleName = "Minimize";
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var close = MakeButton(string.Empty, ButtonTone.Icon, ButtonGlyph.Close);
        close.Dock = DockStyle.Right;
        close.Width = 46;
        close.AccessibleName = "Close";
        close.HoverColor = AppTheme.Coral;
        close.Click += (_, _) => Close();
        titleBar.Controls.Add(minimize);
        titleBar.Controls.Add(close);
        AttachWindowDrag(titleBar);
        AttachWindowDrag(caption);
    }

    private void BuildWorkspace()
    {
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = AppTheme.Canvas,
            Padding = new Padding(18, 18, 18, 10),
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 276));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(workspace, 0, 1);
        BuildSidebar();
        workspace.Controls.Add(sidebar, 0, 0);
        pageHost.Dock = DockStyle.Fill;
        pageHost.BackColor = AppTheme.Canvas;
        pageHost.Margin = new Padding(16, 0, 0, 0);
        workspace.Controls.Add(pageHost, 1, 0);
    }

    private void BuildSidebar()
    {
        sidebar.Dock = DockStyle.Fill;
        sidebar.BackColor = AppTheme.Surface;
        sidebar.Padding = new Padding(14);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = AppTheme.Surface };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 164));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        sidebar.Controls.Add(layout);

        var brand = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };
        var brandTitle = MakeLabel("WORKFLOW LOOPER", AppTheme.Text, 14F, true);
        brandTitle.Location = new Point(2, 8);
        brandTitle.Size = new Size(235, 30);
        var brandLine = MakeLabel("LOCAL AUTOMATION STUDIO", AppTheme.Accent, 7.5F, true);
        brandLine.Location = new Point(3, 43);
        brandLine.AutoSize = true;
        brand.Controls.Add(brandTitle);
        brand.Controls.Add(brandLine);
        layout.Controls.Add(brand, 0, 0);

        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = AppTheme.Surface };
        for (var index = 0; index < 4; index++) nav.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        AddNavButton(nav, AppPage.Studio, "STUDIO", ButtonGlyph.Record, 0);
        AddNavButton(nav, AppPage.Editor, "PRECISION EDITOR", ButtonGlyph.Edit, 1);
        AddNavButton(nav, AppPage.Routine, "TRIGGERED ROUTINE", ButtonGlyph.Tune, 2);
        AddNavButton(nav, AppPage.Settings, "SETTINGS", ButtonGlyph.Settings, 3);
        layout.Controls.Add(nav, 0, 1);

        var libraryHeader = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };
        var libraryLabel = MakeLabel("PATTERN LIBRARY", AppTheme.Muted, 8F, true);
        libraryLabel.Location = new Point(2, 8);
        libraryLabel.AutoSize = true;
        libraryCount.Location = new Point(132, 8);
        libraryCount.Size = new Size(50, 18);
        libraryCount.TextAlign = ContentAlignment.MiddleRight;
        libraryCount.ForeColor = AppTheme.Accent;
        libraryCount.Font = new Font("Consolas", 8F);
        var folder = MakeButton(string.Empty, ButtonTone.Icon, ButtonGlyph.Folder);
        folder.Bounds = new Rectangle(214, 1, 28, 30);
        folder.AccessibleName = "Open pattern folder";
        folder.Click += (_, _) => OpenPatternFolder();
        toolTip.SetToolTip(folder, "Open local pattern folder");
        var refresh = MakeButton(string.Empty, ButtonTone.Icon, ButtonGlyph.Refresh);
        refresh.Bounds = new Rectangle(184, 1, 28, 30);
        refresh.AccessibleName = "Refresh patterns";
        refresh.Click += (_, _) => RefreshLibrary(currentPath);
        toolTip.SetToolTip(refresh, "Refresh pattern library");
        libraryHeader.Controls.Add(libraryLabel);
        libraryHeader.Controls.Add(libraryCount);
        libraryHeader.Controls.Add(refresh);
        libraryHeader.Controls.Add(folder);
        layout.Controls.Add(libraryHeader, 0, 2);

        patternList.Dock = DockStyle.Fill;
        patternList.Margin = new Padding(0, 4, 0, 8);
        layout.Controls.Add(patternList, 0, 3);
        var newButton = MakeButton("RECORD NEW PATTERN", ButtonTone.Accent, ButtonGlyph.Add);
        newButton.Dock = DockStyle.Fill;
        newButton.Margin = new Padding(0, 5, 0, 0);
        newButton.Click += (_, _) => NavigateToPage(AppPage.Studio);
        layout.Controls.Add(newButton, 0, 4);
    }

    private Panel BuildStudioPage()
    {
        var page = CreatePage("STUDIO", "Record. Refine. Replay.", "Capture input, lock it to the right app, and test one controlled loop.");
        var body = PageBody(page);
        body.RowCount = 5;
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var identity = Card();
        identity.Padding = new Padding(18, 12, 18, 12);
        var nameLabel = MakeLabel("ACTIVE PATTERN", AppTheme.Muted, 8F, true);
        nameLabel.Dock = DockStyle.Top;
        nameBox.Dock = DockStyle.Top;
        nameBox.Height = 32;
        StyleTextBox(nameBox);
        nameBox.Text = "New workflow";
        patternSummary.Dock = DockStyle.Bottom;
        patternSummary.Height = 19;
        patternSummary.ForeColor = AppTheme.Muted;
        patternSummary.Font = new Font("Segoe UI", 8.5F);
        identity.Controls.Add(patternSummary);
        identity.Controls.Add(nameBox);
        identity.Controls.Add(nameLabel);
        body.Controls.Add(identity, 0, 0);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = AppTheme.Canvas, Padding = new Padding(0, 8, 0, 8) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        ConfigureButton(recordButton, "RECORD", ButtonTone.Record, ButtonGlyph.Record, 11F);
        ConfigureButton(playButton, "PLAY", ButtonTone.Accent, ButtonGlyph.Play, 11F);
        recordButton.Margin = new Padding(0, 0, 6, 0);
        playButton.Margin = new Padding(6, 0, 0, 0);
        recordButton.Dock = DockStyle.Fill;
        playButton.Dock = DockStyle.Fill;
        actions.Controls.Add(recordButton, 0, 0);
        actions.Controls.Add(playButton, 1, 0);
        body.Controls.Add(actions, 0, 1);

        var execution = Card();
        execution.Padding = new Padding(18, 14, 18, 12);
        var executionGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = AppTheme.Surface };
        for (var index = 0; index < 3; index++) executionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        executionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        executionGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        executionGrid.Controls.Add(TableCaption("LOOP COUNT"), 0, 0);
        executionGrid.Controls.Add(TableCaption("PLAYBACK SPEED"), 1, 0);
        executionGrid.Controls.Add(TableCaption("CURSOR MOVEMENT"), 2, 0);
        loopStepper.Dock = DockStyle.Top;
        loopStepper.Height = 42;
        loopStepper.Margin = new Padding(0, 6, 12, 0);
        speedStepper.Dock = DockStyle.Top;
        speedStepper.Height = 42;
        speedStepper.Margin = new Padding(0, 6, 12, 0);
        var cursorPanel = ToggleRow(trackCursorToggle, "Track recorded cursor", "Off keeps clicks and keys without mouse movement.");
        executionGrid.Controls.Add(loopStepper, 0, 1);
        executionGrid.Controls.Add(speedStepper, 1, 1);
        executionGrid.Controls.Add(cursorPanel, 2, 1);
        execution.Controls.Add(executionGrid);
        body.Controls.Add(execution, 0, 2);

        var targetCard = Card();
        targetCard.Padding = new Padding(18, 13, 18, 12);
        var targetLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = AppTheme.Surface };
        targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        targetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        targetLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        targetLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        targetLayout.SetColumnSpan(MakeLabel("TARGET WINDOW SAFETY", AppTheme.Muted, 8F, true), 3);
        targetLayout.Controls.Add(MakeLabel("TARGET WINDOW SAFETY", AppTheme.Muted, 8F, true), 0, 0);
        patternTargetLabel.Dock = DockStyle.Fill;
        patternTargetLabel.ForeColor = AppTheme.Text;
        patternTargetLabel.TextAlign = ContentAlignment.MiddleLeft;
        var captureTarget = MakeButton("CAPTURE TARGET", ButtonTone.Secondary, ButtonGlyph.Target);
        captureTarget.Dock = DockStyle.Fill;
        captureTarget.Margin = new Padding(8, 2, 8, 2);
        captureTarget.Click += async (_, _) => await CapturePatternTargetAsync();
        var clearTarget = MakeButton("CLEAR", ButtonTone.Secondary, ButtonGlyph.Close);
        clearTarget.Dock = DockStyle.Fill;
        clearTarget.Margin = new Padding(0, 2, 0, 2);
        clearTarget.Click += (_, _) => ClearPatternTarget();
        targetLayout.Controls.Add(patternTargetLabel, 0, 1);
        targetLayout.Controls.Add(captureTarget, 1, 1);
        targetLayout.Controls.Add(clearTarget, 2, 1);
        targetCard.Controls.Add(targetLayout);
        body.Controls.Add(targetCard, 0, 3);

        var patternActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, BackColor = AppTheme.Canvas, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
        patternActions.Controls.Add(ActionButton("SAVE", ButtonGlyph.Save, (_, _) => SaveCurrentPattern()));
        patternActions.Controls.Add(ActionButton("SAVE AS", ButtonGlyph.Duplicate, (_, _) => SaveCurrentPatternAs()));
        patternActions.Controls.Add(ActionButton("OPEN FILE", ButtonGlyph.Folder, (_, _) => OpenPatternFile()));
        patternActions.Controls.Add(ActionButton("CLEAR", ButtonGlyph.Close, (_, _) => ClearCurrentPattern()));
        var delete = ActionButton("DELETE", ButtonGlyph.Delete, (_, _) => DeleteCurrentPattern());
        delete.LineColor = AppTheme.Coral;
        delete.LabelColor = AppTheme.Coral;
        patternActions.Controls.Add(delete);
        body.Controls.Add(patternActions, 0, 4);
        return page;
    }

    private Panel BuildEditorPage()
    {
        var page = CreatePage("PRECISION EDITOR", "Shape the rhythm. Keep the precision.", "Edit delays, disable noise, normalize clicks, and undo safely.");
        var body = PageBody(page);
        body.RowCount = 4;
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        analysisLabel.Dock = DockStyle.Fill;
        analysisLabel.BackColor = AppTheme.Raised;
        analysisLabel.ForeColor = AppTheme.Accent;
        analysisLabel.Padding = new Padding(16, 0, 16, 0);
        analysisLabel.TextAlign = ContentAlignment.MiddleLeft;
        analysisLabel.Font = new Font("Consolas", 9F);
        body.Controls.Add(analysisLabel, 0, 0);
        ConfigureEventGrid();
        body.Controls.Add(eventGrid, 0, 1);

        var normalizeCard = Card();
        normalizeCard.Padding = new Padding(16, 10, 16, 10);
        var normalizer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, BackColor = AppTheme.Surface };
        normalizer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        normalizer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        normalizer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        normalizer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        normalizer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        normalizer.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        normalizer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        normalizer.Controls.Add(MakeLabel("CLICK RHYTHM", AppTheme.Muted, 8F, true), 0, 0);
        normalizer.Controls.Add(MakeLabel("INTERVAL (MS)", AppTheme.Muted, 8F, true), 1, 0);
        normalizer.Controls.Add(MakeLabel("HOLD (MS)", AppTheme.Muted, 8F, true), 2, 0);
        var explanation = MakeLabel("All complete left-click pairs", AppTheme.Muted, 8.2F);
        explanation.Dock = DockStyle.Fill;
        normalizer.Controls.Add(explanation, 0, 1);
        normalizeInterval.Dock = DockStyle.Fill;
        normalizeInterval.Margin = new Padding(4);
        normalizeHold.Dock = DockStyle.Fill;
        normalizeHold.Margin = new Padding(4);
        normalizer.Controls.Add(normalizeInterval, 1, 1);
        normalizer.Controls.Add(normalizeHold, 2, 1);
        var normalize = MakeButton("NORMALIZE", ButtonTone.Accent, ButtonGlyph.Tune);
        normalize.Dock = DockStyle.Fill;
        normalize.Margin = new Padding(8, 4, 8, 4);
        normalize.Click += (_, _) => NormalizeClicks();
        var learn = MakeButton("USE ANALYSIS", ButtonTone.Secondary, ButtonGlyph.Edit);
        learn.Dock = DockStyle.Fill;
        learn.Margin = new Padding(0, 4, 0, 4);
        learn.Click += (_, _) => UsePatternAnalysis();
        normalizer.Controls.Add(normalize, 3, 1);
        normalizer.Controls.Add(learn, 4, 1);
        normalizeCard.Controls.Add(normalizer);
        body.Controls.Add(normalizeCard, 0, 2);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = AppTheme.Canvas, Padding = new Padding(0, 8, 0, 0), WrapContents = false };
        toolbar.Controls.Add(ActionButton("UNDO", ButtonGlyph.Refresh, (_, _) => UndoEditor()));
        toolbar.Controls.Add(ActionButton("REDO", ButtonGlyph.Refresh, (_, _) => RedoEditor()));
        toolbar.Controls.Add(ActionButton("DUPLICATE", ButtonGlyph.Duplicate, (_, _) => DuplicateSelectedEvent()));
        toolbar.Controls.Add(ActionButton("DELETE", ButtonGlyph.Delete, (_, _) => DeleteSelectedEvent()));
        toolbar.Controls.Add(ActionButton("SAVE", ButtonGlyph.Save, (_, _) => SaveCurrentPattern()));
        body.Controls.Add(toolbar, 0, 3);
        return page;
    }

    private Panel BuildRoutinePage()
    {
        var page = CreatePage("TRIGGERED ROUTINE", "Adaptive timing, without the blind timer.", "Hold left-click to hand over control; a visual cue or physical click ends the cycle.");
        var body = PageBody(page);
        body.RowCount = 5;
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var stateCard = Card();
        stateCard.Padding = new Padding(18, 10, 18, 10);
        routineStateLabel.Text = "STOPPED";
        routineStateLabel.ForeColor = AppTheme.Accent;
        routineStateLabel.Font = new Font("Segoe UI Semibold", 15F);
        routineStateLabel.Dock = DockStyle.Top;
        routineStateLabel.Height = 31;
        routineDetailLabel.ForeColor = AppTheme.Muted;
        routineDetailLabel.Font = new Font("Segoe UI", 8.8F);
        routineDetailLabel.Dock = DockStyle.Fill;
        routineDetailLabel.Text = "Configure a target and rhythm, then arm the routine.";
        stateCard.Controls.Add(routineDetailLabel);
        stateCard.Controls.Add(routineStateLabel);
        body.Controls.Add(stateCard, 0, 0);

        var timingCard = Card();
        timingCard.Padding = new Padding(16, 12, 16, 12);
        var timing = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, BackColor = AppTheme.Surface };
        for (var index = 0; index < 3; index++) timing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        timing.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        timing.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        timing.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        timing.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        AddNumberField(timing, "TAP INTERVAL (MS)", tapIntervalInput, 0, 0);
        AddNumberField(timing, "BUTTON HOLD (MS)", holdInput, 1, 0);
        AddNumberField(timing, "TRIGGER HOLD (MS)", triggerHoldInput, 2, 0);
        AddNumberField(timing, "MAXIMUM DURATION (SEC)", maximumDurationInput, 0, 2);
        AddNumberField(timing, "COLLECT DELAY (MS)", collectDelayInput, 1, 2);
        AddNumberField(timing, "COOLDOWN (SEC)", cooldownInput, 2, 2);
        timingCard.Controls.Add(timing);
        body.Controls.Add(timingCard, 0, 1);

        var behaviorCard = Card();
        behaviorCard.Padding = new Padding(16, 10, 16, 10);
        var behavior = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = AppTheme.Surface };
        for (var index = 0; index < 3; index++) behavior.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        behavior.Controls.Add(ToggleRow(physicalFinishToggle, "Physical click finishes", "Click once to collect early."), 0, 0);
        behavior.Controls.Add(ToggleRow(collectTimeoutToggle, "Collect at timeout", "Press E when maximum duration ends."), 1, 0);
        behavior.Controls.Add(ToggleRow(visualCueToggle, "Visual end detection", "Local grayscale fingerprint only."), 2, 0);
        behaviorCard.Controls.Add(behavior);
        body.Controls.Add(behaviorCard, 0, 2);

        var detectorCard = Card();
        detectorCard.Padding = new Padding(16, 10, 16, 10);
        var detector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, BackColor = AppTheme.Surface };
        detector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        detector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
        detector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
        detector.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        detector.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detector.Controls.Add(MakeLabel("TARGET + ADAPTIVE CUE", AppTheme.Muted, 8F, true), 0, 0);
        detector.Controls.Add(MakeLabel("END CHANGE %", AppTheme.Muted, 8F, true), 1, 0);
        routineTargetLabel.Dock = DockStyle.Fill;
        routineTargetLabel.ForeColor = AppTheme.Text;
        cueStatusLabel.Dock = DockStyle.Bottom;
        cueStatusLabel.Height = 18;
        cueStatusLabel.ForeColor = AppTheme.Muted;
        var targetStack = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };
        targetStack.Controls.Add(cueStatusLabel);
        targetStack.Controls.Add(routineTargetLabel);
        similarityInput.Dock = DockStyle.Fill;
        similarityInput.Margin = new Padding(4);
        var captureTarget = MakeButton("SET TARGET", ButtonTone.Secondary, ButtonGlyph.Target);
        captureTarget.Dock = DockStyle.Fill;
        captureTarget.Margin = new Padding(8, 4, 8, 4);
        captureTarget.Click += async (_, _) => await CaptureRoutineTargetAsync();
        var captureCue = MakeButton("CAPTURE CUE", ButtonTone.Secondary, ButtonGlyph.Edit);
        captureCue.Dock = DockStyle.Fill;
        captureCue.Margin = new Padding(0, 4, 0, 4);
        captureCue.Click += async (_, _) => await CaptureVisualCueAsync();
        detector.Controls.Add(targetStack, 0, 1);
        detector.Controls.Add(similarityInput, 1, 1);
        detector.Controls.Add(captureTarget, 2, 1);
        detector.Controls.Add(captureCue, 3, 1);
        detectorCard.Controls.Add(detector);
        body.Controls.Add(detectorCard, 0, 3);

        var routineActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, BackColor = AppTheme.Canvas, Padding = new Padding(0, 10, 0, 0), WrapContents = false };
        ConfigureButton(armRoutineButton, "ARM", ButtonTone.Accent, ButtonGlyph.Arm);
        armRoutineButton.Size = new Size(160, 42);
        armRoutineButton.Margin = new Padding(0, 0, 10, 0);
        armRoutineButton.Click += (_, _) => ToggleRoutine();
        routineActions.Controls.Add(armRoutineButton);
        var finish = ActionButton("FINISH + E", ButtonGlyph.Finish, (_, _) => routine.FinishCurrentCycle());
        finish.Width = 170;
        routineActions.Controls.Add(finish);
        var calibrate = ActionButton("LEARN RHYTHM", ButtonGlyph.Tune, async (_, _) => await CalibrateRhythmAsync());
        calibrate.Width = 170;
        routineActions.Controls.Add(calibrate);
        var stop = ActionButton("STOP", ButtonGlyph.Stop, (_, _) => EmergencyStop("Stopped from the routine page."));
        stop.Width = 170;
        stop.LineColor = AppTheme.Coral;
        stop.LabelColor = AppTheme.Coral;
        routineActions.Controls.Add(stop);
        body.Controls.Add(routineActions, 0, 4);
        return page;
    }

    private Panel BuildSettingsPage()
    {
        var page = CreatePage("SETTINGS", "Controls that stay out of your way.", "Choose a shortcut, press a combination, and save. Conflicts are blocked.");
        var body = PageBody(page);
        body.RowCount = 5;
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddHotkeyCard(body, HotkeyAction.Record, "RECORD / STOP", "Starts recording or finishes the active capture.", 0);
        AddHotkeyCard(body, HotkeyAction.Playback, "PLAY / STOP", "Starts or cancels playback of the selected pattern.", 1);
        AddHotkeyCard(body, HotkeyAction.EmergencyStop, "EMERGENCY STOP", "Releases held input and stops playback or a triggered routine.", 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = AppTheme.Canvas, Padding = new Padding(0, 12, 0, 8), WrapContents = false };
        var save = MakeButton("SAVE SHORTCUTS", ButtonTone.Accent, ButtonGlyph.Save);
        save.Size = new Size(190, 44);
        save.Click += (_, _) => SaveHotkeys();
        var restore = MakeButton("RESTORE DEFAULTS", ButtonTone.Secondary, ButtonGlyph.Refresh);
        restore.Size = new Size(190, 44);
        restore.Margin = new Padding(10, 0, 0, 0);
        restore.Click += (_, _) => RestoreDefaultHotkeys();
        actions.Controls.Add(save);
        actions.Controls.Add(restore);
        body.Controls.Add(actions, 0, 3);

        hotkeyHintLabel.Dock = DockStyle.Top;
        hotkeyHintLabel.Height = 44;
        hotkeyHintLabel.ForeColor = AppTheme.Muted;
        hotkeyHintLabel.Font = new Font("Segoe UI", 9F);
        hotkeyHintLabel.Text = "Tip: use Ctrl, Shift, or Alt for record and playback shortcuts. Pause / Break remains the safest emergency stop.";
        var privacy = Card();
        privacy.Padding = new Padding(18);
        var privacyTitle = MakeLabel("LOCAL BY DESIGN", AppTheme.Accent, 8F, true);
        privacyTitle.Dock = DockStyle.Top;
        privacyTitle.Height = 24;
        var privacyBody = MakeLabel("Patterns, settings, calibration, and optional visual fingerprints stay on this PC. Visual cues store 240 grayscale samples—not screenshots. No account, telemetry, or network runtime.", AppTheme.Text, 9F);
        privacyBody.AutoSize = false;
        privacyBody.Dock = DockStyle.Top;
        privacyBody.Height = 54;
        privacy.Controls.Add(privacyBody);
        privacy.Controls.Add(privacyTitle);
        var lower = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Canvas, Padding = new Padding(0, 14, 0, 0) };
        lower.Controls.Add(privacy);
        lower.Controls.Add(hotkeyHintLabel);
        body.Controls.Add(lower, 0, 4);
        return page;
    }

    private void BuildFooter()
    {
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = AppTheme.Surface, Padding = new Padding(18, 0, 18, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        footerLabel.Dock = DockStyle.Fill;
        footerLabel.ForeColor = AppTheme.Muted;
        footerLabel.Font = new Font("Consolas", 8F);
        footerLabel.TextAlign = ContentAlignment.MiddleLeft;
        var privacy = MakeLabel("LOCAL  ·  PRECISE  ·  USER-CONTROLLED", AppTheme.Accent, 8F, true);
        privacy.Dock = DockStyle.Fill;
        privacy.TextAlign = ContentAlignment.MiddleRight;
        footer.Controls.Add(footerLabel, 0, 0);
        footer.Controls.Add(privacy, 1, 0);
        root.Controls.Add(footer, 0, 2);
    }

    private void WireEvents()
    {
        pageAnimationTimer.Tick += (_, _) => AdvancePageAnimation();
        pageHost.Resize += (_, _) => ResizePages();
        patternList.SelectionChanged += (_, _) => SelectLibraryPattern();
        recordButton.Click += async (_, _) => await ToggleRecordingAsync();
        playButton.Click += async (_, _) => await TogglePlaybackAsync(false);
        loopStepper.ValueChanged += (_, _) => ApplyPatternExecutionSettings();
        speedStepper.ValueChanged += (_, _) => ApplyPatternExecutionSettings();
        trackCursorToggle.CheckedChanged += (_, _) => ApplyPatternExecutionSettings();
        routine.StatusChanged += (_, status) => SafeUi(() => UpdateRoutineStatus(status));
        FormClosed += (_, _) => DisposeRuntime();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (allowGlobalHotkeys)
        {
            RegisterHotkeys(appSettings, true);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyDarkNativeTheme(this);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (allowGlobalHotkeys)
        {
            UnregisterHotkeys();
        }

        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey)
        {
            switch (message.WParam.ToInt32())
            {
                case RecordHotkeyId:
                    _ = ToggleRecordingAsync(appSettings.Record);
                    break;
                case PlaybackHotkeyId:
                    _ = TogglePlaybackAsync(true);
                    break;
                case EmergencyHotkeyId:
                    EmergencyStop("Emergency shortcut received.");
                    break;
                case CueCaptureHotkeyId:
                    CompleteVisualCueCapture();
                    break;
            }
        }
        else if (message.Msg == WmNcHitTest)
        {
            base.WndProc(ref message);
            if ((int)message.Result == HtClient && WindowState == FormWindowState.Normal)
            {
                var point = PointToClient(new Point(message.LParam.ToInt32()));
                const int grip = 7;
                var left = point.X < grip;
                var right = point.X >= ClientSize.Width - grip;
                var top = point.Y < grip;
                var bottom = point.Y >= ClientSize.Height - grip;
                message.Result = (IntPtr)(top && left ? HtTopLeft : top && right ? HtTopRight : bottom && left ? HtBottomLeft : bottom && right ? HtBottomRight : left ? HtLeft : right ? HtRight : top ? HtTop : bottom ? HtBottom : HtClient);
            }

            return;
        }

        base.WndProc(ref message);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (capturedHotkeyAction is null)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            capturedHotkeyAction = null;
            RefreshHotkeyText();
            hotkeyHintLabel.Text = "Shortcut capture cancelled.";
            return true;
        }

        var key = keyData & Keys.KeyCode;
        if (!SettingsStore.IsValid(new HotkeyBinding { Key = key }))
        {
            hotkeyHintLabel.Text = "Press a non-modifier key with optional Ctrl, Shift, or Alt.";
            return true;
        }

        var binding = new HotkeyBinding
        {
            Key = key,
            Control = (keyData & Keys.Control) != 0,
            Shift = (keyData & Keys.Shift) != 0,
            Alt = (keyData & Keys.Alt) != 0,
        };
        SetPendingBinding(capturedHotkeyAction.Value, binding);
        capturedHotkeyAction = null;
        RefreshHotkeyText();
        hotkeyHintLabel.Text = "Shortcut staged. Select Save shortcuts to apply it.";
        return true;
    }

    private async Task ToggleRecordingAsync(HotkeyBinding? stopHotkey = null)
    {
        if (recorder.IsRecording)
        {
            try
            {
                var pattern = recorder.Stop(nameBox.Text, stopHotkey);
                if (pattern.Events.Count == 0)
                {
                    throw new InvalidOperationException("No keyboard or mouse input was captured.");
                }

                pattern.LoopCount = loopStepper.Value;
                pattern.PlaybackSpeedPercent = speedStepper.Value;
                pattern.TrackCursor = trackCursorToggle.Checked;
                currentPattern = pattern;
                currentPath = WorkflowStore.SaveNew(pattern);
                undoHistory.Clear();
                redoHistory.Clear();
                RefreshLibrary(currentPath);
                RefreshPatternView();
                SetStatus("RECORDED", $"{pattern.Events.Count:N0} events saved with microsecond offsets.", AppTheme.Accent);
            }
            catch (Exception exception)
            {
                ShowError("Recording could not be saved", exception);
            }
            finally
            {
                recordButton.Text = "RECORD";
                recordButton.Glyph = ButtonGlyph.Record;
                SetBusy(false);
            }

            return;
        }

        if (playback.IsPlaying || routine.State is not RoutineState.Stopped)
        {
            SetStatus("BUSY", "Stop playback or the triggered routine before recording.", AppTheme.Warning);
            return;
        }

        try
        {
            recorder.Start(trackCursorToggle.Checked);
            recordButton.Text = "STOP RECORDING";
            recordButton.Glyph = ButtonGlyph.Stop;
            SetBusy(true, recordButton);
            SetStatus("RECORDING", "Physical keyboard and mouse events are being captured. Mouse movement follows the toggle.", AppTheme.Coral);
        }
        catch (Exception exception)
        {
            ShowError("Recording could not start", exception);
        }

        await Task.CompletedTask;
    }

    private async Task TogglePlaybackAsync(bool startedFromHotkey = false)
    {
        if (playback.IsPlaying)
        {
            playbackCancellation?.Cancel();
            SetStatus("STOPPING", "Releasing held inputs safely.", AppTheme.Warning);
            return;
        }

        if (currentPattern is null)
        {
            SetStatus("NO PATTERN", "Select or record a pattern first.", AppTheme.Warning);
            return;
        }

        if (routine.State is not RoutineState.Stopped)
        {
            SetStatus("ROUTINE ACTIVE", "Stop the triggered routine before normal playback.", AppTheme.Warning);
            return;
        }

        ApplyGridChanges();
        ApplyPatternExecutionSettings();
        playbackCancellation = new CancellationTokenSource();
        playButton.Text = "STOP PLAYBACK";
        playButton.Glyph = ButtonGlyph.Stop;
        SetBusy(true, playButton);
        SetStatus("PLAYING", "Target and timing preflight passed.", AppTheme.Accent);
        try
        {
            if (!startedFromHotkey)
            {
                SetStatus("STARTING", "Returning focus to the previous window before playback.", AppTheme.Warning);
                WindowState = FormWindowState.Minimized;
                await Task.Delay(1_200, playbackCancellation.Token);
            }

            await playback.PlayAsync(currentPattern, currentPattern.LoopCount, currentPattern.PlaybackSpeedPercent / 100m, currentPattern.TrackCursor, playbackCancellation.Token);
            SetStatus("COMPLETE", $"Mean drift {playback.LastMeanLatenessMilliseconds:F2} ms · max {playback.LastMaximumLatenessMilliseconds:F2} ms.", AppTheme.Accent);
        }
        catch (OperationCanceledException)
        {
            SetStatus("STOPPED", "Playback cancelled and held inputs released.", AppTheme.Warning);
        }
        catch (Exception exception)
        {
            if (!startedFromHotkey)
            {
                WindowState = FormWindowState.Normal;
                Activate();
            }

            ShowError("Playback stopped", exception);
        }
        finally
        {
            playbackCancellation.Dispose();
            playbackCancellation = null;
            playButton.Text = "PLAY";
            playButton.Glyph = ButtonGlyph.Play;
            SetBusy(false);
        }
    }

    private void EmergencyStop(string reason)
    {
        playbackCancellation?.Cancel();
        routine.Stop(reason);
        SetStatus("EMERGENCY STOP", reason, AppTheme.Coral);
    }

    private void ToggleRoutine()
    {
        if (routine.State != RoutineState.Stopped)
        {
            routine.Stop("Routine disarmed.");
            armRoutineButton.Text = "ARM";
            armRoutineButton.Glyph = ButtonGlyph.Arm;
            return;
        }

        if (recorder.IsRecording || playback.IsPlaying)
        {
            SetStatus("BUSY", "Stop recording or playback before arming the routine.", AppTheme.Warning);
            return;
        }

        try
        {
            SaveRoutineControls();
            routine.Arm(appSettings.Routine);
            armRoutineButton.Text = "DISARM";
            armRoutineButton.Glyph = ButtonGlyph.Stop;
            SetStatus("ROUTINE ARMED", "Hold and release physical left-click when the minigame appears.", AppTheme.Accent);
            WindowState = FormWindowState.Minimized;
        }
        catch (Exception exception)
        {
            ShowError("Routine could not be armed", exception);
        }
    }

    private async Task CalibrateRhythmAsync()
    {
        if (routine.State != RoutineState.Stopped)
        {
            SetStatus("DISARM FIRST", "Stop the routine before calibration.", AppTheme.Warning);
            return;
        }

        try
        {
            calibrator.Start();
            SetStatus("CALIBRATING", "Click naturally for 12 seconds. Only physical left-click timing is measured.", AppTheme.Accent);
            WindowState = FormWindowState.Minimized;
            await Task.Delay(TimeSpan.FromSeconds(12));
            var result = calibrator.Complete();
            tapIntervalInput.Value = Math.Clamp(result.IntervalMilliseconds, (int)tapIntervalInput.Minimum, (int)tapIntervalInput.Maximum);
            holdInput.Value = Math.Clamp(result.HoldMilliseconds, (int)holdInput.Minimum, Math.Min((int)holdInput.Maximum, result.IntervalMilliseconds - 1));
            WindowState = FormWindowState.Normal;
            Activate();
            SetStatus("RHYTHM LEARNED", $"{result.ClickCount} clicks · {result.IntervalMilliseconds} ms interval · {result.HoldMilliseconds} ms hold.", AppTheme.Accent);
        }
        catch (Exception exception)
        {
            WindowState = FormWindowState.Normal;
            ShowError("Rhythm calibration failed", exception);
        }
    }

    private async Task CaptureRoutineTargetAsync()
    {
        try
        {
            SetStatus("CAPTURE TARGET", "Returning to the previous window in 1.5 seconds.", AppTheme.Warning);
            WindowState = FormWindowState.Minimized;
            await Task.Delay(1_500);
            appSettings.Routine.TargetWindow = WindowTargetService.CaptureForeground();
            pendingSettings.Routine.TargetWindow = appSettings.Routine.TargetWindow.Copy();
            SettingsStore.Save(appSettings);
            WindowState = FormWindowState.Normal;
            Activate();
            RefreshRoutineTarget();
            SetStatus("TARGET CAPTURED", $"Routine locked to {appSettings.Routine.TargetWindow.ProcessName}.", AppTheme.Accent);
        }
        catch (Exception exception)
        {
            WindowState = FormWindowState.Normal;
            ShowError("Target capture failed", exception);
        }
    }

    private async Task CapturePatternTargetAsync()
    {
        if (currentPattern is null)
        {
            SetStatus("NO PATTERN", "Record or select a pattern first.", AppTheme.Warning);
            return;
        }

        try
        {
            SetStatus("CAPTURE TARGET", "Returning to the previous window in 1.5 seconds.", AppTheme.Warning);
            WindowState = FormWindowState.Minimized;
            await Task.Delay(1_500);
            PushUndo();
            currentPattern.TargetWindow = WindowTargetService.CaptureForeground();
            WindowState = FormWindowState.Normal;
            Activate();
            RefreshPatternView();
            SetStatus("TARGET CAPTURED", $"Pattern locked to {currentPattern.TargetWindow.ProcessName}.", AppTheme.Accent);
        }
        catch (Exception exception)
        {
            WindowState = FormWindowState.Normal;
            ShowError("Target capture failed", exception);
        }
    }

    private async Task CaptureVisualCueAsync()
    {
        SaveRoutineControls();
        if (!appSettings.Routine.TargetWindow.IsConfigured)
        {
            SetStatus("TARGET REQUIRED", "Capture the game window before capturing its visual cue.", AppTheme.Warning);
            return;
        }

        if (cueCaptureCompletion is not null)
        {
            SetStatus("CUE CAPTURE ARMED", "Press Ctrl + Shift + F8 when the minigame is visible.", AppTheme.Warning);
            return;
        }

        try
        {
            if (!NativeMethods.RegisterHotKey(Handle, CueCaptureHotkeyId, NativeMethods.ModControl | NativeMethods.ModShift | NativeMethods.ModNoRepeat, (uint)Keys.F8))
            {
                throw new InvalidOperationException("Ctrl + Shift + F8 is already owned by another application.");
            }

            cueCaptureCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetStatus("CUE CAPTURE ARMED", "Return to the game. Put the cursor on the circular meter and press Ctrl + Shift + F8.", AppTheme.Warning);
            WindowState = FormWindowState.Minimized;
            await cueCaptureCompletion.Task;
        }
        catch (Exception exception)
        {
            NativeMethods.UnregisterHotKey(Handle, CueCaptureHotkeyId);
            cueCaptureCompletion = null;
            ShowError("Visual cue capture failed", exception);
        }
    }

    private void CompleteVisualCueCapture()
    {
        if (cueCaptureCompletion is null)
        {
            return;
        }

        var completion = cueCaptureCompletion;
        try
        {
            appSettings.Routine.VisualCue = VisualCueService.CaptureAtCursor(appSettings.Routine.TargetWindow);
            appSettings.Routine.VisualCue.SimilarityPercent = (int)similarityInput.Value;
            visualCueToggle.Checked = true;
            SettingsStore.Save(appSettings);
            RefreshRoutineTarget();
            SetStatus("CUE CAPTURED", "The 20×12 grayscale fingerprint is ready. No screenshot was saved.", AppTheme.Accent);
            System.Media.SystemSounds.Asterisk.Play();
            completion.TrySetResult(true);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            NativeMethods.UnregisterHotKey(Handle, CueCaptureHotkeyId);
            cueCaptureCompletion = null;
        }
    }

    private void NormalizeClicks()
    {
        if (currentPattern is null)
        {
            return;
        }

        PushUndo();
        var count = PatternTiming.NormalizeLeftClicks(currentPattern, (int)normalizeInterval.Value, (int)normalizeHold.Value);
        if (count == 0)
        {
            undoHistory.Pop();
            SetStatus("NO CLICKS", "The pattern has no complete left-button click pairs.", AppTheme.Warning);
            return;
        }

        RefreshEditor();
        SetStatus("RHYTHM NORMALIZED", $"{count:N0} left-clicks now use an exact {(int)normalizeInterval.Value} ms interval.", AppTheme.Accent);
    }

    private void UsePatternAnalysis()
    {
        if (currentPattern is null)
        {
            return;
        }

        var analysis = PatternTiming.AnalyzeLeftClicks(currentPattern);
        if (analysis.ClickCount < 2)
        {
            SetStatus("NOT ENOUGH CLICKS", "At least two complete left-clicks are needed.", AppTheme.Warning);
            return;
        }

        normalizeInterval.Value = Math.Clamp((int)Math.Round(analysis.MedianIntervalMilliseconds), normalizeInterval.Minimum, normalizeInterval.Maximum);
        normalizeHold.Value = Math.Clamp((int)Math.Round(analysis.MedianHoldMilliseconds), normalizeHold.Minimum, normalizeHold.Maximum);
        SetStatus("ANALYSIS LOADED", "Median timing is ready for normalization.", AppTheme.Accent);
    }

    private void ConfigureEventGrid()
    {
        eventGrid.Dock = DockStyle.Fill;
        eventGrid.Margin = new Padding(0, 10, 0, 10);
        eventGrid.BackgroundColor = AppTheme.Surface;
        eventGrid.BorderStyle = BorderStyle.None;
        eventGrid.EnableHeadersVisualStyles = false;
        eventGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        eventGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = AppTheme.Raised,
            ForeColor = AppTheme.Muted,
            Font = new Font("Segoe UI Semibold", 8F),
            SelectionBackColor = AppTheme.Raised,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
        };
        eventGrid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.Text,
            SelectionBackColor = AppTheme.AccentDark,
            SelectionForeColor = AppTheme.Text,
            Font = new Font("Consolas", 8.5F),
        };
        eventGrid.GridColor = AppTheme.Border;
        eventGrid.RowHeadersVisible = false;
        eventGrid.AllowUserToAddRows = false;
        eventGrid.AllowUserToDeleteRows = false;
        eventGrid.MultiSelect = false;
        eventGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        eventGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        eventGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "ON", FillWeight = 42 });
        eventGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Index", HeaderText = "#", ReadOnly = true, FillWeight = 42 });
        eventGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "EVENT", ReadOnly = true, FillWeight = 120 });
        eventGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Delay", HeaderText = "DELAY MS", FillWeight = 88 });
        eventGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Offset", HeaderText = "AT MS", ReadOnly = true, FillWeight = 88 });
        eventGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Input", HeaderText = "INPUT", ReadOnly = true, FillWeight = 110 });
        eventGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Position", HeaderText = "POSITION", ReadOnly = true, FillWeight = 120 });
        eventGrid.CellEndEdit += (_, _) => ApplyGridChanges();
        eventGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (eventGrid.IsCurrentCellDirty)
            {
                eventGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        eventGrid.CellValueChanged += (_, e) =>
        {
            if (!refreshingEditor && e.RowIndex >= 0 && e.ColumnIndex == 0)
            {
                ApplyGridChanges();
            }
        };
    }

    private void RefreshEditor()
    {
        refreshingEditor = true;
        eventGrid.Rows.Clear();
        if (currentPattern is null)
        {
            analysisLabel.Text = "NO PATTERN SELECTED";
            refreshingEditor = false;
            return;
        }

        long previous = 0;
        for (var index = 0; index < currentPattern.Events.Count; index++)
        {
            var item = currentPattern.Events[index];
            var delay = (item.OffsetMicroseconds - previous) / 1_000d;
            previous = item.OffsetMicroseconds;
            eventGrid.Rows.Add(item.Enabled, index + 1, EventName(item), delay.ToString("F3"), (item.OffsetMicroseconds / 1_000d).ToString("F3"), InputName(item), PositionName(item));
        }

        var analysis = PatternTiming.AnalyzeLeftClicks(currentPattern);
        analysisLabel.Text = analysis.ClickCount == 0
            ? $"{currentPattern.Events.Count:N0} EVENTS  ·  NO COMPLETE LEFT-CLICK PAIRS"
            : $"{analysis.ClickCount:N0} CLICKS  ·  MEDIAN {analysis.MedianIntervalMilliseconds:F1} MS  ·  HOLD {analysis.MedianHoldMilliseconds:F1} MS  ·  RANGE {analysis.MinimumIntervalMilliseconds:F1}–{analysis.MaximumIntervalMilliseconds:F1} MS";
        refreshingEditor = false;
    }

    private void ApplyGridChanges()
    {
        if (refreshingEditor || currentPattern is null || eventGrid.Rows.Count != currentPattern.Events.Count)
        {
            return;
        }

        try
        {
            PushUndo();
            var delays = new List<double>();
            for (var index = 0; index < eventGrid.Rows.Count; index++)
            {
                var row = eventGrid.Rows[index];
                currentPattern.Events[index].Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? true);
                if (!double.TryParse(Convert.ToString(row.Cells["Delay"].Value), out var delay) || delay < 0)
                {
                    throw new InvalidDataException($"Row {index + 1} has an invalid delay.");
                }

                delays.Add(delay);
            }

            PatternTiming.ApplyDelays(currentPattern, delays);
            RefreshEditor();
        }
        catch (Exception exception)
        {
            if (undoHistory.Count > 0)
            {
                RestoreSnapshot(undoHistory.Pop());
            }

            ShowError("Editor change rejected", exception);
        }
    }

    private void DuplicateSelectedEvent()
    {
        if (currentPattern is null || eventGrid.CurrentRow is null)
        {
            return;
        }

        var index = eventGrid.CurrentRow.Index;
        PushUndo();
        var source = currentPattern.Events[index];
        var clone = JsonSerializer.Deserialize<MacroEvent>(JsonSerializer.Serialize(source, WorkflowJson.Options), WorkflowJson.Options)!;
        clone.OffsetMicroseconds += 1_000;
        currentPattern.Events.Insert(index + 1, clone);
        currentPattern.Events.Sort((left, right) => left.OffsetMicroseconds.CompareTo(right.OffsetMicroseconds));
        RefreshEditor();
    }

    private void DeleteSelectedEvent()
    {
        if (currentPattern is null || eventGrid.CurrentRow is null || currentPattern.Events.Count <= 1)
        {
            return;
        }

        PushUndo();
        currentPattern.Events.RemoveAt(eventGrid.CurrentRow.Index);
        currentPattern.DurationMicroseconds = currentPattern.Events[^1].OffsetMicroseconds;
        RefreshEditor();
    }

    private void PushUndo()
    {
        if (currentPattern is null)
        {
            return;
        }

        undoHistory.Push(JsonSerializer.Serialize(currentPattern, WorkflowJson.Options));
        while (undoHistory.Count > 50)
        {
            var retained = undoHistory.Reverse().Take(50).Reverse().ToArray();
            undoHistory.Clear();
            foreach (var item in retained) undoHistory.Push(item);
        }

        redoHistory.Clear();
    }

    private void UndoEditor()
    {
        if (currentPattern is null || undoHistory.Count == 0)
        {
            return;
        }

        redoHistory.Push(JsonSerializer.Serialize(currentPattern, WorkflowJson.Options));
        RestoreSnapshot(undoHistory.Pop());
        SetStatus("UNDONE", "The previous editor change was restored.", AppTheme.Accent);
    }

    private void RedoEditor()
    {
        if (currentPattern is null || redoHistory.Count == 0)
        {
            return;
        }

        undoHistory.Push(JsonSerializer.Serialize(currentPattern, WorkflowJson.Options));
        RestoreSnapshot(redoHistory.Pop());
        SetStatus("REDONE", "The editor change was reapplied.", AppTheme.Accent);
    }

    private void RestoreSnapshot(string json)
    {
        currentPattern = JsonSerializer.Deserialize<WorkflowPattern>(json, WorkflowJson.Options)
            ?? throw new InvalidDataException("The editor snapshot could not be restored.");
        RefreshPatternView();
    }

    private void RefreshLibrary(string? selectPath = null)
    {
        Directory.CreateDirectory(WorkflowStore.PatternDirectory);
        var entries = new List<PatternListItem>();
        foreach (var path in Directory.EnumerateFiles(WorkflowStore.PatternDirectory, "*.workflow.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                entries.Add(new PatternListItem(path, WorkflowStore.Load(path)));
            }
            catch
            {
                // Invalid files remain on disk and are skipped visibly through the reduced count.
            }
        }

        patternList.SetItems(entries, selectPath);
        libraryCount.Text = entries.Count.ToString();
        if (selectPath is not null && entries.Any(item => item.Path.Equals(selectPath, StringComparison.OrdinalIgnoreCase)))
        {
            SelectLibraryPattern();
        }
    }

    private void TryLoadMostRecentPattern()
    {
        if (patternList.SelectedItem is null)
        {
            patternList.SelectFirst();
        }

        SelectLibraryPattern();
    }

    private void SelectLibraryPattern()
    {
        var selected = patternList.SelectedItem;
        if (selected is null)
        {
            return;
        }

        currentPattern = selected.Pattern;
        currentPath = selected.Path;
        undoHistory.Clear();
        redoHistory.Clear();
        RefreshPatternView();
        SetStatus("LOADED", $"{currentPattern.Name} is ready.", AppTheme.Accent);
    }

    private void RefreshPatternView()
    {
        if (currentPattern is null)
        {
            nameBox.Text = "New workflow";
            patternSummary.Text = "No pattern selected";
            patternPathLabel.Text = string.Empty;
            patternTargetLabel.Text = "No target lock. Playback is allowed in the foreground app.";
            RefreshEditor();
            return;
        }

        nameBox.Text = currentPattern.Name;
        loopStepper.Value = currentPattern.LoopCount;
        speedStepper.Value = currentPattern.PlaybackSpeedPercent;
        trackCursorToggle.Checked = currentPattern.TrackCursor;
        var clicks = PatternTiming.AnalyzeLeftClicks(currentPattern).ClickCount;
        patternSummary.Text = $"{currentPattern.Events.Count:N0} events  ·  {clicks:N0} clicks  ·  {TimeSpan.FromMilliseconds(currentPattern.DurationMicroseconds / 1_000d):mm\\:ss\\.fff}";
        patternPathLabel.Text = currentPath ?? "Unsaved pattern";
        patternTargetLabel.Text = currentPattern.TargetWindow.IsConfigured
            ? $"Locked to {currentPattern.TargetWindow.ProcessName}  ·  stops on focus loss"
            : "No target lock. Playback is allowed in the foreground app.";
        RefreshEditor();
    }

    private void ApplyPatternExecutionSettings()
    {
        if (currentPattern is null)
        {
            return;
        }

        currentPattern.LoopCount = loopStepper.Value;
        currentPattern.PlaybackSpeedPercent = speedStepper.Value;
        currentPattern.TrackCursor = trackCursorToggle.Checked;
    }

    private void SaveCurrentPattern()
    {
        if (currentPattern is null)
        {
            return;
        }

        ApplyGridChanges();
        currentPattern.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? currentPattern.Name : nameBox.Text.Trim();
        ApplyPatternExecutionSettings();
        currentPath ??= WorkflowStore.SaveNew(currentPattern);
        WorkflowStore.Save(currentPattern, currentPath);
        RefreshLibrary(currentPath);
        SetStatus("SAVED", "Pattern updated atomically; the previous file is available as .bak.", AppTheme.Accent);
    }

    private void SaveCurrentPatternAs()
    {
        if (currentPattern is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Workflow Looper pattern (*.workflow.json)|*.workflow.json",
            InitialDirectory = WorkflowStore.PatternDirectory,
            FileName = $"{currentPattern.Name}.workflow.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        currentPattern.Name = nameBox.Text.Trim();
        WorkflowStore.Save(currentPattern, dialog.FileName);
        currentPath = dialog.FileName;
        RefreshLibrary(currentPath);
    }

    private void OpenPatternFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Workflow Looper pattern (*.workflow.json)|*.workflow.json|JSON files (*.json)|*.json",
            InitialDirectory = WorkflowStore.PatternDirectory,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            currentPattern = WorkflowStore.Load(dialog.FileName);
            currentPath = dialog.FileName;
            RefreshPatternView();
            RefreshLibrary(currentPath);
        }
        catch (Exception exception)
        {
            ShowError("Pattern could not be opened", exception);
        }
    }

    private void ClearCurrentPattern()
    {
        currentPattern = null;
        currentPath = null;
        patternList.ClearSelection();
        undoHistory.Clear();
        redoHistory.Clear();
        RefreshPatternView();
        SetStatus("CLEARED", "The saved library file was not deleted.", AppTheme.Warning);
    }

    private void DeleteCurrentPattern()
    {
        if (currentPath is null || !File.Exists(currentPath))
        {
            return;
        }

        if (MessageBox.Show(this, $"Delete '{currentPattern?.Name}' from the local library?", "Delete pattern", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        File.Delete(currentPath);
        currentPattern = null;
        currentPath = null;
        RefreshLibrary();
        RefreshPatternView();
        SetStatus("DELETED", "The selected local pattern was removed.", AppTheme.Coral);
    }

    private void OpenPatternFolder()
    {
        Directory.CreateDirectory(WorkflowStore.PatternDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", WorkflowStore.PatternDirectory) { UseShellExecute = true });
    }

    private void ClearPatternTarget()
    {
        if (currentPattern is null)
        {
            return;
        }

        PushUndo();
        currentPattern.TargetWindow = new WindowTargetSettings();
        RefreshPatternView();
    }

    private void LoadRoutineControls()
    {
        var value = appSettings.Routine;
        value.Clamp();
        tapIntervalInput.Value = value.TapIntervalMilliseconds;
        holdInput.Value = value.HoldMilliseconds;
        triggerHoldInput.Value = value.TriggerHoldMilliseconds;
        maximumDurationInput.Value = value.MaximumDurationSeconds;
        collectDelayInput.Value = value.CollectDelayMilliseconds;
        cooldownInput.Value = value.CooldownSeconds;
        similarityInput.Value = value.VisualCue.SimilarityPercent;
        physicalFinishToggle.Checked = value.PhysicalClickFinishes;
        collectTimeoutToggle.Checked = value.CollectOnTimeout;
        visualCueToggle.Checked = value.VisualCue.Enabled;
        RefreshRoutineTarget();
    }

    private void SaveRoutineControls()
    {
        var value = appSettings.Routine;
        value.TapIntervalMilliseconds = (int)tapIntervalInput.Value;
        value.HoldMilliseconds = (int)holdInput.Value;
        value.TriggerHoldMilliseconds = (int)triggerHoldInput.Value;
        value.MaximumDurationSeconds = (int)maximumDurationInput.Value;
        value.CollectDelayMilliseconds = (int)collectDelayInput.Value;
        value.CooldownSeconds = (int)cooldownInput.Value;
        value.PhysicalClickFinishes = physicalFinishToggle.Checked;
        value.CollectOnTimeout = collectTimeoutToggle.Checked;
        value.VisualCue.Enabled = visualCueToggle.Checked && !string.IsNullOrWhiteSpace(value.VisualCue.Fingerprint);
        value.VisualCue.SimilarityPercent = (int)similarityInput.Value;
        value.Clamp();
        pendingSettings.Routine = value.Copy();
        SettingsStore.Save(appSettings);
        RefreshRoutineTarget();
    }

    private void RefreshRoutineTarget()
    {
        var value = appSettings.Routine;
        routineTargetLabel.Text = value.TargetWindow.IsConfigured ? $"{value.TargetWindow.ProcessName}  ·  foreground lock" : "No target captured";
        cueStatusLabel.Text = value.VisualCue.IsConfigured ? $"Visual cue ready  ·  {value.VisualCue.SimilarityPercent}% change threshold" : "Capture arms Ctrl + Shift + F8  ·  manual finish still works";
    }

    private void UpdateRoutineStatus(RoutineStatus status)
    {
        routineStateLabel.Text = status.State.ToString().ToUpperInvariant();
        routineStateLabel.ForeColor = status.State == RoutineState.Faulted ? AppTheme.Coral : status.State == RoutineState.Tapping ? AppTheme.Warning : AppTheme.Accent;
        routineDetailLabel.Text = status.Detail;
        if (status.State == RoutineState.Stopped)
        {
            armRoutineButton.Text = "ARM";
            armRoutineButton.Glyph = ButtonGlyph.Arm;
        }

        SetStatus(status.State.ToString().ToUpperInvariant(), status.Detail, routineStateLabel.ForeColor);
    }

    private void AddHotkeyCard(TableLayoutPanel parent, HotkeyAction action, string title, string description, int row)
    {
        var card = Card();
        card.Padding = new Padding(16, 10, 16, 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = AppTheme.Surface };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var displayText = action switch
        {
            HotkeyAction.Record => pendingSettings.Record.DisplayText,
            HotkeyAction.Playback => pendingSettings.Playback.DisplayText,
            _ => pendingSettings.EmergencyStop.DisplayText,
        };
        var titleLabel = MakeLabel($"{title}  ·  {displayText}", AppTheme.Text, 9.5F, true);
        titleLabel.Dock = DockStyle.Top;
        titleLabel.Height = 23;
        var descriptionLabel = MakeLabel($"{description}  Select to change.", AppTheme.Muted, 8.5F);
        descriptionLabel.Dock = DockStyle.Fill;
        var copy = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };
        copy.Controls.Add(descriptionLabel);
        copy.Controls.Add(titleLabel);
        EventHandler selectHandler = (_, _) => BeginHotkeyCapture(action);
        foreach (var control in new Control[] { card, layout, copy, titleLabel, descriptionLabel })
        {
            control.Cursor = Cursors.Hand;
            control.Click += selectHandler;
        }
        hotkeyLabels[action] = titleLabel;
        layout.Controls.Add(copy, 0, 0);
        card.Controls.Add(layout);
        parent.Controls.Add(card, 0, row);
    }

    private void BeginHotkeyCapture(HotkeyAction action)
    {
        capturedHotkeyAction = action;
        hotkeyLabels[action].Text = $"{HotkeyTitle(action)}  ·  PRESS COMBINATION…";
        hotkeyHintLabel.Text = "Listening now. Press Esc to cancel.";
    }

    private void SaveHotkeys()
    {
        if (pendingSettings.HasDuplicates())
        {
            SetStatus("SHORTCUT CONFLICT", "Each global shortcut must be unique.", AppTheme.Coral);
            return;
        }

        if (allowGlobalHotkeys && !RegisterHotkeys(pendingSettings, false))
        {
            RegisterHotkeys(appSettings, true);
            return;
        }

        appSettings.Record = pendingSettings.Record.Copy();
        appSettings.Playback = pendingSettings.Playback.Copy();
        appSettings.EmergencyStop = pendingSettings.EmergencyStop.Copy();
        SettingsStore.Save(appSettings);
        RefreshHotkeyText();
        SetStatus("SHORTCUTS SAVED", "The new global controls are active now.", AppTheme.Accent);
    }

    private void RestoreDefaultHotkeys()
    {
        pendingSettings.Record = AppSettings.DefaultRecord();
        pendingSettings.Playback = AppSettings.DefaultPlayback();
        pendingSettings.EmergencyStop = AppSettings.DefaultEmergencyStop();
        RefreshHotkeyText();
        hotkeyHintLabel.Text = "Defaults staged. Select Save shortcuts to apply them.";
    }

    private bool RegisterHotkeys(AppSettings settings, bool quiet)
    {
        UnregisterHotkeys();
        var registrations = new[]
        {
            (RecordHotkeyId, settings.Record),
            (PlaybackHotkeyId, settings.Playback),
            (EmergencyHotkeyId, settings.EmergencyStop),
        };
        foreach (var (id, binding) in registrations)
        {
            if (!NativeMethods.RegisterHotKey(Handle, id, binding.NativeModifiers, (uint)binding.Key))
            {
                UnregisterHotkeys();
                if (!quiet)
                {
                    SetStatus("SHORTCUT UNAVAILABLE", $"{binding.DisplayText} is already owned by another application.", AppTheme.Coral);
                }

                return false;
            }
        }

        return true;
    }

    private void UnregisterHotkeys()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(Handle, RecordHotkeyId);
        NativeMethods.UnregisterHotKey(Handle, PlaybackHotkeyId);
        NativeMethods.UnregisterHotKey(Handle, EmergencyHotkeyId);
    }

    private void RefreshHotkeyText()
    {
        if (hotkeyLabels.Count > 0)
        {
            hotkeyLabels[HotkeyAction.Record].Text = $"{HotkeyTitle(HotkeyAction.Record)}  ·  {pendingSettings.Record.DisplayText}";
            hotkeyLabels[HotkeyAction.Playback].Text = $"{HotkeyTitle(HotkeyAction.Playback)}  ·  {pendingSettings.Playback.DisplayText}";
            hotkeyLabels[HotkeyAction.EmergencyStop].Text = $"{HotkeyTitle(HotkeyAction.EmergencyStop)}  ·  {pendingSettings.EmergencyStop.DisplayText}";
        }

        footerLabel.Text = $"{appSettings.Record.DisplayText}  RECORD   ·   {appSettings.Playback.DisplayText}  PLAY   ·   {appSettings.EmergencyStop.DisplayText}  STOP";
    }

    private void SetPendingBinding(HotkeyAction action, HotkeyBinding binding)
    {
        switch (action)
        {
            case HotkeyAction.Record:
                pendingSettings.Record = binding;
                break;
            case HotkeyAction.Playback:
                pendingSettings.Playback = binding;
                break;
            default:
                pendingSettings.EmergencyStop = binding;
                break;
        }
    }

    private void NavigateToPage(AppPage page)
    {
        if (page == activePage || pageAnimationTimer.Enabled)
        {
            return;
        }

        outgoingPage = pages[activePage];
        incomingPage = pages[page];
        if (page == AppPage.Settings)
        {
            RefreshHotkeyText();
        }
        pageAnimationDirection = (int)page > (int)activePage ? 1 : -1;
        activePage = page;
        UpdateNavState();
        if (!SystemInformation.IsMenuAnimationEnabled)
        {
            ShowPageInstant(page);
            return;
        }

        pageAnimationFrame = 0;
        incomingPage.Bounds = pageHost.ClientRectangle;
        incomingPage.Left = pageAnimationDirection * 56;
        incomingPage.Visible = true;
        incomingPage.BringToFront();
        pageAnimationTimer.Start();
    }

    private void AdvancePageAnimation()
    {
        if (outgoingPage is null || incomingPage is null)
        {
            pageAnimationTimer.Stop();
            return;
        }

        pageAnimationFrame++;
        const int frames = 10;
        var progress = Math.Min(1d, pageAnimationFrame / (double)frames);
        var eased = 1d - Math.Pow(1d - progress, 3d);
        outgoingPage.Left = -pageAnimationDirection * (int)Math.Round(28 * eased);
        incomingPage.Left = pageAnimationDirection * (int)Math.Round(56 * (1d - eased));
        if (pageAnimationFrame < frames)
        {
            return;
        }

        pageAnimationTimer.Stop();
        outgoingPage.Visible = false;
        outgoingPage.Bounds = pageHost.ClientRectangle;
        incomingPage.Bounds = pageHost.ClientRectangle;
        outgoingPage = null;
        incomingPage = null;
    }

    private void ShowPageInstant(AppPage page)
    {
        activePage = page;
        foreach (var pair in pages)
        {
            pair.Value.Visible = pair.Key == page;
            pair.Value.Bounds = pageHost.ClientRectangle;
        }

        pages[page].BringToFront();
        UpdateNavState();
    }

    internal void ShowGuideForPreview() => ShowPageInstant(AppPage.Routine);
    internal void ShowEditorForPreview() => ShowPageInstant(AppPage.Editor);
    internal void ShowSettingsForPreview()
    {
        ShowPageInstant(AppPage.Settings);
        RefreshHotkeyText();
        pages[AppPage.Settings].Refresh();
    }
    internal string ShortcutPreviewForTest => string.Join("|", hotkeyLabels.OrderBy(pair => pair.Key).Select(pair => pair.Value.Text));

    private static string HotkeyTitle(HotkeyAction action) => action switch
    {
        HotkeyAction.Record => "RECORD / STOP",
        HotkeyAction.Playback => "PLAY / STOP",
        _ => "EMERGENCY STOP",
    };

    private void ResizePages()
    {
        foreach (var page in pages.Values)
        {
            if (page != incomingPage && page != outgoingPage)
            {
                page.Bounds = pageHost.ClientRectangle;
            }
        }
    }

    private void UpdateNavState()
    {
        foreach (var pair in navButtons)
        {
            pair.Value.FillColor = pair.Key == activePage ? AppTheme.AccentDark : AppTheme.Surface;
            pair.Value.LineColor = pair.Key == activePage ? AppTheme.Accent : AppTheme.Surface;
            pair.Value.LabelColor = pair.Key == activePage ? AppTheme.Accent : AppTheme.Muted;
            pair.Value.DrawBorder = pair.Key == activePage;
            pair.Value.Invalidate();
        }
    }

    private void AddNavButton(TableLayoutPanel parent, AppPage page, string text, ButtonGlyph glyph, int row)
    {
        var button = MakeButton(text, ButtonTone.Icon, glyph);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 2, 0, 2);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Click += (_, _) => NavigateToPage(page);
        navButtons[page] = button;
        parent.Controls.Add(button, 0, row);
    }

    private static Panel CreatePage(string kicker, string title, string subtitle)
    {
        var page = new Panel { BackColor = AppTheme.Canvas, Padding = new Padding(0), AutoScroll = true };
        var header = new Panel { Dock = DockStyle.Top, Height = 94, BackColor = AppTheme.Canvas };
        var kickerLabel = MakeLabel(kicker, AppTheme.Accent, 8F, true);
        kickerLabel.Location = new Point(2, 0);
        kickerLabel.AutoSize = true;
        var titleLabel = MakeLabel(title, AppTheme.Text, 21F, true);
        titleLabel.Location = new Point(0, 20);
        titleLabel.AutoSize = true;
        var subtitleLabel = MakeLabel(subtitle, AppTheme.Muted, 9F);
        subtitleLabel.Location = new Point(2, 58);
        subtitleLabel.Size = new Size(820, 30);
        header.Controls.Add(kickerLabel);
        header.Controls.Add(titleLabel);
        header.Controls.Add(subtitleLabel);
        var body = new TableLayoutPanel { Name = "PageBody", Dock = DockStyle.Fill, ColumnCount = 1, BackColor = AppTheme.Canvas, Padding = new Padding(0, 0, 0, 4) };
        page.Controls.Add(body);
        page.Controls.Add(header);
        return page;
    }

    private static TableLayoutPanel PageBody(Panel page) => page.Controls.OfType<TableLayoutPanel>().Single(control => control.Name == "PageBody");

    private static Panel Card() => new() { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Margin = new Padding(0, 5, 0, 5) };

    private static Panel ToggleRow(ToggleSwitch toggle, string title, string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface };
        toggle.Location = new Point(0, 8);
        var titleLabel = MakeLabel(title, AppTheme.Text, 8.8F, true);
        titleLabel.AutoSize = false;
        titleLabel.Location = new Point(58, 2);
        titleLabel.Size = new Size(190, 20);
        var subtitleLabel = MakeLabel(subtitle, AppTheme.Muted, 7.8F);
        subtitleLabel.AutoSize = false;
        subtitleLabel.Location = new Point(58, 24);
        subtitleLabel.Size = new Size(210, 34);
        panel.Resize += (_, _) =>
        {
            titleLabel.Width = Math.Max(60, panel.ClientSize.Width - 62);
            subtitleLabel.Width = Math.Max(60, panel.ClientSize.Width - 62);
        };
        panel.Controls.Add(toggle);
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(subtitleLabel);
        return panel;
    }

    private static void AddNumberField(TableLayoutPanel layout, string label, StepperControl input, int column, int labelRow)
    {
        var caption = MakeLabel(label, AppTheme.Muted, 8F, true);
        caption.Dock = DockStyle.Fill;
        layout.Controls.Add(caption, column, labelRow);
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 3, 12, 7);
        layout.Controls.Add(input, column, labelRow + 1);
    }

    private static Label TableCaption(string text)
    {
        var label = MakeLabel(text, AppTheme.Muted, 8F, true);
        label.AutoSize = false;
        label.Dock = DockStyle.Fill;
        return label;
    }

    private static StepperControl NumberInput(int minimum, int maximum, int value, int increment)
    {
        var input = new StepperControl
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Step = increment,
            Height = 40,
        };
        return input;
    }

    private static Label MakeLabel(string text, Color color, float size, bool semibold = false) => new()
    {
        Text = text,
        ForeColor = color,
        Font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size),
        BackColor = Color.Transparent,
        AutoSize = true,
    };

    private static void StyleTextBox(TextBox box)
    {
        box.BackColor = AppTheme.Raised;
        box.ForeColor = AppTheme.Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI Semibold", 11F);
    }

    private static ThemedButton MakeButton(string text, ButtonTone tone, ButtonGlyph glyph = ButtonGlyph.None, float fontSize = 9F)
    {
        var button = new ThemedButton();
        ConfigureButton(button, text, tone, glyph, fontSize);
        return button;
    }

    private static void ConfigureButton(ThemedButton button, string text, ButtonTone tone, ButtonGlyph glyph = ButtonGlyph.None, float fontSize = 9F)
    {
        button.Text = text;
        button.Glyph = glyph;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Font = new Font("Segoe UI Semibold", fontSize);
        button.AccessibleName = text;
        switch (tone)
        {
            case ButtonTone.Accent:
                button.FillColor = AppTheme.Accent;
                button.HoverColor = AppTheme.Blend(AppTheme.Accent, Color.White, 0.10);
                button.PressedColor = AppTheme.Blend(AppTheme.Accent, AppTheme.Canvas, 0.14);
                button.LabelColor = AppTheme.Canvas;
                button.DrawBorder = false;
                break;
            case ButtonTone.Record:
                button.FillColor = AppTheme.Coral;
                button.HoverColor = AppTheme.Blend(AppTheme.Coral, Color.White, 0.08);
                button.PressedColor = AppTheme.Blend(AppTheme.Coral, AppTheme.Canvas, 0.15);
                button.LabelColor = AppTheme.Canvas;
                button.DrawBorder = false;
                break;
            case ButtonTone.Icon:
                button.FillColor = AppTheme.Surface;
                button.HoverColor = AppTheme.RaisedHover;
                button.PressedColor = AppTheme.AccentDark;
                button.LabelColor = AppTheme.Muted;
                button.DrawBorder = false;
                break;
            default:
                button.FillColor = AppTheme.Raised;
                button.HoverColor = AppTheme.RaisedHover;
                button.PressedColor = AppTheme.AccentDark;
                button.LineColor = AppTheme.Border;
                button.LabelColor = AppTheme.Text;
                button.DrawBorder = true;
                break;
        }
    }

    private static ThemedButton ActionButton(string text, ButtonGlyph glyph, EventHandler handler)
    {
        var button = MakeButton(text, ButtonTone.Secondary, glyph, 8.3F);
        button.Size = new Size(138, 38);
        button.Margin = new Padding(0, 0, 8, 0);
        button.Click += handler;
        return button;
    }

    private void SetBusy(bool busy, Control? exception = null)
    {
        patternList.Enabled = !busy;
        foreach (var button in navButtons.Values) button.Enabled = !busy;
        recordButton.Enabled = !busy || exception == recordButton;
        playButton.Enabled = !busy || exception == playButton;
    }

    private void SetStatus(string status, string detail, Color color)
    {
        statusLabel.Text = status;
        statusLabel.ForeColor = color;
        statusDetail.Text = detail;
    }

    private void ShowError(string title, Exception exception)
    {
        SetStatus("ERROR", exception.Message, AppTheme.Coral);
        MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private static void ApplyDarkNativeTheme(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is DataGridView or NumericUpDown or TextBox)
            {
                NativeMethods.SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }

            if (control.HasChildren)
            {
                ApplyDarkNativeTheme(control);
            }
        }
    }

    private void AttachWindowDrag(Control control)
    {
        Point offset = Point.Empty;
        control.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                offset = new Point(e.X, e.Y);
            }
        };
        control.MouseMove += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && offset != Point.Empty)
            {
                Location = new Point(Left + e.X - offset.X, Top + e.Y - offset.Y);
            }
        };
    }

    private void DisposeRuntime()
    {
        playbackCancellation?.Cancel();
        playbackCancellation?.Dispose();
        routine.Dispose();
        calibrator.Dispose();
        recorder.Dispose();
        pageAnimationTimer.Dispose();
        toolTip.Dispose();
        if (IsHandleCreated)
        {
            NativeMethods.UnregisterHotKey(Handle, CueCaptureHotkeyId);
        }
    }

    private static string EventName(MacroEvent item) => item.Type switch
    {
        MacroEventType.MouseDown => "Mouse down",
        MacroEventType.MouseUp => "Mouse up",
        MacroEventType.MouseMove => "Mouse move",
        MacroEventType.MouseWheel => "Mouse wheel",
        MacroEventType.MouseHorizontalWheel => "Horizontal wheel",
        MacroEventType.KeyDown => "Key down",
        _ => "Key up",
    };

    private static string InputName(MacroEvent item) => item.Type switch
    {
        MacroEventType.KeyDown or MacroEventType.KeyUp => ((Keys)item.VirtualKey).ToString(),
        MacroEventType.MouseDown or MacroEventType.MouseUp => item.Data switch { 1 => "Left", 2 => "Right", 3 => "Middle", _ => $"Button {item.Data}" },
        MacroEventType.MouseWheel or MacroEventType.MouseHorizontalWheel => item.Data.ToString(),
        _ => string.Empty,
    };

    private static string PositionName(MacroEvent item) => item.Type is MacroEventType.MouseDown or MacroEventType.MouseUp or MacroEventType.MouseMove ? $"{item.X}, {item.Y}" : string.Empty;

    private enum AppPage
    {
        Studio,
        Editor,
        Routine,
        Settings,
    }

    private enum HotkeyAction
    {
        Record,
        Playback,
        EmergencyStop,
    }

    private enum ButtonTone
    {
        Secondary,
        Accent,
        Record,
        Icon,
    }
}
