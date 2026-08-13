namespace WorkflowLooper;

internal sealed class OperatorSettingsDrawer : Panel
{
    private TableLayoutPanel? layoutRoot;
    private readonly WindowChromeButton closeButton = new(WindowChromeKind.Close);
    private readonly NumericSetting lower = new("Pulse threshold", 25, 80, 55, 1, "%");
    private readonly NumericSetting target = new("Target tension", 30, 85, 68, 1, "%");
    private readonly NumericSetting pulse = new("Max pulse", 35, 120, 90, 5, "ms");
    private readonly NumericSetting duration = new("Safety time", 5, 3600, 210, 5, "sec");
    private readonly CheckBox collectTimeout = new();
    private readonly ConsoleChoice inputMode = new();
    private readonly ConsoleButton emergencyStop = new()
    {
        Text = "STOP IMMEDIATELY  ·  PAUSE / BREAK",
        Tone = ButtonTone.Danger,
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 4, 0, 4),
    };
    private FishingRoutineSettings loadedSettings = new();

    internal event EventHandler? ApplyRequested;
    internal event EventHandler? CancelRequested;
    internal event EventHandler? EmergencyStopRequested;

    internal OperatorSettingsDrawer()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.Raised;
        Padding = new Padding(20, 18, 20, 18);

        layoutRoot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            BackColor = AppTheme.Raised,
        };
        layoutRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layoutRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layoutRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layoutRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layoutRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        layoutRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layoutRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layoutRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layoutRoot.Controls.Add(BuildHeader(), 0, 0);
        layoutRoot.Controls.Add(BuildEmergencyStop(), 0, 1);
        layoutRoot.Controls.Add(new Label
        {
            Text = "Tune the proven controller envelope. Change one value at a time, then validate it against a live catch.",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F),
            ForeColor = AppTheme.Muted,
        }, 0, 2);
        layoutRoot.Controls.Add(SectionLabel("CONTROLLER ENVELOPE  ·  PROVEN BASELINE"), 0, 3);
        layoutRoot.Controls.Add(BuildNumericGrid(), 0, 4);
        layoutRoot.Controls.Add(BuildInputMode(), 0, 5);
        layoutRoot.Controls.Add(BuildSafety(), 0, 6);
        layoutRoot.Controls.Add(BuildActions(), 0, 7);
        Controls.Add(layoutRoot);

        closeButton.Dock = DockStyle.None;
        closeButton.Click += (_, _) => Cancel();
        Controls.Add(closeButton);
        closeButton.BringToFront();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        ConstrainLayout();
    }

    internal void ConstrainLayout()
    {
        if (layoutRoot is null) return;
        layoutRoot.Dock = DockStyle.None;
        layoutRoot.Bounds = new Rectangle(
            Padding.Left,
            Padding.Top,
            Math.Max(0, ClientSize.Width - Padding.Horizontal),
            Math.Max(0, ClientSize.Height - Padding.Vertical));
        closeButton.Bounds = new Rectangle(Math.Max(Padding.Left, ClientSize.Width - Padding.Right - 32), Padding.Top, 32, 32);
        closeButton.BringToFront();
        layoutRoot.PerformLayout();
    }

    internal void LoadFrom(FishingRoutineSettings settings)
    {
        loadedSettings = settings.Copy();
        ApplyValues(loadedSettings);
    }

    private void ApplyValues(FishingRoutineSettings settings)
    {
        lower.Value = settings.FishingLowerTensionPercent;
        target.Value = settings.FishingUpperTensionPercent;
        pulse.Value = settings.FishingMaximumPulseMilliseconds;
        duration.Value = settings.MaximumDurationSeconds;
        collectTimeout.Checked = settings.CollectOnTimeout;
        inputMode.SelectedIndex = settings.InputMode switch
        {
            InputDeliveryMode.Application => 1,
            InputDeliveryMode.Foreground => 2,
            _ => 0,
        };
    }

    private void Cancel()
    {
        ApplyValues(loadedSettings);
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    internal void SaveTo(FishingRoutineSettings settings)
    {
        settings.FishingLowerTensionPercent = (int)lower.Value;
        settings.FishingUpperTensionPercent = (int)target.Value;
        settings.FishingMaximumPulseMilliseconds = (int)pulse.Value;
        settings.MaximumDurationSeconds = (int)duration.Value;
        settings.CollectOnTimeout = collectTimeout.Checked;
        settings.InputMode = inputMode.SelectedIndex switch
        {
            1 => InputDeliveryMode.Application,
            2 => InputDeliveryMode.Foreground,
            _ => InputDeliveryMode.Automatic,
        };
        settings.Clamp();
    }

    internal void SetAutomationActive(bool active) => emergencyStop.Enabled = active;
    internal bool EmergencyStopEnabledForTest => emergencyStop.Enabled;
    internal void FocusPrimaryField() => lower.FocusInput();
    internal bool PrimaryFieldFocusedForTest => lower.InputFocusedForTest;

    private Control BuildHeader()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = AppTheme.Raised, Padding = new Padding(0, 0, 40, 0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var copy = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = AppTheme.Raised, Margin = Padding.Empty };
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        copy.Controls.Add(new Label
        {
            Text = "FISHING PROFILE",
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 7.5F, FontStyle.Bold),
            ForeColor = AppTheme.Mint,
        }, 0, 0);
        copy.Controls.Add(new Label
        {
            Text = "ADVANCED TUNING",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 15F),
            ForeColor = AppTheme.Text,
        }, 0, 1);
        row.Controls.Add(copy, 0, 0);
        return row;
    }

    private Control BuildEmergencyStop()
    {
        emergencyStop.Click += (_, _) => EmergencyStopRequested?.Invoke(this, EventArgs.Empty);
        return emergencyStop;
    }

    private Control BuildNumericGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = AppTheme.Raised };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        AddNumeric(grid, lower, 0, 0);
        AddNumeric(grid, target, 1, 0);
        AddNumeric(grid, pulse, 0, 1);
        AddNumeric(grid, duration, 1, 1);
        return grid;
    }

    private Control BuildInputMode()
    {
        var wrap = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = AppTheme.Raised, Padding = new Padding(0, 8, 0, 0) };
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        wrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inputMode.Dock = DockStyle.Fill;
        inputMode.AccessibleName = "Input delivery";
        inputMode.AddRange(["AUTO · FOCUS FIVEM", "EXPERIMENTAL · BACKGROUND", "FOREGROUND ONLY"]);
        wrap.Controls.Add(SectionLabel("INPUT DELIVERY"), 0, 0);
        wrap.Controls.Add(inputMode, 0, 1);
        wrap.Controls.Add(new Label
        {
            Text = "Auto activates FiveM and sends verified physical scan-code input.",
            Dock = DockStyle.Fill,
            ForeColor = AppTheme.Muted,
            Font = new Font("Segoe UI", 7.2F),
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 2);
        return wrap;
    }

    private Control BuildSafety()
    {
        var wrap = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Raised, Padding = new Padding(0, 14, 0, 0) };
        collectTimeout.Text = "Collect when the safety timer expires";
        collectTimeout.ForeColor = AppTheme.Text;
        collectTimeout.Font = new Font("Segoe UI", 9F);
        collectTimeout.Dock = DockStyle.Top;
        collectTimeout.Height = 28;
        collectTimeout.FlatStyle = FlatStyle.Flat;
        wrap.Controls.Add(collectTimeout);
        return wrap;
    }

    private Control BuildActions()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = AppTheme.Raised };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var cancel = new ConsoleButton { Text = "CANCEL", Tone = ButtonTone.Ghost, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 5, 0) };
        var apply = new ConsoleButton { Text = "APPLY", Tone = ButtonTone.Primary, Dock = DockStyle.Fill, Margin = new Padding(5, 0, 0, 0) };
        cancel.Click += (_, _) => Cancel();
        apply.Click += (_, _) => ApplyRequested?.Invoke(this, EventArgs.Empty);
        row.Controls.Add(cancel, 0, 0);
        row.Controls.Add(apply, 1, 0);
        return row;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 7.5F, FontStyle.Bold),
        ForeColor = AppTheme.Text,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static void AddNumeric(TableLayoutPanel grid, Control control, int column, int row)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(column == 0 ? 0 : 5, row == 0 ? 0 : 5, column == 0 ? 5 : 0, row == 0 ? 5 : 0);
        grid.Controls.Add(control, column, row);
    }
}
