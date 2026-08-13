namespace WorkflowLooper;

internal sealed class OperatorConsoleDashboard : UserControl
{
    private readonly Label state = new();
    private readonly Label detail = new();
    private readonly Label targetName = new();
    private readonly Label targetHealth = new();
    private readonly Label captureHealth = new();
    private readonly Label inputHealth = new();
    private readonly Label confidenceValue = new();
    private readonly Label confidenceUnit = new();
    private readonly Label samples = new();
    private readonly Label runMode = new();
    private readonly Label runIdentifier = new();
    private readonly TelemetryReadout profileMetric = new("PROFILE");
    private readonly TelemetryReadout cycleMetric = new("CYCLE");
    private readonly TelemetryReadout runtimeMetric = new("RUNTIME");
    private readonly TelemetryReadout lastCatchMetric = new("LAST CATCH");
    private readonly Label loopStatus = new();
    private readonly Panel[] signalSegments = new Panel[10];
    private readonly List<CycleStep> cycleSteps = [];
    private readonly ConsoleButton runAction = new() { Text = "START AUTOMATION", Tone = ButtonTone.Primary, IconKind = ConsoleButtonIcon.Play };
    private readonly ConsoleButton target = new() { Text = "SET TARGET", Tone = ButtonTone.Secondary, IconKind = ConsoleButtonIcon.Target };
    private readonly ConsoleButton advanced = new() { Text = "ADVANCED TUNING", Tone = ButtonTone.Ghost, IconKind = ConsoleButtonIcon.Settings };
    private readonly OperatorSettingsDrawer settingsDrawer = new();
    private readonly DimmingOverlay advancedVeil = new();
    private readonly Panel advancedHost = new();
    private readonly TableLayoutPanel root;
    private readonly System.Windows.Forms.Timer runtimeTimer = new() { Interval = 1_000 };
    private readonly ToolTip detailToolTip = new();
    private DateTimeOffset? runStartedAt;
    private DateTimeOffset? lastCatchAt;
    private RoutineState previousState = RoutineState.Stopped;
    private int cycleCount;
    private bool active;
    private bool targetConfigured;

    internal event EventHandler? StartRequested;
    internal event EventHandler? StopRequested;
    internal event EventHandler? TargetRequested;
    internal event EventHandler? SettingsApplyRequested;

