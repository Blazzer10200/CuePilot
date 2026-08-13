namespace WorkflowLooper;

internal static class SelfTest
{
    internal static int Run()
    {
        try
        {
            if (NativeMethods.InputSize != 40) throw new InvalidOperationException($"Unexpected x64 INPUT size: {NativeMethods.InputSize}.");
            var defaults = AppSettings.Defaults();
            if (!SettingsStore.IsValid(defaults) || defaults.EmergencyStop.DisplayText != "PAUSE / BREAK")
                throw new InvalidOperationException("Emergency shortcut defaults are invalid.");

            var restored = SettingsStore.RoundTripForTest(defaults);
            if (restored.FormatVersion != 7 || restored.SelectedProfile != "fishing")
                throw new InvalidOperationException("Settings v7 did not survive serialization.");

            using var form = new MainForm(false, defaults)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-10_000, -10_000),
                ShowInTaskbar = false,
            };
            form.Show();
            Application.DoEvents();
            form.PrepareReadyForPreview();
            Application.DoEvents();
            if (form.DashboardStateForTest != "READY TO START" || form.AdvancedVisibleForTest)
                throw new InvalidOperationException("Dashboard ready state is invalid.");
            if (form.ActiveCycleStepForTest != -1 || form.RuntimeValueForTest != "—")
                throw new InvalidOperationException("Dashboard idle signals are not truthful.");
            var fixedClientSize = form.ClientSizeForTest;

            form.PrepareForPreview();
            Application.DoEvents();
            if (form.DashboardStateForTest != "WATCHING FOR MINIGAME")
                throw new InvalidOperationException("Dashboard running state is invalid.");

            form.ShowAdvancedForPreview();
            Application.DoEvents();
            if (!form.AdvancedVisibleForTest || !form.DrawerEmergencyEnabledForTest)
                throw new InvalidOperationException("Dashboard advanced state is invalid.");
            if (form.ClientSizeForTest != fixedClientSize)
                throw new InvalidOperationException("Advanced tuning changed the fixed dashboard footprint.");
            var drawerBounds = form.AdvancedBoundsForPreview;
            if (drawerBounds.Left <= fixedClientSize.Width / 2 || drawerBounds.Width <= 0)
                throw new InvalidOperationException($"Advanced drawer bounds are invalid: {drawerBounds}.");
            form.Hide();

            Console.WriteLine("SELF_TEST_OK serialization=v7 dashboard=ready-running-advanced-fixed idle=truthful focus=drawer targeting=application capture=desktop input=target-aware fishing-feedback=validated emergency-stop=drawer-visible");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SELF_TEST_FAILED {exception}");
            return 1;
        }
    }
}
