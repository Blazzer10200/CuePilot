using System.Diagnostics;

namespace WorkflowLooper;

internal sealed class MainForm : Form
{
    private const int HotkeyRecord = 101;
    private const int HotkeyPlay = 102;
    private const int HotkeyPanic = 103;
    private const int PageLeft = 328;
    private const int PageTop = 136;
    private const int PageWidth = 688;
    private const int PageHeight = 568;

    private readonly GlobalRecorder recorder = new();
    private readonly PlaybackEngine playback = new();
    private readonly AppSettings appSettingsLoaded = SettingsStore.Load();
    private AppSettings appSettings;
    private AppSettings pendingSettings;
    private CancellationTokenSource? playbackCancellation;
    private CancellationTokenSource? recordingArming;
    private WorkflowPattern? currentPattern;
    private string? currentPath;
    private bool suppressLibrarySelection;
    private bool hotkeysRegistered;

    private readonly Panel statusDot = new();
    private readonly Label statusLabel = new();
    private readonly Label detailLabel = new();
    private readonly TextBox nameBox = new();
    private readonly ThemedButton recordButton = new();
    private readonly ThemedButton playButton = new();
    private readonly StepperControl loopsControl = new();
    private readonly StepperControl speedControl = new();
    private readonly ToggleSwitch mouseMovesSwitch = new();
    private readonly PatternListControl patternList = new();
    private readonly ThemedButton presetSelector = new() { ShowChevron = true };
    private readonly SmoothPanel presetPopover = new();
    private readonly List<PresetOptionControl> presetOptions = [];
    private readonly List<Control> libraryRegularControls = [];
    private readonly Label presetDescription = new();
    private WorkflowPreset selectedPreset = PresetFactory.BuiltIn[1];
    private readonly ThemedButton addPresetButton = new();
    private readonly ThemedButton clearButton = new();
    private readonly ThemedButton deleteButton = new();
    private readonly ThemedButton saveButton = new();
    private readonly ThemedButton openButton = new();
    private readonly ThemedButton folderButton = new();
    private readonly ThemedButton refreshButton = new();
    private readonly Label patternSummary = new();
    private readonly Label pathLabel = new();
    private readonly ThemedButton studioNavButton = new();
    private readonly ThemedButton guideNavButton = new();
    private readonly ThemedButton settingsNavButton = new();
    private readonly ThemedButton recordHotkeyButton = new();
    private readonly ThemedButton playbackHotkeyButton = new();
    private readonly ThemedButton panicHotkeyButton = new();
    private readonly Label guideRecordHotkey = new();
    private readonly Label guidePlaybackHotkey = new();
    private readonly Label guidePanicHotkey = new();
    private readonly Label footerLabel = new();
    private readonly Label captureHintLabel = new();

    private readonly System.Windows.Forms.Timer pageAnimationTimer = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer presetAnimationTimer = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer statusPulseTimer = new() { Interval = 45 };
    private SmoothPanel studioPage = null!;
    private SmoothPanel guidePage = null!;
    private SmoothPanel settingsPage = null!;
    private Control? outgoingPage;
    private Control? incomingPage;
    private AppPage activePage = AppPage.Studio;
    private int pageAnimationFrame;
    private int pageAnimationDirection;
    private bool presetOpening;
    private int presetAnimationFrame;
    private int statusPulseFrame;
    private Color statusBaseColor = AppTheme.Accent;
    private HotkeyAction? captureAction;
    private bool draggingWindow;
    private Point windowDragOffset;
    private readonly bool allowGlobalHotkeys;

