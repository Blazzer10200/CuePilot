using System.Text.Json;

namespace WorkflowLooper;

internal static class SelfTest
{
    internal static int Run()
    {
        try
        {
            var pattern = new WorkflowPattern
            {
                Name = "Self test",
                DurationMicroseconds = 650_000,
                RecordedLeft = 0,
                RecordedTop = 0,
                RecordedWidth = 1920,
                RecordedHeight = 1080,
                Events =
                [
                    new MacroEvent { OffsetMicroseconds = 100_000, Type = MacroEventType.MouseMove, X = 960, Y = 540 },
                    new MacroEvent { OffsetMicroseconds = 200_000, Type = MacroEventType.KeyDown, VirtualKey = 65, ScanCode = 30 },
                    new MacroEvent { OffsetMicroseconds = 250_000, Type = MacroEventType.KeyUp, VirtualKey = 65, ScanCode = 30 },
                ],
            };

            var json = JsonSerializer.Serialize(pattern, WorkflowJson.Options);
            var restored = JsonSerializer.Deserialize<WorkflowPattern>(json, WorkflowJson.Options)
                ?? throw new InvalidOperationException("JSON round trip returned null.");
            if (restored.Events.Count != 3 || restored.Events[1].Type != MacroEventType.KeyDown)
            {
                throw new InvalidOperationException("JSON round trip changed workflow events.");
            }

            var scaled = PlaybackEngine.ScalePoint(pattern, new Rectangle(-1920, 0, 3840, 2160), 960, 540);
            if (scaled.X is < -2 or > 2 || scaled.Y is < 1078 or > 1082)
            {
                throw new InvalidOperationException($"Screen scaling was incorrect: {scaled}.");
            }

            if (NativeMethods.InputSize != 40)
            {
                throw new InvalidOperationException($"Unexpected x64 INPUT size: {NativeMethods.InputSize}.");
            }

            var rhythm = new WorkflowPattern
            {
                Name = "Rhythm",
                DurationMicroseconds = 600_000,
                Events =
                [
                    new MacroEvent { OffsetMicroseconds = 10_000, Type = MacroEventType.MouseDown, Data = 1 },
                    new MacroEvent { OffsetMicroseconds = 70_000, Type = MacroEventType.MouseUp, Data = 1 },
                    new MacroEvent { OffsetMicroseconds = 330_000, Type = MacroEventType.MouseDown, Data = 1 },
                    new MacroEvent { OffsetMicroseconds = 410_000, Type = MacroEventType.MouseUp, Data = 1 },
                ],
            };
            var normalized = PatternTiming.NormalizeLeftClicks(rhythm, 150, 25);
            var analysis = PatternTiming.AnalyzeLeftClicks(rhythm);
            if (normalized != 2 || analysis.MedianIntervalMilliseconds != 150 || analysis.MedianHoldMilliseconds != 25)
            {
                throw new InvalidOperationException("Click normalization changed the requested rhythm.");
            }

            var defaults = AppSettings.Defaults();
            if (!SettingsStore.IsValid(defaults)
                || defaults.Record.DisplayText != "CTRL + SHIFT + F6"
                || defaults.Playback.DisplayText != "CTRL + SHIFT + F7"
                || defaults.EmergencyStop.DisplayText != "PAUSE / BREAK")
            {
                throw new InvalidOperationException("Default shortcut settings were invalid.");
            }

            var duplicateSettings = defaults.Copy();
            duplicateSettings.Playback = duplicateSettings.Record.Copy();
            if (SettingsStore.IsValid(duplicateSettings))
            {
                throw new InvalidOperationException("Duplicate shortcuts were not rejected.");
            }

            var restoredSettings = SettingsStore.RoundTripForTest(defaults);
            if (!restoredSettings.Record.Matches(defaults.Record)
                || !restoredSettings.Playback.Matches(defaults.Playback)
                || !restoredSettings.EmergencyStop.Matches(defaults.EmergencyStop))
            {
                throw new InvalidOperationException("Shortcut settings did not survive JSON serialization.");
            }

            using (var stepper = new StepperControl { Minimum = 0, Maximum = 10 })
            {
                stepper.Value = 99;
                if (stepper.Value != 10)
                {
                    throw new InvalidOperationException("Custom stepper did not enforce its maximum.");
                }

                stepper.Value = -1;
                if (stepper.Value != 0)
                {
                    throw new InvalidOperationException("Custom stepper did not enforce its minimum.");
                }
            }

            using (var waiter = new PrecisionWaiter())
            {
                var timing = System.Diagnostics.Stopwatch.StartNew();
                var lateness = new List<double>();
                for (var index = 1; index <= 100; index++)
                {
                    var target = index * 5_000d;
                    PlaybackEngine.WaitUntil(timing, target, waiter, CancellationToken.None);
                    var actual = timing.ElapsedTicks * 1_000_000d / System.Diagnostics.Stopwatch.Frequency;
                    lateness.Add(Math.Max(0, actual - target));
                }

                var meanLateness = lateness.Average() / 1_000d;
                var maximumLateness = lateness.Max() / 1_000d;
                if (meanLateness > 5 || maximumLateness > 50)
                {
                    throw new InvalidOperationException($"Precision timing was unstable: mean {meanLateness:F3} ms, max {maximumLateness:F3} ms.");
                }

                Console.WriteLine($"TIMING mean_ms={meanLateness:F3} max_ms={maximumLateness:F3} high_resolution={waiter.IsHighResolution}");
            }

            using (var recorder = new GlobalRecorder())
            {
                recorder.Start(false);
                Thread.Sleep(50);
                var emptyCapture = recorder.Stop("Hook test", null);
                if (emptyCapture.Events.Count != 0)
                {
                    throw new InvalidOperationException("Hook test captured unexpected input.");
                }
            }

            using (var form = new MainForm(false))
            {
                if (form.ShortcutPreviewForTest != "RECORD / STOP  ·  CTRL + SHIFT + F6|PLAY / STOP  ·  CTRL + SHIFT + F7|EMERGENCY STOP  ·  PAUSE / BREAK")
                {
                    throw new InvalidOperationException($"Shortcut labels were not populated: {form.ShortcutPreviewForTest}");
                }
            }

            Console.WriteLine("SELF_TEST_OK serialization=v2 scaling=center input-size=40 rhythm=normalized hotkeys=validated ui-labels=populated custom-controls=validated hooks=installed precision-timer=ok");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SELF_TEST_FAILED {exception}");
            return 1;
        }
    }
}
