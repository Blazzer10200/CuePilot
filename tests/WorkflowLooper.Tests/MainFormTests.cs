using System.Runtime.ExceptionServices;

namespace WorkflowLooper.Tests;

public sealed class MainFormTests
{
    [Fact]
    public void ArmedStatusKeepsOperatorWindowAvailable()
    {
        RunInSta(() =>
        {
            using var form = new MainForm(false, AppSettings.Defaults());
            form.WindowState = FormWindowState.Normal;

            form.ApplyStatusForTest(new RoutineStatus(RoutineState.Armed, "Watching for meter."));

            Assert.Equal(FormWindowState.Normal, form.WindowState);
        });
    }

    [Fact]
    public void TitleBarDragUsesOnlyRegisteredLeftClickSurfaces()
    {
        RunInSta(() =>
        {
            var dragRequests = 0;
            using var form = new MainForm(false, AppSettings.Defaults(), _ => dragRequests++);

            Assert.Equal(2, form.TitleBarDragSurfaceCountForTest);

            form.RequestTitleBarDragForTest(MouseButtons.Right);
            form.RequestTitleBarDragForTest(MouseButtons.Left, clicks: 2);
            Assert.Equal(0, dragRequests);

            form.RequestTitleBarDragForTest(MouseButtons.Left);
            Assert.Equal(1, dragRequests);
        });
    }

    [Fact]
    public void DashboardStateUsesHonestIdleAndRunningSignals()
    {
        RunInSta(() =>
        {
            using var form = new MainForm(false, AppSettings.Defaults());

            form.PrepareReadyForPreview();
            Assert.Equal(-1, form.ActiveCycleStepForTest);
            Assert.Equal("—", form.RuntimeValueForTest);
            Assert.True(form.TargetEnabledForTest);
            Assert.True(form.RunActionEnabledForTest);

            form.PrepareForPreview();
            Assert.Equal(1, form.ActiveCycleStepForTest);
            Assert.NotEqual("—", form.RuntimeValueForTest);
            Assert.False(form.TargetEnabledForTest);
        });
    }

    [Fact]
    public void StoppingResetsLiveHealthIndicators()
    {
        RunInSta(() =>
        {
            using var dashboard = new OperatorConsoleDashboard();
            dashboard.SetTarget(new WindowTargetSettings { ProcessName = "FiveM" }, "Target ready");
            dashboard.SetStatus(new RoutineStatus(RoutineState.Armed, "Watching", 10, 0.8));

            Assert.Contains("DESKTOP CAPTURE LIVE", dashboard.CaptureHealthForTest);
            Assert.Contains("INPUT VERIFIED", dashboard.InputHealthForTest);

            dashboard.SetStatus(new RoutineStatus(RoutineState.Stopped, "Stopped"));

            Assert.Contains("CAPTURE CHECK ON START", dashboard.CaptureHealthForTest);
            Assert.Contains("INPUT CHECK ON START", dashboard.InputHealthForTest);
        });
    }

    [Fact]
    public void EmptyAndFaultStatesRemainActionableAndTruthful()
    {
        RunInSta(() =>
        {
            using var form = new MainForm(false, AppSettings.Defaults());

            form.PrepareEmptyForPreview();
            Assert.Equal("TARGET REQUIRED", form.DashboardStateForTest);
            Assert.Equal("SETUP", form.RunModeForTest);
            Assert.Equal(-1, form.ActiveCycleStepForTest);
            Assert.Equal("—", form.RuntimeValueForTest);
            Assert.True(form.TargetEnabledForTest);
            Assert.False(form.RunActionEnabledForTest);
            Assert.Equal("WAITING FOR TARGET", form.DetectorStatusForTest);

            form.PrepareFaultForPreview();
            Assert.Equal("AUTOMATION FAULT", form.DashboardStateForTest);
            Assert.Equal("ALERT", form.RunModeForTest);
            Assert.Equal(-1, form.ActiveCycleStepForTest);
            Assert.NotEqual("—", form.RuntimeValueForTest);
            Assert.Equal(AppTheme.Coral, form.RuntimeColorForTest);
            Assert.True(form.TargetEnabledForTest);
            Assert.True(form.RunActionEnabledForTest);
            Assert.Equal("SIGNAL LOST  ·  CHECK TARGET", form.DetectorStatusForTest);
            Assert.Equal("RETRY AUTOMATION", form.RunActionTextForTest);
        });
    }

    [Fact]
    public void AdvancedDrawerMovesFocusIntoItsFirstField()
    {
        RunInSta(() =>
        {
            using var form = new MainForm(false, AppSettings.Defaults())
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-10_000, -10_000),
                ShowInTaskbar = false,
            };
            form.Show();
            form.ShowAdvancedForPreview();
            Application.DoEvents();

            Assert.True(form.AdvancedPrimaryFieldFocusedForTest);
            Assert.False(form.MainActionsInTabOrderForTest);

            form.HideAdvancedForPreview();
            Assert.True(form.MainActionsInTabOrderForTest);
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
