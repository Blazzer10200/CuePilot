using System.Drawing;
using System.Text.Json;

namespace CuePilot;

internal static class SelfTest
{
    internal static int Run()
    {
        try
        {
            if (NativeMethods.InputSize != 40)
                throw new InvalidOperationException($"Unexpected x64 INPUT size: {NativeMethods.InputSize}.");

            var defaults = AppSettings.Defaults();
            if (!SettingsStore.IsValid(defaults)
                || defaults.StartStop.DisplayText != "F10"
                || defaults.LockpickingStartStop.DisplayText != "F9"
                || defaults.EmergencyStop.DisplayText != "PAUSE / BREAK")
                throw new InvalidOperationException("Global shortcut defaults are invalid.");

            var restored = SettingsStore.RoundTripForTest(defaults);
            if (restored.FormatVersion != 9 || restored.SelectedProfile != "fishing")
                throw new InvalidOperationException("Settings v8 did not survive serialization.");

            var keyDown = InputSender.CreateScanCodeInput(InputKey.E, false);
            var keyUp = InputSender.CreateScanCodeInput(InputKey.E, true);
            if (keyDown.Data.Keyboard.ScanCode == 0
                || (keyUp.Data.Keyboard.Flags & NativeMethods.KeyeventfKeyup) == 0)
                throw new InvalidOperationException("Backend-neutral E key input is invalid.");

            defaults.Routine.TargetWindow = new WindowTargetSettings
            {
                ProcessId = 3258,
                ProcessName = "FiveM_b3258_GTAProcess",
                WindowTitle = "FiveM",
            };
            using var input = new StringReader("{\"id\":\"self-test\",\"command\":\"snapshot\"}");
            using var output = new StringWriter();
            UiBridge.Run(
                input,
                output,
                () => defaults,
                _ => throw new InvalidOperationException("Snapshot self-test attempted to save settings."),
                () =>
                [
                    new WindowTargetService.FiveMWindowTarget(
                        3258,
                        "FiveM_b3258_GTAProcess",
                        "FiveM",
                        new Rectangle(0, 0, 1280, 720),
                        false,
                        false),
                ]);

            var response = output.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonDocument.Parse(line).RootElement.Clone())
                .Single(message => message.GetProperty("type").GetString() == "response");
            var result = response.GetProperty("result");
            if (!response.GetProperty("ok").GetBoolean()
                || result.GetProperty("protocolVersion").GetInt32() != UiBridge.ProtocolVersion
                || !result.GetProperty("targetValid").GetBoolean())
                throw new InvalidOperationException("The local UI bridge snapshot contract is invalid.");

            Console.WriteLine("SELF_TEST_OK serialization=v9 bridge=protocol-1 target=fivem input=backend-neutral global-shortcuts=tauri-owned fishing-feedback=validated");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"SELF_TEST_FAILED {exception}");
            return 1;
        }
    }
}