    internal MainForm(bool registerGlobalHotkeys = true)
    {
        allowGlobalHotkeys = registerGlobalHotkeys;
        appSettings = appSettingsLoaded.Copy();
        pendingSettings = appSettings.Copy();
        AutoScaleMode = AutoScaleMode.None;
        Text = "Workflow Looper";
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(1040, 760);
        MinimumSize = Size;
        MaximumSize = Size;
        BackColor = AppTheme.Canvas;
        ForeColor = AppTheme.Text;
        Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;
        pageAnimationTimer.Tick += (_, _) => AdvancePageAnimation();
        presetAnimationTimer.Tick += (_, _) => AdvancePresetAnimation();
        statusPulseTimer.Tick += (_, _) => AdvanceStatusPulse();
        BuildInterface();
        SetStatus("READY", "Create, record, or select a pattern.", AppTheme.Accent);
        RefreshLibrary();
        TryLoadMostRecentPattern();
        RefreshHotkeyText();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var border = new Pen(AppTheme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        base.OnPaint(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (allowGlobalHotkeys)
        {
            RegisterHotkeys(appSettings, true);
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotkeys();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey)
        {
            switch (message.WParam.ToInt32())
            {
                case HotkeyRecord:
                    if (recorder.IsRecording)
                    {
                        StopRecording(appSettings.Record);
                    }
                    else
                    {
                        _ = ToggleRecordingAsync();
                    }

                    break;
                case HotkeyPlay:
                    _ = TogglePlaybackAsync();
                    break;
                case HotkeyPanic:
                    EmergencyStop();
                    break;
            }
        }

        base.WndProc(ref message);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (captureAction is not null)
        {
            CaptureHotkey(keyData);
            return true;
        }

        if (keyData == Keys.Escape && presetPopover.Visible)
        {
            TogglePresetPopover(false);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        playbackCancellation?.Cancel();
        recordingArming?.Cancel();
        UnregisterHotkeys();
        recorder.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildInterface()
    {
        BuildTitleBar();
        BuildHeader();
        BuildLibrary();
        BuildStudioPage();
        BuildGuidePage();
        BuildSettingsPage();
        guidePage.Visible = false;
        settingsPage.Visible = false;
        SetActiveNavigation();

        footerLabel.ForeColor = AppTheme.Muted;
        footerLabel.Font = new Font("Consolas", 8.5F);
        footerLabel.TextAlign = ContentAlignment.MiddleCenter;
        footerLabel.Location = new Point(24, 716);
        footerLabel.Size = new Size(992, 28);
        Controls.Add(footerLabel);
    }

    private void BuildTitleBar()
    {
        var bar = new SmoothPanel
        {
            BackColor = AppTheme.Surface,
            Location = new Point(1, 1),
            Size = new Size(1038, 37),
        };
        Controls.Add(bar);
        var mark = new Panel { BackColor = AppTheme.Accent, Location = new Point(14, 13), Size = new Size(10, 10) };
        bar.Controls.Add(mark);
        var caption = new Label
        {
            Text = "WORKFLOW LOOPER",
            ForeColor = AppTheme.Muted,
            Font = new Font("Segoe UI Semibold", 8.5F),
            Location = new Point(34, 8),
            Size = new Size(190, 22),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        bar.Controls.Add(caption);

        var minimize = MakeButton(string.Empty, new Rectangle(950, 0, 44, 37), ButtonTone.Icon);
        minimize.Glyph = ButtonGlyph.Minimize;
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        bar.Controls.Add(minimize);
        var close = MakeButton(string.Empty, new Rectangle(994, 0, 44, 37), ButtonTone.Icon);
        close.Glyph = ButtonGlyph.Close;
        close.HoverColor = AppTheme.Coral;
        close.PressedColor = AppTheme.Blend(AppTheme.Coral, AppTheme.Canvas, 0.18);
        close.Click += (_, _) => Close();
        bar.Controls.Add(close);

        AttachWindowDrag(bar);
        AttachWindowDrag(caption);
    }

    private void AttachWindowDrag(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            draggingWindow = true;
            windowDragOffset = control.PointToScreen(e.Location);
            windowDragOffset = new Point(windowDragOffset.X - Left, windowDragOffset.Y - Top);
        };
        control.MouseMove += (_, _) =>
        {
            if (draggingWindow)
            {
                Location = new Point(Cursor.Position.X - windowDragOffset.X, Cursor.Position.Y - windowDragOffset.Y);
            }
        };
        control.MouseUp += (_, _) => draggingWindow = false;
    }

    private void BuildHeader()
    {
        Controls.Add(new Label
        {
            Text = "WORKFLOW LOOPER",
            Font = new Font("Segoe UI Semibold", 23F),
            ForeColor = AppTheme.Text,
            AutoSize = true,
            Location = new Point(24, 51),
        });
        Controls.Add(new Label
        {
            Text = "VERSION 2.1   ·   LOCAL AUTOMATION STUDIO",
            ForeColor = AppTheme.Muted,
            Font = new Font("Segoe UI Semibold", 8F),
            AutoSize = true,
            Location = new Point(28, 94),
        });

        studioNavButton.Bounds = new Rectangle(432, 62, 92, 34);
        ConfigureNavButton(studioNavButton, "STUDIO", AppPage.Studio);
        guideNavButton.Bounds = new Rectangle(532, 62, 92, 34);
        ConfigureNavButton(guideNavButton, "GUIDE", AppPage.Guide);
        settingsNavButton.Bounds = new Rectangle(632, 62, 104, 34);
        ConfigureNavButton(settingsNavButton, "SETTINGS", AppPage.Settings);

        statusDot.BackColor = AppTheme.Accent;
        statusDot.Location = new Point(788, 60);
        statusDot.Size = new Size(9, 9);
        Controls.Add(statusDot);
        statusLabel.ForeColor = AppTheme.Accent;
        statusLabel.Font = new Font("Segoe UI Semibold", 9F);
        statusLabel.Location = new Point(807, 53);
        statusLabel.Size = new Size(205, 24);
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(statusLabel);
        detailLabel.ForeColor = AppTheme.Muted;
        detailLabel.Font = new Font("Segoe UI", 8.5F);
        detailLabel.Location = new Point(768, 78);
        detailLabel.Size = new Size(244, 38);
        detailLabel.TextAlign = ContentAlignment.TopRight;
        detailLabel.AutoEllipsis = true;
        Controls.Add(detailLabel);
    }

    private void ConfigureNavButton(ThemedButton button, string text, AppPage page)
    {
        button.Text = text;
        button.Font = new Font("Segoe UI Semibold", 8.5F);
        button.FillColor = AppTheme.Canvas;
        button.HoverColor = AppTheme.Raised;
        button.PressedColor = AppTheme.AccentDark;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Click += (_, _) => NavigateToPage(page);
        Controls.Add(button);
    }

    private void BuildLibrary()
    {
        var panel = CreateSurface(new Rectangle(24, PageTop, 288, PageHeight));
        panel.Controls.Add(MakeSectionLabel("PATTERN LIBRARY", 16, 18));

        refreshButton.Bounds = new Rectangle(212, 10, 28, 32);
        ConfigureIconButton(refreshButton, ButtonGlyph.Refresh);
        refreshButton.Click += (_, _) => RefreshLibrary(currentPath);
        panel.Controls.Add(refreshButton);
        folderButton.Bounds = new Rectangle(244, 10, 28, 32);
        ConfigureIconButton(folderButton, ButtonGlyph.Folder);
        folderButton.Click += (_, _) => OpenPatternFolder();
        panel.Controls.Add(folderButton);

        patternList.Bounds = new Rectangle(16, 56, 256, 322);
        patternList.SelectionChanged += (_, _) => SelectLibraryPattern();
        panel.Controls.Add(patternList);
        panel.Controls.Add(new Panel { BackColor = AppTheme.Border, Location = new Point(16, 394), Size = new Size(256, 1) });
        panel.Controls.Add(MakeSectionLabel("BUILT-IN PRESETS", 16, 412));

        presetSelector.Bounds = new Rectangle(16, 438, 256, 42);
        presetSelector.Font = new Font("Segoe UI Semibold", 9.5F);
        presetSelector.TextAlign = ContentAlignment.MiddleLeft;
        presetSelector.Click += (_, _) => TogglePresetPopover(!presetOpening && !presetPopover.Visible);
        panel.Controls.Add(presetSelector);
        presetDescription.ForeColor = AppTheme.Muted;
        presetDescription.Font = new Font("Segoe UI", 8.3F);
        presetDescription.Location = new Point(18, 486);
        presetDescription.Size = new Size(252, 26);
        presetDescription.AutoEllipsis = true;
        panel.Controls.Add(presetDescription);

        addPresetButton.Bounds = new Rectangle(16, 516, 256, 38);
        ConfigureButton(addPresetButton, "+  ADD PRESET TO LIBRARY", ButtonTone.Accent);
        addPresetButton.Click += (_, _) => AddSelectedPreset();
        panel.Controls.Add(addPresetButton);

        presetPopover.BackColor = AppTheme.Surface;
        presetPopover.Bounds = new Rectangle(288, 0, 286, 566);
        presetPopover.Visible = false;
        panel.Controls.Add(presetPopover);
        presetPopover.Controls.Add(MakeSectionLabel("CHOOSE A PRESET", 16, 18));
        presetPopover.Controls.Add(MakeMutedLabel("Pick a starting rhythm. You can fine-tune it with playback speed.", 16, 42, 238, 36));
        var closePresets = MakeButton(string.Empty, new Rectangle(244, 10, 28, 32), ButtonTone.Icon);
        closePresets.Glyph = ButtonGlyph.Close;
        closePresets.Click += (_, _) => TogglePresetPopover(false);
        presetPopover.Controls.Add(closePresets);
        for (var index = 0; index < PresetFactory.BuiltIn.Count; index++)
        {
            var option = new PresetOptionControl(PresetFactory.BuiltIn[index])
            {
                Bounds = new Rectangle(16, 92 + index * 86, 254, 78),
            };
            option.SelectedPreset += (_, _) =>
            {
                SelectPreset(option.Preset);
                TogglePresetPopover(false);
            };
            presetOptions.Add(option);
            presetPopover.Controls.Add(option);
        }

        presetPopover.Controls.Add(MakeMutedLabel("Selecting a preset returns you to the library. Add it when ready.", 16, 450, 254, 44));

        UpdatePresetDescription();
        libraryRegularControls.AddRange(panel.Controls.Cast<Control>().Where(control => control != presetPopover));
    }

    private void BuildStudioPage()
    {
        studioPage = CreatePage();
        studioPage.Controls.Add(MakeSectionLabel("ACTIVE PATTERN", 18, 18));
        var nameFrame = new SmoothPanel
        {
            BackColor = AppTheme.Raised,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(18, 48),
            Size = new Size(652, 38),
        };
        studioPage.Controls.Add(nameFrame);
        nameBox.Text = "New workflow";
        nameBox.BackColor = AppTheme.Raised;
        nameBox.ForeColor = AppTheme.Text;
        nameBox.BorderStyle = BorderStyle.None;
        nameBox.Font = new Font("Segoe UI Semibold", 12F);
        nameBox.Location = new Point(10, 8);
        nameBox.Size = new Size(630, 24);
        nameFrame.Controls.Add(nameBox);

        patternSummary.ForeColor = AppTheme.Text;
        patternSummary.Font = new Font("Segoe UI Semibold", 9F);
        patternSummary.Location = new Point(20, 94);
        patternSummary.Size = new Size(328, 22);
        studioPage.Controls.Add(patternSummary);
        pathLabel.ForeColor = AppTheme.Muted;
        pathLabel.Font = new Font("Segoe UI", 8.5F);
        pathLabel.TextAlign = ContentAlignment.MiddleRight;
        pathLabel.AutoEllipsis = true;
        pathLabel.Location = new Point(352, 94);
        pathLabel.Size = new Size(316, 22);
        studioPage.Controls.Add(pathLabel);

        recordButton.Bounds = new Rectangle(18, 126, 318, 66);
        ConfigureButton(recordButton, "●  RECORD", ButtonTone.Record, 12F);
        recordButton.Click += async (_, _) => await ToggleRecordingAsync();
        studioPage.Controls.Add(recordButton);
        playButton.Bounds = new Rectangle(352, 126, 318, 66);
        ConfigureButton(playButton, "▶  PLAY", ButtonTone.Accent, 12F);
        playButton.Click += async (_, _) => await TogglePlaybackAsync();
        studioPage.Controls.Add(playButton);

        var timingPanel = new SmoothPanel
        {
            BackColor = AppTheme.Canvas,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(18, 210),
            Size = new Size(652, 126),
        };
        studioPage.Controls.Add(timingPanel);
        timingPanel.Controls.Add(MakeSectionLabel("LOOP COUNT", 16, 14));
        loopsControl.Bounds = new Rectangle(16, 42, 166, 42);
        loopsControl.Minimum = 0;
        loopsControl.Maximum = 9999;
        loopsControl.Value = 1;
        timingPanel.Controls.Add(loopsControl);
        timingPanel.Controls.Add(MakeMutedLabel("0 means continuous", 18, 91, 165, 20));

        timingPanel.Controls.Add(MakeSectionLabel("PLAYBACK SPEED", 204, 14));
        speedControl.Bounds = new Rectangle(204, 42, 166, 42);
        speedControl.Minimum = 25;
        speedControl.Maximum = 400;
        speedControl.Step = 5;
        speedControl.Value = 100;
        speedControl.Suffix = "%";
        timingPanel.Controls.Add(speedControl);
        timingPanel.Controls.Add(MakeMutedLabel("Recorded timing scale", 206, 91, 166, 20));

        timingPanel.Controls.Add(new Label
        {
            Text = "●  HIGH-RES TIMING ACTIVE",
            ForeColor = AppTheme.Accent,
            Font = new Font("Segoe UI Semibold", 8F),
            Location = new Point(404, 14),
            Size = new Size(224, 20),
        });
        mouseMovesSwitch.Location = new Point(404, 47);
        mouseMovesSwitch.Checked = false;
        mouseMovesSwitch.CheckedChanged += (_, _) => UpdatePatternSummary();
        timingPanel.Controls.Add(mouseMovesSwitch);
        timingPanel.Controls.Add(new Label
        {
            Text = "TRACK CURSOR POSITION",
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI Semibold", 8.5F),
            Location = new Point(460, 47),
            Size = new Size(176, 22),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        timingPanel.Controls.Add(MakeMutedLabel("Off keeps click/key playback stationary.", 406, 86, 228, 28));

        studioPage.Controls.Add(MakeSectionLabel("PATTERN ACTIONS", 18, 356));
        openButton.Bounds = new Rectangle(18, 382, 148, 42);
        ConfigureButton(openButton, "OPEN FILE", ButtonTone.Secondary);
        openButton.Click += (_, _) => OpenPattern();
        studioPage.Controls.Add(openButton);
        saveButton.Bounds = new Rectangle(178, 382, 148, 42);
        ConfigureButton(saveButton, "SAVE AS", ButtonTone.Secondary);
        saveButton.Click += (_, _) => SavePatternAs();
        studioPage.Controls.Add(saveButton);
        clearButton.Bounds = new Rectangle(338, 382, 166, 42);
        ConfigureButton(clearButton, "CLEAR CURRENT", ButtonTone.Secondary);
        clearButton.Click += (_, _) => ClearCurrentPattern();
        studioPage.Controls.Add(clearButton);
        deleteButton.Bounds = new Rectangle(516, 382, 154, 42);
        ConfigureButton(deleteButton, "DELETE", ButtonTone.Danger);
        deleteButton.Click += (_, _) => DeleteCurrentPattern();
        studioPage.Controls.Add(deleteButton);

        var privacy = new SmoothPanel
        {
            BackColor = AppTheme.Raised,
            Location = new Point(18, 444),
            Size = new Size(652, 102),
        };
        studioPage.Controls.Add(privacy);
        privacy.Controls.Add(new Label
        {
            Text = "LOCAL BY DESIGN",
            ForeColor = AppTheme.Accent,
            Font = new Font("Segoe UI Semibold", 8F),
            Location = new Point(16, 14),
            Size = new Size(180, 20),
        });
        privacy.Controls.Add(new Label
        {
            Text = "Your patterns stay on this PC. No screenshots, telemetry, cloud account, or network access.",
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI", 9F),
            Location = new Point(16, 40),
            Size = new Size(616, 24),
        });
        privacy.Controls.Add(MakeMutedLabel("Settings and shortcut choices are stored beside the local pattern library.", 16, 70, 616, 22));
    }

    private void BuildGuidePage()
    {
        guidePage = CreatePage();
        guidePage.Controls.Add(MakeSectionLabel("QUICK START", 18, 18));
        guidePage.Controls.Add(new Label
        {
            Text = "Build a reliable pattern in three clean steps.",
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI Semibold", 15F),
            Location = new Point(18, 46),
            Size = new Size(630, 34),
        });
        AddGuideCard(guidePage, new Rectangle(18, 94, 204, 140), "01", "RECORD", "Name it, begin recording, then perform the workflow once.");
        AddGuideCard(guidePage, new Rectangle(242, 94, 204, 140), "02", "REVIEW", "Stop recording. The pattern saves into your local library.");
        AddGuideCard(guidePage, new Rectangle(466, 94, 204, 140), "03", "PLAY", "Test one loop at 100%, then increase the loop count when stable.");

        guidePage.Controls.Add(MakeSectionLabel("GLOBAL CONTROLS", 18, 258));
        var hotkeys = new SmoothPanel
        {
            BackColor = AppTheme.Canvas,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(18, 284),
            Size = new Size(652, 112),
        };
        guidePage.Controls.Add(hotkeys);
        AddHotkeyRow(hotkeys, 14, guideRecordHotkey, "Start or stop recording");
        AddHotkeyRow(hotkeys, 43, guidePlaybackHotkey, "Start or stop playback");
        AddHotkeyRow(hotkeys, 72, guidePanicHotkey, "Emergency stop and release held inputs");

        var release = new SmoothPanel
        {
            BackColor = AppTheme.Raised,
            Location = new Point(18, 418),
            Size = new Size(652, 128),
        };
        guidePage.Controls.Add(release);
        release.Controls.Add(new Label
        {
            Text = "OPEN, LOCAL, RELEASE-READY",
            ForeColor = AppTheme.Accent,
            Font = new Font("Segoe UI Semibold", 8F),
            Location = new Point(16, 14),
            Size = new Size(250, 20),
        });
        release.Controls.Add(new Label
        {
            Text = "Source, an MIT license, automated Windows builds, and versioned releases are included.",
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI", 9.5F),
            Location = new Point(16, 42),
            Size = new Size(616, 42),
        });
        release.Controls.Add(MakeMutedLabel("Tip: leave cursor tracking off for workflows made entirely of clicks and keys.", 16, 88, 616, 24));
    }

    private void BuildSettingsPage()
    {
        settingsPage = CreatePage();
        settingsPage.Controls.Add(MakeSectionLabel("GLOBAL SHORTCUTS", 18, 18));
        settingsPage.Controls.Add(new Label
        {
            Text = "Make the controls fit your workflow.",
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI Semibold", 15F),
            Location = new Point(18, 46),
            Size = new Size(630, 34),
        });
        settingsPage.Controls.Add(MakeMutedLabel("Select a shortcut, press a combination, then save to apply it.", 18, 78, 640, 26));

        AddHotkeyCard(settingsPage, 112, HotkeyAction.Record, "RECORD / STOP", "Starts recording or finishes the active capture.", recordHotkeyButton);
        AddHotkeyCard(settingsPage, 204, HotkeyAction.Playback, "PLAY / STOP", "Starts or cancels playback of the active pattern.", playbackHotkeyButton);
        AddHotkeyCard(settingsPage, 296, HotkeyAction.EmergencyStop, "EMERGENCY STOP", "Stops automation and releases held inputs.", panicHotkeyButton);

        var saveShortcuts = MakeButton("SAVE SHORTCUTS", new Rectangle(18, 402, 214, 44), ButtonTone.Accent);
        saveShortcuts.Click += (_, _) => ApplyPendingHotkeys();
        settingsPage.Controls.Add(saveShortcuts);
        var resetShortcuts = MakeButton("RESTORE DEFAULTS", new Rectangle(244, 402, 214, 44), ButtonTone.Secondary);
        resetShortcuts.Click += (_, _) =>
        {
            pendingSettings = AppSettings.Defaults();
            RefreshHotkeyText();
            captureHintLabel.Text = "Defaults staged. Select Save shortcuts to apply them.";
            SetStatus("DEFAULTS STAGED", "Review, then save the restored shortcuts.", AppTheme.Warning);
        };
        settingsPage.Controls.Add(resetShortcuts);

        captureHintLabel.Text = "Tip: use Ctrl, Shift, or Alt for record and playback shortcuts to avoid accidental triggers.";
        captureHintLabel.ForeColor = AppTheme.Muted;
        captureHintLabel.Font = new Font("Segoe UI", 8.5F);
        captureHintLabel.Location = new Point(18, 462);
        captureHintLabel.Size = new Size(640, 24);
        settingsPage.Controls.Add(captureHintLabel);

        var local = new SmoothPanel
        {
            BackColor = AppTheme.Raised,
            Location = new Point(18, 496),
            Size = new Size(652, 50),
        };
        local.Controls.Add(new Label
        {
            Text = "SAVED LOCALLY   ·   NO ACCOUNT REQUIRED   ·   CONFLICT CHECKED BEFORE APPLY",
            ForeColor = AppTheme.Accent,
            Font = new Font("Consolas", 8.5F, FontStyle.Bold),
            Location = new Point(16, 14),
            Size = new Size(620, 22),
            TextAlign = ContentAlignment.MiddleLeft,
        });
        settingsPage.Controls.Add(local);
    }

    private void AddHotkeyCard(Control parent, int y, HotkeyAction action, string title, string detail, ThemedButton button)
    {
        var card = new SmoothPanel
        {
            BackColor = AppTheme.Raised,
            Location = new Point(18, y),
            Size = new Size(652, 80),
        };
        parent.Controls.Add(card);
        card.Controls.Add(new Label
        {
            Text = title,
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI Semibold", 9.5F),
            Location = new Point(16, 14),
            Size = new Size(260, 22),
        });
        card.Controls.Add(MakeMutedLabel(detail, 16, 42, 348, 22));
        button.Bounds = new Rectangle(388, 17, 246, 46);
        button.Font = new Font("Consolas", 9F, FontStyle.Bold);
        ConfigureButton(button, string.Empty, ButtonTone.Secondary);
        button.Click += (_, _) => BeginHotkeyCapture(action);
        card.Controls.Add(button);
    }

    private async Task ToggleRecordingAsync()
    {
        if (recordingArming is not null)
        {
            recordingArming.Cancel();
            return;
        }

        if (playback.IsPlaying || playbackCancellation is not null)
        {
            SetStatus("BUSY", "Stop playback before recording.", AppTheme.Warning);
            return;
        }

        if (recorder.IsRecording)
        {
            StopRecording(null);
            return;
        }

        recordingArming = new CancellationTokenSource();
        try
        {
            SetStatus("ARMING", "Recording begins in half a second…", AppTheme.Warning);
            SetControlsEnabled(false);
            WindowState = FormWindowState.Minimized;
            await Task.Delay(500, recordingArming.Token);
            recorder.Start(mouseMovesSwitch.Checked);
            SetStatus("RECORDING", $"Press {appSettings.Record.DisplayText} to finish.", AppTheme.Coral);
            recordButton.Text = "■  STOP RECORDING";
        }
        catch (OperationCanceledException)
        {
            RestoreWindow();
            SetControlsEnabled(true);
            SetStatus("CANCELLED", "Recording was not started.", AppTheme.Warning);
        }
        catch (Exception exception)
        {
            RestoreWindow();
            SetControlsEnabled(true);
            ShowError("Recording could not start", exception);
        }
        finally
        {
            recordingArming?.Dispose();
            recordingArming = null;
        }
    }

    private void StopRecording(HotkeyBinding? stopHotkey)
    {
        try
        {
            currentPattern = recorder.Stop(nameBox.Text, stopHotkey);
            if (currentPattern.Events.Count == 0)
            {
                currentPattern = null;
                currentPath = null;
                SetStatus("EMPTY", "Nothing was captured. Try recording again.", AppTheme.Warning);
                return;
            }

            currentPath = WorkflowStore.SaveNew(currentPattern);
            nameBox.Text = currentPattern.Name;
            RefreshLibrary(currentPath);
            UpdatePatternSummary();
            SetStatus("SAVED", "Pattern captured and added to the library.", AppTheme.Accent);
        }
        catch (Exception exception)
        {
            ShowError("Recording could not be saved", exception);
        }
        finally
        {
            recordButton.Text = "●  RECORD";
            RestoreWindow();
            SetControlsEnabled(true);
        }
    }

    private async Task TogglePlaybackAsync()
    {
        if (playbackCancellation is not null)
        {
            playbackCancellation.Cancel();
            return;
        }

        if (recorder.IsRecording || recordingArming is not null)
        {
            SetStatus("BUSY", "Finish recording before playback.", AppTheme.Warning);
            return;
        }

        if (currentPattern is null)
        {
            SetStatus("NO PATTERN", "Select, record, or add a pattern first.", AppTheme.Warning);
            return;
        }

        playbackCancellation = new CancellationTokenSource();
        try
        {
            SetControlsEnabled(false);
            playButton.Text = "■  STOP PLAYBACK";
            SetStatus("ARMING", "Playback begins in half a second…", AppTheme.Warning);
            WindowState = FormWindowState.Minimized;
            await Task.Delay(500, playbackCancellation.Token);
            SetStatus("PLAYING", $"Press {appSettings.EmergencyStop.DisplayText} for an immediate stop.", AppTheme.Accent);
            await playback.PlayAsync(
                currentPattern,
                loopsControl.Value,
                speedControl.Value / 100m,
                mouseMovesSwitch.Checked,
                playbackCancellation.Token);
            SetStatus("COMPLETE", $"Mean error {playback.LastMeanLatenessMilliseconds:F2} ms · max {playback.LastMaximumLatenessMilliseconds:F2} ms.", AppTheme.Accent);
        }
        catch (OperationCanceledException)
        {
            SetStatus("STOPPED", "Playback stopped; held inputs were released.", AppTheme.Warning);
        }
        catch (Exception exception)
        {
            ShowError("Playback failed", exception);
        }
        finally
        {
            playButton.Text = "▶  PLAY";
            SetControlsEnabled(true);
            RestoreWindow();
            playbackCancellation?.Dispose();
            playbackCancellation = null;
        }
    }

    private void EmergencyStop()
    {
        if (recorder.IsRecording)
        {
            StopRecording(appSettings.EmergencyStop);
        }

        recordingArming?.Cancel();
        playbackCancellation?.Cancel();
        SetStatus("EMERGENCY STOP", "Automation halted safely.", AppTheme.Warning);
        RestoreWindow();
    }

    private void AddSelectedPreset()
    {
        try
        {
            currentPattern = PresetFactory.Create(selectedPreset);
            currentPath = WorkflowStore.SaveNew(currentPattern);
            nameBox.Text = currentPattern.Name;
            loopsControl.Value = 1;
            mouseMovesSwitch.Checked = false;
            RefreshLibrary(currentPath);
            UpdatePatternSummary();
            SetStatus("PRESET ADDED", $"{selectedPreset.Name} is ready for a one-loop test.", AppTheme.Accent);
        }
        catch (Exception exception)
        {
            ShowError("Preset could not be added", exception);
        }
    }

    private void ClearCurrentPattern()
    {
        currentPattern = null;
        currentPath = null;
        nameBox.Text = "New workflow";
        suppressLibrarySelection = true;
        patternList.ClearSelection();
        suppressLibrarySelection = false;
        UpdatePatternSummary();
        SetStatus("CLEARED", "Current pattern unloaded; saved files are untouched.", AppTheme.Accent);
    }

    private void DeleteCurrentPattern()
    {
        if (currentPattern is null || string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
        {
            SetStatus("NOT SAVED", "The active pattern has no local file to delete.", AppTheme.Warning);
            return;
        }

        var fullPath = Path.GetFullPath(currentPath);
        var libraryRoot = Path.GetFullPath(WorkflowStore.PatternDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("EXTERNAL FILE", "Only library patterns can be deleted here.", AppTheme.Warning);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Delete '{currentPattern.Name}' from the pattern library?\n\nThis removes the saved workflow file.",
            "Delete pattern",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        try
        {
            File.Delete(fullPath);
            ClearCurrentPattern();
            RefreshLibrary();
            SetStatus("DELETED", "Pattern removed from the library.", AppTheme.Warning);
        }
        catch (Exception exception)
        {
            ShowError("Pattern could not be deleted", exception);
        }
    }

    private void OpenPattern()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open a Workflow Looper pattern",
            Filter = "Workflow patterns (*.workflow.json)|*.workflow.json|JSON files (*.json)|*.json",
            InitialDirectory = WorkflowStore.PatternDirectory,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadPattern(dialog.FileName, true);
        }
    }

    private void SavePatternAs()
    {
        if (currentPattern is null)
        {
            SetStatus("NO PATTERN", "Nothing is loaded to save.", AppTheme.Warning);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save workflow pattern",
            Filter = "Workflow patterns (*.workflow.json)|*.workflow.json",
            InitialDirectory = WorkflowStore.PatternDirectory,
            FileName = $"{currentPattern.Name}.workflow.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            currentPattern.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? currentPattern.Name : nameBox.Text.Trim();
            WorkflowStore.Save(currentPattern, dialog.FileName);
            currentPath = dialog.FileName;
            RefreshLibrary(currentPath);
            UpdatePatternSummary();
            SetStatus("SAVED", "Pattern saved to the selected location.", AppTheme.Accent);
        }
        catch (Exception exception)
        {
            ShowError("Pattern could not be saved", exception);
        }
    }

    private void RefreshLibrary(string? selectPath = null)
    {
        try
        {
            Directory.CreateDirectory(WorkflowStore.PatternDirectory);
            var entries = new DirectoryInfo(WorkflowStore.PatternDirectory)
                .GetFiles("*.workflow.json")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => new PatternListItem(file.FullName, WorkflowStore.Load(file.FullName)))
                .ToList();
            suppressLibrarySelection = true;
            patternList.SetItems(entries, selectPath);
        }
        catch (Exception exception)
        {
            SetStatus("LIBRARY ERROR", exception.Message, AppTheme.Coral);
        }
        finally
        {
            suppressLibrarySelection = false;
        }
    }

    private void SelectLibraryPattern()
    {
        if (!suppressLibrarySelection && patternList.SelectedItem is { } entry)
        {
            TogglePresetPopover(false);
            LoadPattern(entry.Path, false);
        }
    }

    private void LoadPattern(string path, bool refreshLibrary)
    {
        try
        {
            currentPattern = WorkflowStore.Load(path);
            currentPath = path;
            nameBox.Text = currentPattern.Name;
            if (refreshLibrary)
            {
                RefreshLibrary(currentPath);
            }

            UpdatePatternSummary();
            SetStatus("LOADED", $"{currentPattern.Name} is ready.", AppTheme.Accent);
        }
        catch (Exception exception)
        {
            ShowError("Pattern could not be opened", exception);
        }
    }

    private void TryLoadMostRecentPattern()
    {
        patternList.SelectFirst();
        if (patternList.SelectedItem is { } latest)
        {
            LoadPattern(latest.Path, false);
        }
    }

    private void OpenPatternFolder()
    {
        try
        {
            Directory.CreateDirectory(WorkflowStore.PatternDirectory);
            Process.Start(new ProcessStartInfo { FileName = WorkflowStore.PatternDirectory, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError("Pattern folder could not be opened", exception);
        }
    }

    private void UpdatePatternSummary()
    {
        if (currentPattern is null)
        {
            patternSummary.Text = "No pattern selected";
            pathLabel.Text = "Record a workflow or choose a preset.";
            clearButton.Enabled = false;
            deleteButton.Enabled = false;
            saveButton.Enabled = false;
            return;
        }

        var leftClicks = currentPattern.Events.Count(item => item.Type == MacroEventType.MouseDown && item.Data == 1);
        var keyPresses = currentPattern.Events.Count(item => item.Type == MacroEventType.KeyDown);
        var mouseMoves = currentPattern.Events.Count(item => item.Type == MacroEventType.MouseMove);
        patternSummary.Text = $"{leftClicks:N0} clicks   ·   {keyPresses:N0} keys   ·   {mouseMoves:N0} moves {(mouseMovesSwitch.Checked ? "used" : "ignored")}";
        pathLabel.Text = string.IsNullOrWhiteSpace(currentPath)
            ? "Unsaved pattern"
            : $"{(IsLibraryPath(currentPath) ? "LOCAL LIBRARY" : "OPEN FILE")}   ·   {Path.GetFileName(currentPath)}";
        clearButton.Enabled = true;
        saveButton.Enabled = true;
        deleteButton.Enabled = IsLibraryPath(currentPath);
    }

    private static bool IsLibraryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var root = Path.GetFullPath(WorkflowStore.PatternDirectory) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectPreset(WorkflowPreset preset)
    {
        selectedPreset = preset;
        UpdatePresetDescription();
    }

    private void UpdatePresetDescription()
    {
        presetSelector.Text = selectedPreset.Name;
        presetDescription.Text = selectedPreset.Description;
        foreach (var option in presetOptions)
        {
            option.Selected = option.Preset == selectedPreset;
            option.Invalidate();
        }
    }

    private void TogglePresetPopover(bool open)
    {
        presetOpening = open;
        presetAnimationFrame = 0;
        presetAnimationTimer.Stop();
        if (!SystemInformation.IsMenuAnimationEnabled)
        {
            presetPopover.Bounds = new Rectangle(open ? 0 : 288, 0, 286, 566);
            presetPopover.Visible = open;
            SetLibraryControlsVisible(!open);
            if (open)
            {
                presetPopover.BringToFront();
            }

            return;
        }

        if (open)
        {
            SetLibraryControlsVisible(false);
            presetPopover.Visible = true;
            presetPopover.BringToFront();
        }

        presetAnimationTimer.Start();
    }

    private void AdvancePresetAnimation()
    {
        presetAnimationFrame++;
        const int frames = 8;
        var progress = Math.Min(1d, presetAnimationFrame / (double)frames);
        var eased = 1d - Math.Pow(1d - progress, 3d);
        presetPopover.Left = presetOpening
            ? (int)Math.Round(288 * (1d - eased))
            : (int)Math.Round(288 * eased);
        if (presetAnimationFrame < frames)
        {
            return;
        }

        presetAnimationTimer.Stop();
        presetPopover.Visible = presetOpening;
        if (!presetOpening)
        {
            SetLibraryControlsVisible(true);
        }
    }

    private void BeginHotkeyCapture(HotkeyAction action)
    {
        if (recorder.IsRecording || playbackCancellation is not null || recordingArming is not null)
        {
            SetStatus("BUSY", "Stop automation before changing shortcuts.", AppTheme.Warning);
            return;
        }

        captureAction = action;
        UnregisterHotkeys();
        RefreshHotkeyText();
        GetHotkeyButton(action).Text = "PRESS COMBINATION…";
        captureHintLabel.Text = "Listening now. Press Esc to cancel.";
        SetStatus("LISTENING", $"Press a new shortcut for {ActionName(action)}.", AppTheme.Warning);
    }

    private void CaptureHotkey(Keys keyData)
    {
        if (captureAction is not { } action)
        {
            return;
        }

        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape)
        {
            EndHotkeyCapture(false);
            SetStatus("CANCELLED", "Shortcut capture cancelled.", AppTheme.Warning);
            return;
        }

        var candidate = new HotkeyBinding
        {
            Key = key,
            Control = (keyData & Keys.Control) == Keys.Control,
            Shift = (keyData & Keys.Shift) == Keys.Shift,
            Alt = (keyData & Keys.Alt) == Keys.Alt,
        };
        if (!SettingsStore.IsValid(candidate))
        {
            SetStatus("INVALID SHORTCUT", "Press a non-modifier key with the combination.", AppTheme.Coral);
            return;
        }

        if (action is HotkeyAction.Record or HotkeyAction.Playback && !candidate.Control && !candidate.Shift && !candidate.Alt)
        {
            SetStatus("ADD A MODIFIER", "Record and playback shortcuts require Ctrl, Shift, or Alt.", AppTheme.Warning);
            return;
        }

        var previousBinding = GetBinding(pendingSettings, action).Copy();
        SetBinding(pendingSettings, action, candidate);
        if (pendingSettings.HasDuplicates())
        {
            SetBinding(pendingSettings, action, previousBinding);
            RefreshHotkeyText();
            GetHotkeyButton(action).Text = "PRESS COMBINATION…";
            SetStatus("DUPLICATE SHORTCUT", "Each action needs a unique combination.", AppTheme.Coral);
            return;
        }

        EndHotkeyCapture(true);
        captureHintLabel.Text = "Shortcut staged. Select Save shortcuts to apply it.";
        SetStatus("SHORTCUT STAGED", $"{ActionName(action)} will use {candidate.DisplayText}.", AppTheme.Warning);
    }

    private void EndHotkeyCapture(bool keepPending)
    {
        captureAction = null;
        if (!keepPending)
        {
            pendingSettings = appSettings.Copy();
        }

        RegisterHotkeys(appSettings, false);
        RefreshHotkeyText();
        captureHintLabel.Text = "Tip: use Ctrl, Shift, or Alt for record and playback shortcuts to avoid accidental triggers.";
    }

    private void ApplyPendingHotkeys()
    {
        if (captureAction is not null)
        {
            EndHotkeyCapture(true);
        }

        if (!SettingsStore.IsValid(pendingSettings))
        {
            SetStatus("INVALID SHORTCUTS", "Choose three valid, unique shortcuts.", AppTheme.Coral);
            return;
        }

        var previous = appSettings.Copy();
        UnregisterHotkeys();
        if (!RegisterHotkeys(pendingSettings, false))
        {
            RegisterHotkeys(previous, false);
            SetStatus("HOTKEY CONFLICT", "Windows or another app already owns one of those shortcuts.", AppTheme.Coral);
            return;
        }

        try
        {
            SettingsStore.Save(pendingSettings);
            appSettings = pendingSettings.Copy();
            RefreshHotkeyText();
            captureHintLabel.Text = "Shortcuts saved locally and active now.";
            SetStatus("SHORTCUTS SAVED", "New global controls are active.", AppTheme.Accent);
        }
        catch (Exception exception)
        {
            UnregisterHotkeys();
            RegisterHotkeys(previous, false);
            pendingSettings = previous.Copy();
            ShowError("Shortcuts could not be saved", exception);
        }
    }

    private bool RegisterHotkeys(AppSettings settings, bool reportConflict)
    {
        if (!IsHandleCreated)
        {
            return false;
        }

        UnregisterHotkeys();
        var recordOk = NativeMethods.RegisterHotKey(Handle, HotkeyRecord, settings.Record.NativeModifiers, (uint)settings.Record.Key);
        var playOk = recordOk && NativeMethods.RegisterHotKey(Handle, HotkeyPlay, settings.Playback.NativeModifiers, (uint)settings.Playback.Key);
        var panicOk = playOk && NativeMethods.RegisterHotKey(Handle, HotkeyPanic, settings.EmergencyStop.NativeModifiers, (uint)settings.EmergencyStop.Key);
        hotkeysRegistered = recordOk && playOk && panicOk;
        if (!hotkeysRegistered)
        {
            UnregisterHotkeys();
            if (reportConflict)
            {
                SetStatus("HOTKEY CONFLICT", "Another app owns one of the configured shortcuts.", AppTheme.Warning);
            }
        }

        return hotkeysRegistered;
    }

    private void UnregisterHotkeys()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(Handle, HotkeyRecord);
        NativeMethods.UnregisterHotKey(Handle, HotkeyPlay);
        NativeMethods.UnregisterHotKey(Handle, HotkeyPanic);
        hotkeysRegistered = false;
    }

    private void RefreshHotkeyText()
    {
        recordHotkeyButton.Text = pendingSettings.Record.DisplayText;
        playbackHotkeyButton.Text = pendingSettings.Playback.DisplayText;
        panicHotkeyButton.Text = pendingSettings.EmergencyStop.DisplayText;
        guideRecordHotkey.Text = appSettings.Record.DisplayText;
        guidePlaybackHotkey.Text = appSettings.Playback.DisplayText;
        guidePanicHotkey.Text = appSettings.EmergencyStop.DisplayText;
        footerLabel.Text = $"{appSettings.Record.DisplayText}   RECORD     ·     {appSettings.Playback.DisplayText}   PLAY / STOP     ·     {appSettings.EmergencyStop.DisplayText}   EMERGENCY STOP";
    }

    private static void SetBinding(AppSettings settings, HotkeyAction action, HotkeyBinding binding)
    {
        switch (action)
        {
            case HotkeyAction.Record:
                settings.Record = binding;
                break;
            case HotkeyAction.Playback:
                settings.Playback = binding;
                break;
            case HotkeyAction.EmergencyStop:
                settings.EmergencyStop = binding;
                break;
        }
    }

    private static HotkeyBinding GetBinding(AppSettings settings, HotkeyAction action) => action switch
    {
        HotkeyAction.Record => settings.Record,
        HotkeyAction.Playback => settings.Playback,
        _ => settings.EmergencyStop,
    };

    private ThemedButton GetHotkeyButton(HotkeyAction action) => action switch
    {
        HotkeyAction.Record => recordHotkeyButton,
        HotkeyAction.Playback => playbackHotkeyButton,
        _ => panicHotkeyButton,
    };

    private static string ActionName(HotkeyAction action) => action switch
    {
        HotkeyAction.Record => "record / stop",
        HotkeyAction.Playback => "play / stop",
        _ => "emergency stop",
    };

    private void NavigateToPage(AppPage page)
    {
        if (page == activePage || pageAnimationTimer.Enabled)
        {
            return;
        }

        TogglePresetPopover(false);
        outgoingPage = PageFor(activePage);
        incomingPage = PageFor(page);
        pageAnimationDirection = (int)page > (int)activePage ? 1 : -1;
        activePage = page;
        SetActiveNavigation();
        if (!SystemInformation.IsMenuAnimationEnabled)
        {
            outgoingPage.Visible = false;
            incomingPage.Left = PageLeft;
            incomingPage.Visible = true;
            incomingPage.BringToFront();
            return;
        }

        pageAnimationFrame = 0;
        incomingPage.Left = PageLeft + pageAnimationDirection * 52;
        incomingPage.Visible = true;
        incomingPage.BringToFront();
        pageAnimationTimer.Start();
    }

    internal void ShowGuideForPreview() => ShowPageInstant(AppPage.Guide);
    internal void ShowSettingsForPreview() => ShowPageInstant(AppPage.Settings);
    internal void ShowPresetForPreview()
    {
        presetOpening = true;
        SetLibraryControlsVisible(false);
        presetPopover.Bounds = new Rectangle(0, 0, 286, 566);
        presetPopover.Visible = true;
        presetPopover.BringToFront();
    }

    private void SetLibraryControlsVisible(bool visible)
    {
        foreach (var control in libraryRegularControls)
        {
            control.Visible = visible;
        }
    }

    private void ShowPageInstant(AppPage page)
    {
        activePage = page;
        studioPage.Visible = page == AppPage.Studio;
        guidePage.Visible = page == AppPage.Guide;
        settingsPage.Visible = page == AppPage.Settings;
        var panel = PageFor(page);
        panel.Left = PageLeft;
        panel.BringToFront();
        SetActiveNavigation();
    }

    private Control PageFor(AppPage page) => page switch
    {
        AppPage.Studio => studioPage,
        AppPage.Guide => guidePage,
        _ => settingsPage,
    };

    private void AdvancePageAnimation()
    {
        if (outgoingPage is null || incomingPage is null)
        {
            pageAnimationTimer.Stop();
            return;
        }

        pageAnimationFrame++;
        const int frames = 12;
        var progress = Math.Min(1d, pageAnimationFrame / (double)frames);
        var eased = 1d - Math.Pow(1d - progress, 3d);
        outgoingPage.Left = PageLeft - pageAnimationDirection * (int)Math.Round(34 * eased);
        incomingPage.Left = PageLeft + pageAnimationDirection * (int)Math.Round(52 * (1d - eased));
        if (pageAnimationFrame < frames)
        {
            return;
        }

        pageAnimationTimer.Stop();
        outgoingPage.Visible = false;
        outgoingPage.Left = PageLeft;
        incomingPage.Left = PageLeft;
        outgoingPage = null;
        incomingPage = null;
    }

    private void SetActiveNavigation()
    {
        SetNavigationState(studioNavButton, activePage == AppPage.Studio);
        SetNavigationState(guideNavButton, activePage == AppPage.Guide);
        SetNavigationState(settingsNavButton, activePage == AppPage.Settings);
    }

    private static void SetNavigationState(ThemedButton button, bool active)
    {
        button.FillColor = active ? AppTheme.AccentDark : AppTheme.Canvas;
        button.LineColor = active ? AppTheme.Accent : AppTheme.Border;
        button.LabelColor = active ? AppTheme.Accent : AppTheme.Muted;
        button.Invalidate();
    }

    private void SetControlsEnabled(bool enabled)
    {
        nameBox.Enabled = enabled;
        loopsControl.Enabled = enabled;
        speedControl.Enabled = enabled;
        mouseMovesSwitch.Enabled = enabled;
        patternList.Enabled = enabled;
        presetSelector.Enabled = enabled;
        addPresetButton.Enabled = enabled;
        openButton.Enabled = enabled;
        folderButton.Enabled = enabled;
        refreshButton.Enabled = enabled;
        clearButton.Enabled = enabled && currentPattern is not null;
        deleteButton.Enabled = enabled && IsLibraryPath(currentPath);
        saveButton.Enabled = enabled && currentPattern is not null;
        recordButton.Enabled = enabled || recorder.IsRecording;
        playButton.Enabled = enabled || playback.IsPlaying;
        settingsNavButton.Enabled = enabled;
    }

    private void RestoreWindow()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Show();
        Activate();
    }

    private void SetStatus(string status, string detail, Color color)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(status, detail, color));
            return;
        }

        statusBaseColor = color;
        statusDot.BackColor = color;
        statusLabel.Text = status;
        statusLabel.ForeColor = color;
        detailLabel.Text = detail;
        if (status is "ARMING" or "RECORDING" or "PLAYING" or "LISTENING" && SystemInformation.IsMenuAnimationEnabled)
        {
            statusPulseTimer.Start();
        }
        else
        {
            statusPulseTimer.Stop();
            statusPulseFrame = 0;
        }
    }

    private void AdvanceStatusPulse()
    {
        statusPulseFrame = (statusPulseFrame + 1) % 40;
        var wave = (Math.Sin(statusPulseFrame / 40d * Math.PI * 2d) + 1d) / 2d;
        statusDot.BackColor = AppTheme.Blend(statusBaseColor, AppTheme.Canvas, 0.18 + wave * 0.34);
    }

    private void ShowError(string title, Exception exception)
    {
        RestoreWindow();
        SetStatus("ERROR", exception.Message, AppTheme.Coral);
        MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private SmoothPanel CreateSurface(Rectangle bounds)
    {
        var panel = new SmoothPanel
        {
            Bounds = bounds,
            BackColor = AppTheme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(panel);
        return panel;
    }

    private SmoothPanel CreatePage() => CreateSurface(new Rectangle(PageLeft, PageTop, PageWidth, PageHeight));

    private static Label MakeSectionLabel(string text, int x, int y) => new()
    {
        Text = text,
        ForeColor = AppTheme.Muted,
        Font = new Font("Segoe UI Semibold", 8F),
        Location = new Point(x, y),
        AutoSize = true,
    };

    private static Label MakeMutedLabel(string text, int x, int y, int width, int height) => new()
    {
        Text = text,
        ForeColor = AppTheme.Muted,
        Font = new Font("Segoe UI", 8.5F),
        Location = new Point(x, y),
        Size = new Size(width, height),
    };

    private static ThemedButton MakeButton(string text, Rectangle bounds, ButtonTone tone, float fontSize = 9F)
    {
        var button = new ThemedButton { Bounds = bounds };
        ConfigureButton(button, text, tone, fontSize);
        return button;
    }

    private static void ConfigureButton(ThemedButton button, string text, ButtonTone tone, float fontSize = 9F)
    {
        button.Text = text;
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Font = new Font("Segoe UI Semibold", fontSize);
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
            case ButtonTone.Danger:
                button.FillColor = AppTheme.Surface;
                button.HoverColor = AppTheme.Blend(AppTheme.Surface, AppTheme.Coral, 0.16);
                button.PressedColor = AppTheme.Blend(AppTheme.Surface, AppTheme.Coral, 0.26);
                button.LineColor = AppTheme.Coral;
                button.LabelColor = AppTheme.Coral;
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
                break;
        }
    }

    private static void ConfigureIconButton(ThemedButton button, ButtonGlyph glyph)
    {
        ConfigureButton(button, string.Empty, ButtonTone.Icon, 11F);
        button.Glyph = glyph;
        button.TextAlign = ContentAlignment.MiddleCenter;
    }

    private static void AddGuideCard(Control parent, Rectangle bounds, string number, string title, string body)
    {
        var card = new SmoothPanel { Bounds = bounds, BackColor = AppTheme.Raised };
        parent.Controls.Add(card);
        card.Controls.Add(new Label
        {
            Text = number,
            ForeColor = AppTheme.Accent,
            Font = new Font("Consolas", 10F, FontStyle.Bold),
            Location = new Point(14, 14),
            Size = new Size(44, 22),
        });
        card.Controls.Add(new Label
        {
            Text = title,
            ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI Semibold", 10F),
            Location = new Point(14, 43),
            Size = new Size(bounds.Width - 28, 24),
        });
        card.Controls.Add(new Label
        {
            Text = body,
            ForeColor = AppTheme.Muted,
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(14, 76),
            Size = new Size(bounds.Width - 28, 52),
        });
    }

    private static void AddHotkeyRow(Control parent, int y, Label hotkeyLabel, string description)
    {
        hotkeyLabel.ForeColor = AppTheme.Accent;
        hotkeyLabel.Font = new Font("Consolas", 8.5F, FontStyle.Bold);
        hotkeyLabel.Location = new Point(16, y);
        hotkeyLabel.Size = new Size(230, 22);
        parent.Controls.Add(hotkeyLabel);
        parent.Controls.Add(new Label
        {
            Text = description,
            ForeColor = AppTheme.Muted,
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(258, y),
            Size = new Size(372, 22),
        });
    }

    private enum AppPage
    {
        Studio,
        Guide,
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
        Danger,
        Icon,
    }
}
