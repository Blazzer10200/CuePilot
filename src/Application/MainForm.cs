using System.Runtime.InteropServices;

namespace WorkflowLooper;

internal sealed class MainForm : Form
{
    private static readonly Size DesignClientSize = new(1024, 570);
    private static readonly Size DesignMinimumSize = DesignClientSize;
    private const int EmergencyHotkeyId = 7103;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int ResizeGrip = 8;

    private readonly AdaptiveRoutineEngine routine = new();
    private readonly OperatorConsoleDashboard dashboard = new();
    private readonly AppSettings settings;
    private readonly bool registerGlobalHotkey;
    private readonly Action<IntPtr> beginWindowDrag;
    private readonly Label footerState = new();
    private readonly Label footerDetail = new();
    private readonly WindowChromeButton minimizeWindow = new(WindowChromeKind.Minimize);
    private readonly WindowChromeButton maximizeWindow = new(WindowChromeKind.Maximize);
    private readonly WindowChromeButton closeWindow = new(WindowChromeKind.Close);
    private int shellDpi;
    private int titleBarDragSurfaceCount;

    internal MainForm(bool registerGlobalHotkeys = true, AppSettings? initialSettings = null, Action<IntPtr>? windowDragAction = null)
    {
        registerGlobalHotkey = registerGlobalHotkeys;
        beginWindowDrag = windowDragAction ?? BeginNativeWindowDrag;
        settings = (initialSettings ?? SettingsStore.Load()).Copy();
        ConfigureWindow();
        BuildShell();
        ConfigureScaling();
        WireEvents();
        dashboard.LoadSettings(settings.Routine);
        RefreshTarget();
    }

    private void ConfigureWindow()
    {
        Text = WindowTitle;
        BackColor = AppTheme.Canvas;
        ForeColor = AppTheme.Text;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = DesignMinimumSize;
        ClientSize = DesignClientSize;
        MaximizeBox = true;
        KeyPreview = true;
        using var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (icon is not null) Icon = (Icon)icon.Clone();
    }

    private void ConfigureScaling()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private static Size ScaleSize(Size size, float scale) => new(
        (int)Math.Round(size.Width * scale),
        (int)Math.Round(size.Height * scale));