    internal OperatorConsoleDashboard()
    {
        Dock = DockStyle.Fill;
        BackColor = AppTheme.Canvas;
        Padding = new Padding(22, 15, 22, 18);

        root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = AppTheme.Canvas };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 182));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 134));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildHeading(), 0, 0);
        root.Controls.Add(BuildInstrument(), 0, 1);
        root.Controls.Add(BuildTelemetry(), 0, 2);
        root.Controls.Add(BuildCycle(), 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        Controls.Add(root);
        targetName.Dock = DockStyle.None;
        targetName.Margin = Padding.Empty;
        Controls.Add(targetName);
        targetName.BringToFront();

        advancedVeil.Visible = false;
        advancedVeil.Click += (_, _) => SetAdvancedVisible(false);
        Controls.Add(advancedVeil);

        advancedHost.Dock = DockStyle.None;
        advancedHost.BackColor = AppTheme.Raised;
        advancedHost.Padding = new Padding(1, 0, 0, 0);
        advancedHost.Visible = false;
        advancedHost.Controls.Add(settingsDrawer);
        Controls.Add(advancedHost);
        advancedHost.BringToFront();
        SizeChanged += (_, _) => UpdateAdvancedBounds();
        UpdateAdvancedBounds();

        runAction.Click += (_, _) =>
        {
            if (active) StopRequested?.Invoke(this, EventArgs.Empty);
            else StartRequested?.Invoke(this, EventArgs.Empty);
        };
        target.Click += (_, _) => TargetRequested?.Invoke(this, EventArgs.Empty);
        advanced.Click += (_, _) => SetAdvancedVisible(true);
        settingsDrawer.CancelRequested += (_, _) => SetAdvancedVisible(false);
        settingsDrawer.EmergencyStopRequested += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        settingsDrawer.ApplyRequested += (_, _) =>
        {
            SettingsApplyRequested?.Invoke(this, EventArgs.Empty);
            SetAdvancedVisible(false);
        };
        runtimeTimer.Tick += (_, _) => UpdateTelemetry();
        Disposed += (_, _) =>
        {
            runtimeTimer.Dispose();
            detailToolTip.Dispose();
        };

        SetStatus(new RoutineStatus(RoutineState.Stopped, "Select FiveM as the target, then start the complete fishing loop."));
        SetTarget(null, "No target configured");
    }

    internal void LoadSettings(FishingRoutineSettings settings) => settingsDrawer.LoadFrom(settings);
    internal void SaveSettings(FishingRoutineSettings settings) => settingsDrawer.SaveTo(settings);
    internal void AttachFooter(Control footer) => root.Controls.Add(footer, 0, 5);

    internal void SetTarget(WindowTargetSettings? configured, string healthDetail)
    {
        var ready = configured?.IsConfigured == true;
        targetConfigured = ready;
        var targetDisplay = ready ? "●  FIVEM  ·  TARGET LOCKED" : "NO TARGET SELECTED";
        targetName.Text = targetDisplay;
        detailToolTip.SetToolTip(targetName, targetDisplay);
        SetHealth(targetHealth, ready ? "TARGET LOCKED" : "TARGET REQUIRED", ready ? AppTheme.Mint : AppTheme.Amber);
        SetHealth(captureHealth, ready ? "CAPTURE CHECK ON START" : "CAPTURE WAITING", AppTheme.Muted);
        SetHealth(inputHealth, ready ? "INPUT CHECK ON START" : "INPUT WAITING", AppTheme.Muted);
        if (!active) SetStatus(new RoutineStatus(RoutineState.Stopped, healthDetail));
    }

    internal void SetStatus(RoutineStatus status)
    {
        var wasActive = active;
        active = status.State is not (RoutineState.Stopped or RoutineState.Faulted);
        if (active && !wasActive)
        {
            runStartedAt = DateTimeOffset.Now;
            lastCatchAt = null;
            cycleCount = 1;
            runtimeTimer.Start();
        }
        else if (!active)
        {
            runtimeTimer.Stop();
            if (status.State == RoutineState.Stopped)
            {
                runStartedAt = null;
                lastCatchAt = null;
                cycleCount = 0;
            }
        }
        if (status.State == RoutineState.Collecting && previousState != RoutineState.Collecting)
            lastCatchAt = DateTimeOffset.Now;
        if (status.State == RoutineState.Casting && previousState == RoutineState.Stowing)
            cycleCount++;
        state.Text = status.State switch
        {
            RoutineState.Stopped when !targetConfigured => "TARGET REQUIRED",
            RoutineState.Stopped => "READY TO START",
            RoutineState.Armed => "WATCHING FOR MINIGAME",
            RoutineState.Regulating => "CONTROLLING TENSION",
            RoutineState.Collecting => "COLLECTING FISH",
            RoutineState.Stowing => "PREPARING NEXT CAST",
            RoutineState.Casting => "RUNNING PREFLIGHT",
            RoutineState.Faulted => "AUTOMATION FAULT",
            _ => status.State.ToString().ToUpperInvariant(),
        };
        state.ForeColor = status.State switch
        {
            RoutineState.Faulted => AppTheme.Coral,
            RoutineState.Stopped when !targetConfigured => AppTheme.Amber,
            RoutineState.Casting or RoutineState.Stowing => AppTheme.Amber,
            RoutineState.Stopped => AppTheme.Text,
            _ => AppTheme.Mint,
        };
        detail.Text = status.Detail;
        detailToolTip.SetToolTip(detail, status.Detail);
        runMode.Text = status.State switch
        {
            RoutineState.Stopped when !targetConfigured => "SETUP",
            RoutineState.Stopped => "READY",
            RoutineState.Faulted => "ALERT",
            _ => "ACTIVE",
        };
        runMode.ForeColor = status.State switch
        {
            RoutineState.Faulted => AppTheme.Coral,
            RoutineState.Stopped when !targetConfigured => AppTheme.Amber,
            _ => AppTheme.Mint,
        };
        runMode.BackColor = status.State switch
        {
            RoutineState.Faulted => AppTheme.Coral,
            RoutineState.Stopped when !targetConfigured => AppTheme.Amber,
            RoutineState.Stopped => AppTheme.Surface,
            _ => AppTheme.Mint,
        };
        if (active || status.State == RoutineState.Faulted || (status.State == RoutineState.Stopped && !targetConfigured))
            runMode.ForeColor = AppTheme.Canvas;
        runIdentifier.Text = active
            ? $"RUN {Math.Max(cycleCount, 1):0000}  /  FISHING"
            : targetConfigured ? "TARGET  /  FISHING" : "NO TARGET  /  FISHING";
        UpdateSignal(status.Confidence, status.SampleCount, status.State);
        UpdateCycle(status.State);
        UpdateTelemetry();
        runAction.Text = active
            ? "STOP IMMEDIATELY  ·  PAUSE / BREAK"
            : status.State == RoutineState.Faulted
                ? "RETRY AUTOMATION"
                : targetConfigured ? "START AUTOMATION" : "SET TARGET TO CONTINUE";
        runAction.Tone = active ? ButtonTone.Danger : ButtonTone.Primary;
        runAction.IconKind = active ? ConsoleButtonIcon.Stop : ConsoleButtonIcon.Play;
        runAction.Enabled = active || targetConfigured;
        target.Enabled = !active;
        settingsDrawer.SetAutomationActive(active);

        if (status.State == RoutineState.Armed)
        {
            SetHealth(captureHealth, "DESKTOP CAPTURE LIVE", AppTheme.Mint);
            SetHealth(inputHealth, "INPUT VERIFIED", AppTheme.Mint);
        }
        else if (status.State == RoutineState.Faulted)
        {
            SetHealth(captureHealth, "CAPTURE / FOCUS FAILED", AppTheme.Coral);
            SetHealth(inputHealth, "CHECK FAILED", AppTheme.Coral);
        }
        else if (status.State == RoutineState.Stopped)
        {
            SetHealth(captureHealth, targetConfigured ? "CAPTURE CHECK ON START" : "CAPTURE WAITING", AppTheme.Muted);
            SetHealth(inputHealth, targetConfigured ? "INPUT CHECK ON START" : "INPUT WAITING", AppTheme.Muted);
        }
        previousState = status.State;
    }

    internal string StateTextForTest => state.Text;
    internal bool AdvancedVisibleForTest => advancedHost.Visible;
    internal bool DrawerEmergencyEnabledForTest => settingsDrawer.EmergencyStopEnabledForTest;
    internal int ActiveCycleStepForTest => cycleSteps.FindIndex(step => step.IsActiveForTest);
    internal string RuntimeValueForTest => runtimeMetric.ValueForTest;
    internal bool TargetEnabledForTest => target.Enabled;
    internal string CaptureHealthForTest => captureHealth.Text;
    internal string InputHealthForTest => inputHealth.Text;
    internal string RunModeForTest => runMode.Text;
    internal Color RuntimeColorForTest => runtimeMetric.ValueColorForTest;
    internal bool AdvancedPrimaryFieldFocusedForTest => settingsDrawer.PrimaryFieldFocusedForTest;
    internal bool RunActionEnabledForTest => runAction.Enabled;
    internal string DetectorStatusForTest => samples.Text;
    internal string RunActionTextForTest => runAction.Text;
    internal bool MainActionsInTabOrderForTest => runAction.TabStop || target.TabStop || advanced.TabStop;
    internal void ShowAdvancedForTest() => SetAdvancedVisible(true);
    internal void HideAdvancedForTest() => SetAdvancedVisible(false);
    internal void HideAdvancedForPreview() => SetAdvancedVisible(false, restoreFocus: false);
    internal Rectangle AdvancedBoundsForTest => advancedHost.Bounds;
    internal Control AdvancedControlForTest => advancedHost;
    internal void FocusPrimaryAction() => (runAction.Enabled ? runAction : target).Focus();
    internal void PrimeForRenderTest()
    {
        advancedVeil.Bounds = ClientRectangle;
        advancedVeil.Visible = false;
        advancedHost.Dock = DockStyle.None;
        advancedHost.Bounds = new Rectangle(ClientSize.Width + 1, 0, ScaleLogical(392), ClientSize.Height);
        advancedHost.Visible = true;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        PositionFloatingControls();
    }

    private Control BuildHeading()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = AppTheme.Canvas, Margin = new Padding(0, 0, 0, 14) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        var copy = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = AppTheme.Canvas, Margin = Padding.Empty };
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        copy.Controls.Add(new Label
        {
            Text = "FISHING  /  ACTIVE PROFILE",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Font = new Font("Consolas", 8F, FontStyle.Bold),
            ForeColor = AppTheme.Mint,
        }, 0, 0);
        copy.Controls.Add(new Label
        {
            Text = "AUTOMATION CONSOLE",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Font = new Font("Segoe UI Semibold", 18F),
            ForeColor = AppTheme.Text,
        }, 0, 1);
        targetName.ForeColor = AppTheme.Text;
        targetName.BackColor = AppTheme.Canvas;
        targetName.BorderStyle = BorderStyle.FixedSingle;
        targetName.Font = new Font("Consolas", 7F);
        targetName.TextAlign = ContentAlignment.MiddleLeft;
        targetName.Padding = new Padding(9, 0, 0, 0);
        row.Controls.Add(copy, 0, 0);
        return row;
    }

    private Control BuildInstrument()
    {
        var instrument = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = AppTheme.Border, Padding = new Padding(1), Margin = new Padding(0, 0, 0, 14) };
        instrument.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 77));
        instrument.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = AppTheme.Surface, Padding = new Padding(12, 16, 12, 14), Margin = new Padding(0, 0, 1, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        var runMetadata = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = AppTheme.Surface, Margin = Padding.Empty };
        runMetadata.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        runMetadata.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        runMode.Dock = DockStyle.Fill;
        runMode.Font = new Font("Consolas", 6.5F, FontStyle.Bold);
        runMode.ForeColor = AppTheme.Mint;
        runMode.TextAlign = ContentAlignment.MiddleCenter;
        runMode.Margin = new Padding(0, 2, 8, 2);
        runIdentifier.Dock = DockStyle.Fill;
        runIdentifier.Font = new Font("Consolas", 6.5F);
        runIdentifier.ForeColor = AppTheme.Muted;
        runMetadata.Controls.Add(runMode, 0, 0);
        runMetadata.Controls.Add(runIdentifier, 1, 0);
        left.Controls.Add(runMetadata, 0, 0);
        state.Dock = DockStyle.Fill;
        state.Margin = Padding.Empty;
        state.Font = new Font("Segoe UI Semibold", 17F);
        state.ForeColor = AppTheme.Mint;
        left.Controls.Add(state, 0, 1);
        detail.Dock = DockStyle.Fill;
        detail.Margin = Padding.Empty;
        detail.Font = new Font("Segoe UI", 8.4F);
        detail.ForeColor = AppTheme.Muted;
        detail.AutoEllipsis = true;
        left.Controls.Add(detail, 0, 2);
        var health = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, BackColor = AppTheme.Surface, Margin = Padding.Empty };
        for (var index = 0; index < 3; index++) health.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        health.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var label in new[] { targetHealth, captureHealth, inputHealth })
        {
            label.Dock = DockStyle.Fill;
            label.Font = new Font("Consolas", 6.5F, FontStyle.Bold);
            label.TextAlign = ContentAlignment.MiddleLeft;
        }
        health.Controls.Add(targetHealth, 0, 0);
        health.Controls.Add(captureHealth, 1, 0);
        health.Controls.Add(inputHealth, 2, 0);
        left.Controls.Add(health, 0, 3);
        instrument.Controls.Add(left, 0, 0);
        instrument.Controls.Add(BuildSignal(), 1, 0);
        return instrument;
    }

    private Control BuildSignal()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, BackColor = AppTheme.Surface, Padding = new Padding(18, 18, 18, 12) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = "DETECTOR LOCK",
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8F, FontStyle.Bold),
            ForeColor = AppTheme.Muted,
        }, 0, 0);
        var valueRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = AppTheme.Surface };
        valueRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        valueRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        confidenceValue.Dock = DockStyle.Fill;
        confidenceValue.Font = new Font("Consolas", 31F, FontStyle.Bold);
        confidenceValue.ForeColor = AppTheme.Text;
        confidenceValue.TextAlign = ContentAlignment.BottomLeft;
        confidenceUnit.Text = "%";
        confidenceUnit.Dock = DockStyle.Fill;
        confidenceUnit.Font = new Font("Consolas", 13F, FontStyle.Bold);
        confidenceUnit.ForeColor = AppTheme.Mint;
        confidenceUnit.TextAlign = ContentAlignment.BottomLeft;
        valueRow.Controls.Add(confidenceValue, 0, 0);
        valueRow.Controls.Add(confidenceUnit, 1, 0);
        panel.Controls.Add(valueRow, 0, 1);
        var scale = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 10, BackColor = AppTheme.Surface };
        for (var index = 0; index < signalSegments.Length; index++)
        {
            scale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10));
            signalSegments[index] = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Border, Margin = new Padding(0, 1, 3, 1) };
            scale.Controls.Add(signalSegments[index], index, 0);
        }
        panel.Controls.Add(scale, 0, 2);
        samples.Dock = DockStyle.Fill;
        samples.Font = new Font("Consolas", 7.5F);
        samples.ForeColor = AppTheme.Muted;
        panel.Controls.Add(samples, 0, 3);
        return panel;
    }

    private Control BuildTelemetry()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, BackColor = AppTheme.Border, Margin = new Padding(0, 0, 0, 14), Padding = new Padding(0, 1, 0, 1) };
        for (var index = 0; index < 4; index++) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        AddTelemetry(row, 0, profileMetric);
        AddTelemetry(row, 1, cycleMetric);
        AddTelemetry(row, 2, runtimeMetric);
        AddTelemetry(row, 3, lastCatchMetric);
        profileMetric.SetValue("FISHING");
        return row;
    }

    private Control BuildCycle()
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = AppTheme.Surface, Padding = new Padding(12, 9, 12, 10), Margin = new Padding(0, 0, 0, 14) };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = AppTheme.Surface };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        header.Controls.Add(Caption("AUTOMATION CYCLE", AppTheme.Text), 0, 0);
        loopStatus.Text = "STANDBY  ·  START TO VERIFY";
        loopStatus.Dock = DockStyle.Fill;
        loopStatus.Font = new Font("Consolas", 7.5F, FontStyle.Bold);
        loopStatus.ForeColor = AppTheme.Muted;
        loopStatus.TextAlign = ContentAlignment.MiddleRight;
        header.Controls.Add(loopStatus, 1, 0);
        card.Controls.Add(header, 0, 0);
        var rail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, BackColor = AppTheme.Border };
        for (var index = 0; index < 6; index++) rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.6667F));
        var data = new[]
        {
            ("01", "CAST", "E PRESS"), ("02", "ACQUIRE", "METER SEARCH"),
            ("03", "CONTROL", "LMB PULSE"), ("04", "COLLECT", "E PRESS"),
            ("05", "RESET", "10 SEC"), ("06", "REPEAT", "AUTO CAST"),
        };
        for (var index = 0; index < data.Length; index++)
        {
            var step = new CycleStep(data[index].Item1, data[index].Item2, data[index].Item3);
            step.Root.Margin = new Padding(0, 0, index == data.Length - 1 ? 0 : 1, 0);
            cycleSteps.Add(step);
            rail.Controls.Add(step.Root, index, 0);
        }
        card.Controls.Add(rail, 0, 1);
        return card;
    }

    private Control BuildActions()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = AppTheme.Canvas };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 177));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        runAction.Dock = target.Dock = advanced.Dock = DockStyle.Fill;
        runAction.Margin = new Padding(0, 0, 8, 0);
        target.Margin = new Padding(0, 0, 8, 0);
        advanced.Margin = Padding.Empty;
        row.Controls.Add(runAction, 0, 0);
        row.Controls.Add(target, 1, 0);
        row.Controls.Add(advanced, 2, 0);
        return row;
    }

    private void SetAdvancedVisible(bool visible, bool restoreFocus = true)
    {
        UpdateAdvancedBounds();
        advancedVeil.Visible = visible;
        advancedHost.Visible = visible;
        runAction.TabStop = target.TabStop = advanced.TabStop = !visible;
        if (visible)
        {
            advancedVeil.BringToFront();
            advancedHost.BringToFront();
            BeginInvoke(settingsDrawer.FocusPrimaryField);
        }
        else if (restoreFocus && advanced.CanFocus)
        {
            advanced.Focus();
        }
    }

    private void UpdateAdvancedBounds()
    {
        var drawerWidth = Math.Min(ClientSize.Width, ScaleLogical(392));
        advancedVeil.Bounds = ClientRectangle;
        advancedHost.Bounds = new Rectangle(ClientSize.Width - drawerWidth, 0, drawerWidth, ClientSize.Height);
        settingsDrawer.Dock = DockStyle.None;
        settingsDrawer.Bounds = new Rectangle(
            advancedHost.Padding.Left,
            advancedHost.Padding.Top,
            Math.Max(0, advancedHost.ClientSize.Width - advancedHost.Padding.Horizontal),
            Math.Max(0, advancedHost.ClientSize.Height - advancedHost.Padding.Vertical));
        settingsDrawer.ConstrainLayout();
        PositionFloatingControls();
    }

    private void PositionFloatingControls()
    {
        targetName.Bounds = new Rectangle(
            Math.Max(Padding.Left, ClientSize.Width - Padding.Right - ScaleLogical(156)),
            Padding.Top + ScaleLogical(18),
            ScaleLogical(156),
            ScaleLogical(27));
        targetName.BringToFront();
    }

    private int ScaleLogical(int value) => (int)Math.Round(value * DeviceDpi / 96F);

    private void UpdateSignal(double confidence, int sampleCount, RoutineState routineState)
    {
        var percent = (int)Math.Round(Math.Clamp(confidence, 0, 1) * 100);
        confidenceValue.Text = confidence > 0 ? percent.ToString() : "—";
        confidenceUnit.Visible = confidence > 0;
        samples.Text = sampleCount > 0
            ? $"{sampleCount:N0} SAMPLES  ·  {(confidence >= 0.6 ? "STABLE LOCK" : "ACQUIRING")}"
            : routineState == RoutineState.Faulted
                ? "SIGNAL LOST  ·  CHECK TARGET"
                : !targetConfigured ? "WAITING FOR TARGET" : "PREFLIGHT STARTS WITH AUTOMATION";
        samples.ForeColor = routineState switch
        {
            RoutineState.Faulted => AppTheme.Coral,
            RoutineState.Stopped when !targetConfigured => AppTheme.Amber,
            _ => AppTheme.Muted,
        };
        var activeSegments = confidence > 0 ? (int)Math.Round(confidence * signalSegments.Length) : 0;
        for (var index = 0; index < signalSegments.Length; index++)
            signalSegments[index].BackColor = index < activeSegments ? AppTheme.Mint : AppTheme.Border;
    }

    private void UpdateCycle(RoutineState routineState)
    {
        var activeIndex = routineState switch
        {
            RoutineState.Stopped or RoutineState.Faulted => -1,
            RoutineState.Casting => 0,
            RoutineState.Armed => 1,
            RoutineState.Regulating => 2,
            RoutineState.Collecting => 3,
            RoutineState.Stowing => 4,
            _ => 5,
        };
        for (var index = 0; index < cycleSteps.Count; index++) cycleSteps[index].SetActive(index == activeIndex);
        var phase = activeIndex switch
        {
            0 => "CAST",
            1 => "ACQUIRE",
            2 => "CONTROL",
            3 => "COLLECT",
            4 => "RESET",
            _ => "REPEAT",
        };
        loopStatus.Text = routineState switch
        {
            RoutineState.Stopped when !targetConfigured => "WAITING  ·  SET TARGET FIRST",
            RoutineState.Stopped => "STANDBY  ·  START TO VERIFY",
            RoutineState.Faulted => "ATTENTION  ·  CHECK STATUS",
            _ => $"STEP {activeIndex + 1:00} OF 06  ·  {phase}",
        };
        loopStatus.ForeColor = routineState switch
        {
            RoutineState.Faulted => AppTheme.Coral,
            RoutineState.Stopped when !targetConfigured => AppTheme.Amber,
            RoutineState.Stopped => AppTheme.Muted,
            _ => AppTheme.Mint,
        };
    }

    private void UpdateTelemetry()
    {
        cycleMetric.SetValue(cycleCount > 0 ? cycleCount.ToString("0000") : "—");
        if (runStartedAt is null)
        {
            runtimeMetric.SetValue("—", AppTheme.Muted);
        }
        else
        {
            var elapsed = DateTimeOffset.Now - runStartedAt.Value;
            runtimeMetric.SetValue(elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
                : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}", active ? AppTheme.Mint : AppTheme.Coral);
        }
        if (lastCatchAt is null)
        {
            lastCatchMetric.SetValue("—", AppTheme.Muted);
        }
        else
        {
            var sinceCatch = DateTimeOffset.Now - lastCatchAt.Value;
            lastCatchMetric.SetValue(sinceCatch.TotalHours >= 1
                ? $"{(int)sinceCatch.TotalHours:00}:{sinceCatch.Minutes:00}:{sinceCatch.Seconds:00} AGO"
                : $"{sinceCatch.Minutes:00}:{sinceCatch.Seconds:00} AGO", AppTheme.Amber);
        }
    }

    private static void SetHealth(Label label, string text, Color color)
    {
        label.Text = $"●  {text}";
        label.ForeColor = color;
    }

    private static void AddTelemetry(TableLayoutPanel row, int column, TelemetryReadout metric)
    {
        metric.Margin = new Padding(0, 0, column == 3 ? 0 : 1, 0);
        row.Controls.Add(metric, column, 0);
    }

    private static Label Caption(string text, Color color) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 7.5F, FontStyle.Bold),
        ForeColor = color,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private sealed class CycleStep
    {
        internal readonly Panel Root = new();
        private readonly Label number = new();
        private readonly Label title = new();
        private readonly Panel activeRule = new() { Height = 3, BackColor = AppTheme.Mint, Visible = false };
        internal bool IsActiveForTest { get; private set; }

        internal CycleStep(string numberText, string titleText, string detailText)
        {
            Root.Dock = DockStyle.Fill;
            Root.BackColor = AppTheme.Surface;
            Root.Padding = new Padding(10, 7, 8, 5);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Color.Transparent };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            number.Text = numberText;
            number.Dock = DockStyle.Fill;
            number.Font = new Font("Consolas", 7.5F, FontStyle.Bold);
            number.ForeColor = AppTheme.Muted;
            title.Text = titleText;
            title.Dock = DockStyle.Fill;
            title.Font = new Font("Segoe UI Semibold", 9F);
            title.ForeColor = AppTheme.Text;
            var detail = new Label
            {
                Text = detailText,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 7.2F),
                ForeColor = AppTheme.Muted,
            };
            layout.Controls.Add(number, 0, 0);
            layout.Controls.Add(title, 0, 1);
            layout.Controls.Add(detail, 0, 2);
            Root.Controls.Add(layout);
            activeRule.Location = Point.Empty;
            activeRule.Width = Root.ClientSize.Width;
            activeRule.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Root.Controls.Add(activeRule);
            activeRule.BringToFront();
            Root.Resize += (_, _) => activeRule.Width = Root.ClientSize.Width;
        }

        internal void SetActive(bool isActive)
        {
            IsActiveForTest = isActive;
            Root.BackColor = isActive ? AppTheme.MintWash : AppTheme.Surface;
            activeRule.Visible = isActive;
            number.ForeColor = isActive ? AppTheme.Mint : AppTheme.Muted;
            title.ForeColor = isActive ? AppTheme.Mint : AppTheme.Text;
        }
    }
}
