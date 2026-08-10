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

            foreach (var preset in PresetFactory.BuiltIn)
            {
                var presetPattern = PresetFactory.Create(preset);
                if (presetPattern.Events.Count != 2
                    || presetPattern.Events[0].Type != MacroEventType.MouseDown
                    || presetPattern.Events[1].Type != MacroEventType.MouseUp
                    || presetPattern.Events[1].OffsetMicroseconds >= presetPattern.DurationMicroseconds)
                {
                    throw new InvalidOperationException($"Preset '{preset.Name}' produced an invalid click cycle.");
                }
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

            Console.WriteLine($"SELF_TEST_OK serialization=3-events scaling=center input-size=40 presets={PresetFactory.BuiltIn.Count} hotkeys=validated custom-controls=validated hooks=installed precision-timer=ok");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SELF_TEST_FAILED {exception}");
            return 1;
        }
    }
}
