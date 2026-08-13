using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkflowLooper;

// Local stdin/stdout bridge for the Tauri shell. It intentionally exposes a
// tiny, allowlisted command surface rather than a listener on a network port.
internal static class UiBridge
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly object OutputLock = new();

    internal static int Run()
    {
        var settings = SettingsStore.Load();
        using var routine = new AdaptiveRoutineEngine();
        routine.StatusChanged += (_, status) => Emit("status", new
        {
            state = status.State,
            detail = status.Detail,
            sampleCount = status.SampleCount,
            confidence = status.Confidence,
        });

        Emit("ready", Snapshot(settings, routine.State));
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            try
            {
                using var request = JsonDocument.Parse(line);
                var root = request.RootElement;
                var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
                var command = root.TryGetProperty("command", out var commandValue) ? commandValue.GetString() : null;
                switch (command)
                {
                    case "snapshot":
                        Respond(id, true, Snapshot(settings, routine.State));
                        break;
                    case "start":
                        routine.Arm(settings.Routine);
                        Respond(id, true, Snapshot(settings, routine.State));
                        break;
                    case "stop":
                        routine.Stop("Stopped from the Tauri dashboard.");
                        Respond(id, true, Snapshot(settings, routine.State));
                        break;
                    case "capture_target":
                        CaptureTarget(root, settings);
                        SettingsStore.Save(settings);
                        Emit("target", Snapshot(settings, routine.State));
                        Respond(id, true, Snapshot(settings, routine.State));
                        break;
                    case "save_settings":
                        if (!root.TryGetProperty("settings", out var settingsValue))
                            throw new InvalidOperationException("The settings payload is required.");
                        settings = SettingsStore.DeserializeAndMigrateForBridge(settingsValue.GetRawText());
                        SettingsStore.Save(settings);
                        Emit("settings", Snapshot(settings, routine.State));
                        Respond(id, true, Snapshot(settings, routine.State));
                        break;
                    case "shutdown":
                        routine.Stop("Tauri shell closed.");
                        Respond(id, true, Snapshot(settings, routine.State));
                        return 0;
                    default:
                        throw new InvalidOperationException($"Unsupported bridge command: {command ?? "(missing)"}.");
                }
            }
            catch (Exception exception)
            {
                Emit("fault", new { detail = exception.Message });
            }
        }

        routine.Stop("Tauri bridge input closed.");
        return 0;
    }

    private static void CaptureTarget(JsonElement root, AppSettings settings)
    {
        var delayMilliseconds = root.TryGetProperty("delayMilliseconds", out var delay)
            ? Math.Clamp(delay.GetInt32(), 0, 10_000)
            : 0;
        if (delayMilliseconds > 0) Thread.Sleep(delayMilliseconds);
        settings.Routine.TargetWindow = WindowTargetService.CaptureForeground();
    }

    private static object Snapshot(AppSettings settings, RoutineState state) => new
    {
        routineState = state,
        settings,
        diagnosticsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkflowLooper", "diagnostics"),
    };

    private static void Respond(string id, bool ok, object result) => Write(new { type = "response", id, ok, result });
    private static void Emit(string name, object payload) => Write(new { type = "event", name, payload });

    private static void Write(object value)
    {
        lock (OutputLock)
        {
            Console.WriteLine(JsonSerializer.Serialize(value, Json));
        }
    }
}