    private void BuildShell()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = AppTheme.Canvas };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildTitleBar(), 0, 0);
        dashboard.AttachFooter(BuildFooter());
        root.Controls.Add(dashboard, 0, 1);
        Controls.Add(root);
    }

    private Control BuildTitleBar()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, BackColor = AppTheme.Surface, Padding = new Padding(12, 0, 8, 0) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        var brand = new Label
        {
            Text = BrandText, Dock = DockStyle.Fill, ForeColor = AppTheme.Text,
            Font = new Font("Segoe UI Semibold", 8F), TextAlign = ContentAlignment.MiddleLeft,
        };
        minimizeWindow.Click += (_, _) => WindowState = FormWindowState.Minimized;
        maximizeWindow.Click += (_, _) => ToggleMaximize();
        closeWindow.Click += (_, _) => Close();
        minimizeWindow.Margin = maximizeWindow.Margin = closeWindow.Margin = new Padding(2, 6, 2, 6);
        Resize += (_, _) => maximizeWindow.IsMaximized = WindowState == FormWindowState.Maximized;
        bar.DoubleClick += (_, _) => ToggleMaximize();
        brand.DoubleClick += (_, _) => ToggleMaximize();
        RegisterTitleBarDragSurface(bar);
        RegisterTitleBarDragSurface(brand);
        bar.Controls.Add(brand, 0, 0);
        bar.Controls.Add(minimizeWindow, 1, 0);
        bar.Controls.Add(maximizeWindow, 2, 0);
        bar.Controls.Add(closeWindow, 3, 0);
        return bar;
    }

    private void RegisterTitleBarDragSurface(Control control)
    {
        control.MouseDown += HandleTitleBarMouseDown;
        titleBarDragSurfaceCount++;
    }

    private void HandleTitleBarMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && e.Clicks == 1)
            beginWindowDrag(Handle);
    }

    private static void BeginNativeWindowDrag(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(handle, WmNcLeftButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = AppTheme.Canvas, Padding = new Padding(26, 0, 26, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        footerState.Text = "READY"; footerState.Dock = DockStyle.Fill; footerState.Margin = Padding.Empty; footerState.ForeColor = AppTheme.Mint; footerState.Font = new Font("Consolas", 6.5F); footerState.TextAlign = ContentAlignment.MiddleLeft;
        footerDetail.Text = "Set FiveM as the target, then start automation."; footerDetail.Dock = DockStyle.Fill; footerDetail.Margin = Padding.Empty; footerDetail.ForeColor = AppTheme.Muted; footerDetail.Font = new Font("Consolas", 6.5F); footerDetail.TextAlign = ContentAlignment.MiddleLeft; footerDetail.AutoEllipsis = true;
        var emergency = new Label { Text = "PAUSE / BREAK  ·  EMERGENCY STOP", Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = AppTheme.Amber, Font = new Font("Consolas", 6.5F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
        footer.Controls.Add(footerState, 0, 0); footer.Controls.Add(footerDetail, 1, 0); footer.Controls.Add(emergency, 2, 0);
        return footer;
    }

    private void WireEvents()
    {
        dashboard.StartRequested += (_, _) => StartAutomation();
        dashboard.StopRequested += (_, _) => EmergencyStop("Stopped from the dashboard.");
        dashboard.TargetRequested += async (_, _) => await CaptureTargetAsync();
        dashboard.SettingsApplyRequested += (_, _) => ApplyDashboardSettings();
        routine.StatusChanged += (_, status) => SafeUi(() => UpdateStatus(status));
        FormClosed += (_, _) => routine.Dispose();
    }

    private void ApplyDashboardSettings()
    {
        dashboard.SaveSettings(settings.Routine);
        SettingsStore.Save(settings);
        dashboard.LoadSettings(settings.Routine);
        UpdateFooter("SETTINGS SAVED", "Fishing tuning will be used on the next detector update.", AppTheme.Mint);
    }

    private void StartAutomation()
    {
        try
        {
            dashboard.SaveSettings(settings.Routine);
            SettingsStore.Save(settings);
            routine.Arm(settings.Routine);
            UpdateFooter("PREFLIGHT", "Resolving target, validating capture, and preparing input.", AppTheme.Amber);
        }
        catch (Exception exception)
        {
            UpdateStatus(new RoutineStatus(RoutineState.Faulted, exception.Message));
        }
    }

    private async Task CaptureTargetAsync()
    {
        try
        {
            UpdateFooter("SELECT TARGET", "Switch to FiveM. Capturing the foreground application in 1.5 seconds.", AppTheme.Amber);
            WindowState = FormWindowState.Minimized;
            await Task.Delay(1_500);
            settings.Routine.TargetWindow = WindowTargetService.CaptureForeground();
            SettingsStore.Save(settings);
            WindowState = FormWindowState.Normal;
            Activate();
            RefreshTarget();
            UpdateFooter("TARGET READY", $"Locked to {settings.Routine.TargetWindow.ProcessName}.", AppTheme.Mint);
        }
        catch (Exception exception)
        {
            WindowState = FormWindowState.Normal;
            UpdateStatus(new RoutineStatus(RoutineState.Faulted, $"Target selection failed: {exception.Message}"));
        }
    }

    private void RefreshTarget()
    {
        var detail = settings.Routine.TargetWindow.IsConfigured
            ? $"Target saved as {settings.Routine.TargetWindow.ProcessName}. Capture and input will be checked when automation starts."
            : "Select the FiveM window before starting automation.";
        dashboard.SetTarget(settings.Routine.TargetWindow, detail);
    }

    private void UpdateStatus(RoutineStatus status)
    {
        dashboard.SetStatus(status);
        var color = status.State switch
        {
            RoutineState.Faulted => AppTheme.Coral,
            RoutineState.Casting or RoutineState.Stowing => AppTheme.Amber,
            _ => AppTheme.Mint,
        };
        UpdateFooter(status.State.ToString().ToUpperInvariant(), status.Detail, color);
    }

    private void UpdateFooter(string state, string detail, Color color)
    {
        footerState.Text = state;
        footerState.ForeColor = color;
        footerDetail.Text = detail;
    }

    private void EmergencyStop(string reason)
    {
        routine.Stop(reason);
        InputSender.ReleaseAll();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyShellDpi();
        if (registerGlobalHotkey)
        {
            NativeMethods.RegisterHotKey(Handle, EmergencyHotkeyId, settings.EmergencyStop.NativeModifiers, (uint)settings.EmergencyStop.Key);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        dashboard.FocusPrimaryAction();
    }

    private void ApplyShellDpi()
    {
        if (DeviceDpi == shellDpi) return;
        shellDpi = DeviceDpi;
        var scale = shellDpi / 96F;
        MinimumSize = ScaleSize(DesignMinimumSize, scale);
        ClientSize = ScaleSize(DesignClientSize, scale);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (registerGlobalHotkey) NativeMethods.UnregisterHotKey(Handle, EmergencyHotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey && message.WParam.ToInt32() == EmergencyHotkeyId)
        {
            EmergencyStop("Emergency stop pressed.");
            return;
        }

        if (message.Msg == WmNcHitTest)
        {
            base.WndProc(ref message);
            if ((int)message.Result != 1 || WindowState == FormWindowState.Maximized) return;

            var point = PointToClient(Cursor.Position);
            var left = point.X <= ResizeGrip;
            var right = point.X >= ClientSize.Width - ResizeGrip;
            var top = point.Y <= ResizeGrip;
            var bottom = point.Y >= ClientSize.Height - ResizeGrip;
            message.Result = (left, right, top, bottom) switch
            {
                (true, _, true, _) => (IntPtr)HtTopLeft,
                (_, true, true, _) => (IntPtr)HtTopRight,
                (true, _, _, true) => (IntPtr)HtBottomLeft,
                (_, true, _, true) => (IntPtr)HtBottomRight,
                (true, _, _, _) => (IntPtr)HtLeft,
                (_, true, _, _) => (IntPtr)HtRight,
                (_, _, true, _) => (IntPtr)HtTop,
                (_, _, _, true) => (IntPtr)HtBottom,
                _ when point.Y < 38 => (IntPtr)HtCaption,
                _ => message.Result,
            };
            return;
        }

        base.WndProc(ref message);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape && dashboard.AdvancedVisibleForTest)
        {
            dashboard.HideAdvancedForTest();
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    private static string VersionText
    {
        get
        {
            var version = typeof(MainForm).Assembly.GetName().Version;
            return version is null ? "05.01" : $"{version.Major:00}.{Math.Max(version.Build, 1):00}";
        }
    }
#if DEBUG
    private static string WindowTitle => "Workflow Looper — DEV";
    private static string BrandText => $"WORKFLOW LOOPER   {VersionText}   / DEV";
#else
    private static string WindowTitle => "Workflow Looper";
    private static string BrandText => $"WORKFLOW LOOPER   {VersionText}";
#endif

    internal void PrepareForPreview()
    {
        dashboard.SetTarget(new WindowTargetSettings { ProcessName = "FiveM_b3258_GTAProcess" }, "Application target ready. Capture and input will be verified on start.");
        dashboard.SetStatus(new RoutineStatus(RoutineState.Armed, "Cast sent. Scanning FiveM for the fishing meter; control begins only after a stable lock.", 42, 0.82));
        UpdateFooter("RUN 0001  /", "INPUT RELEASES AUTOMATICALLY ON STOP", AppTheme.Muted);
    }

    internal void PrepareReadyForPreview()
    {
        dashboard.SetTarget(new WindowTargetSettings { ProcessName = "FiveM_b3258_GTAProcess" }, "Target locked. Start Automation verifies capture and input before casting.");
        dashboard.SetStatus(new RoutineStatus(RoutineState.Stopped, "Target locked. Start Automation verifies capture and input, then casts only when FiveM is ready."));
        UpdateFooter("READY", "Target configured. Start Automation will run preflight checks.", AppTheme.Mint);
    }

    internal void PrepareEmptyForPreview()
    {
        dashboard.SetTarget(null, "No target configured. Set the FiveM window before starting automation.");
        dashboard.SetStatus(new RoutineStatus(RoutineState.Stopped, "Choose Set Target, switch to FiveM, and return here when the lock is confirmed."));
        UpdateFooter("TARGET REQUIRED", "Set the FiveM window before starting automation.", AppTheme.Amber);
    }

    internal void PrepareFaultForPreview()
    {
        dashboard.SetTarget(new WindowTargetSettings { ProcessName = "FiveM_b3258_GTAProcess" }, "Application target ready.");
        dashboard.SetStatus(new RoutineStatus(RoutineState.Armed, "Watching for the fishing meter.", 24, 0.72));
        dashboard.SetStatus(new RoutineStatus(RoutineState.Faulted, "Capture verification was lost. Restore FiveM, then start automation again."));
        UpdateFooter("FAULT", "Capture verification was lost. Restore FiveM, then retry.", AppTheme.Coral);
    }

    internal void ShowAdvancedForPreview() => dashboard.ShowAdvancedForTest();
    internal void HideAdvancedForPreview() => dashboard.HideAdvancedForPreview();
    internal void PrimeForRenderPreview() => dashboard.PrimeForRenderTest();
    internal Rectangle AdvancedBoundsForPreview
    {
        get
        {
            var origin = PointToClient(dashboard.PointToScreen(dashboard.AdvancedBoundsForTest.Location));
            return new Rectangle(origin, dashboard.AdvancedBoundsForTest.Size);
        }
    }
    internal string DashboardStateForTest => dashboard.StateTextForTest;
    internal bool AdvancedVisibleForTest => dashboard.AdvancedVisibleForTest;
    internal bool DrawerEmergencyEnabledForTest => dashboard.DrawerEmergencyEnabledForTest;
    internal Size ClientSizeForTest => ClientSize;
    internal int TitleBarDragSurfaceCountForTest => titleBarDragSurfaceCount;
    internal int ActiveCycleStepForTest => dashboard.ActiveCycleStepForTest;
    internal string RuntimeValueForTest => dashboard.RuntimeValueForTest;
    internal bool TargetEnabledForTest => dashboard.TargetEnabledForTest;
    internal string RunModeForTest => dashboard.RunModeForTest;
    internal Color RuntimeColorForTest => dashboard.RuntimeColorForTest;
    internal bool AdvancedPrimaryFieldFocusedForTest => dashboard.AdvancedPrimaryFieldFocusedForTest;
    internal bool RunActionEnabledForTest => dashboard.RunActionEnabledForTest;
    internal string DetectorStatusForTest => dashboard.DetectorStatusForTest;
    internal string RunActionTextForTest => dashboard.RunActionTextForTest;
    internal bool MainActionsInTabOrderForTest => dashboard.MainActionsInTabOrderForTest;
    internal Control AdvancedControlForPreview => dashboard.AdvancedControlForTest;
    internal void ApplyStatusForTest(RoutineStatus status) => UpdateStatus(status);
    internal void RequestTitleBarDragForTest(MouseButtons button, int clicks = 1) =>
        HandleTitleBarMouseDown(this, new MouseEventArgs(button, clicks, 0, 0, 0));
}
