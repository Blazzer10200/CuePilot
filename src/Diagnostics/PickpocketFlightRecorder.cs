using System.Text.Json;

namespace CuePilot;

/// <summary>
/// Per-press / per-run-end evidence for Pickpocket failures (shortcut log + run incidents).
/// Nothing here runs on the capture loop.
/// </summary>
internal static class PickpocketFlightRecorder
{
    internal const long RotateBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly object Gate = new();

    internal static string DefaultDirectory => Path.Combine(AppPaths.DiagnosticsDirectory, "pickpocket");
    internal static string DefaultShortcutLogPath => Path.Combine(DefaultDirectory, "shortcuts.jsonl");

    /// <summary>Appends one JSON line; at <paramref name="rotateBytes"/> the file moves to ".1" (one backup).</summary>
    internal static void Append(string path, object record, long rotateBytes = RotateBytes)
    {
        try
        {
            var line = JsonSerializer.Serialize(record, Json);
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length >= rotateBytes) File.Move(path, path + ".1", true);
                File.AppendAllText(path, line + "\n");
            }
        }
        // Deliberate: this log is best-effort evidence and must never break the command or the run.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }

    internal static string Categorize(int activeSamples, bool everArmed, int manualPresses) =>
        activeSamples == 0 ? "no-panel-seen"
        : manualPresses > 0 ? "manual-space"
        : !everArmed ? "panel-seen-never-armed"
        : "armed-no-shot";

    internal static void WriteIncident(string sessionDirectory, PickpocketObserveStatus latest, int activeSamples, int hiddenSamples,
        bool everArmed, string lastActiveReason)
    {
        var category = Categorize(activeSamples, everArmed, latest.ManualSpacePressCount);
        var incident = new
        {
            endedUtc = DateTime.UtcNow, endReason = latest.Detail, category,
            lastPredictionReason = lastActiveReason, activeSamples, hiddenSamples, totalSamples = latest.SampleCount,
            armedSeen = everArmed, manualSpacePresses = latest.ManualSpacePressCount,
            captureBackend = latest.CaptureBackend, inputMode = latest.InputMode,
        };
        try
        {
            File.WriteAllText(Path.Combine(sessionDirectory, "incident.json"), JsonSerializer.Serialize(incident, new JsonSerializerOptions(Json) { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var parent = Path.GetDirectoryName(Path.GetFullPath(sessionDirectory));
        if (parent is not null)
            Append(Path.Combine(parent, "incidents.jsonl"), new { incident.endedUtc, session = Path.GetFileName(sessionDirectory), incident.category, incident.endReason, incident.activeSamples, incident.hiddenSamples, incident.captureBackend });
    }

    /// <summary>"category: reason" from incident.json, or null when absent or unreadable.</summary>
    internal static string? ReadIncidentSummary(string sessionDirectory)
    {
        try
        {
            var path = Path.Combine(sessionDirectory, "incident.json");
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var category = root.TryGetProperty("category", out var c) ? c.GetString() : null;
            var reason = root.TryGetProperty("endReason", out var r) ? r.GetString() : null;
            var last = root.TryGetProperty("lastPredictionReason", out var l) ? l.GetString() : null;
            if (category is null) return null;
            return string.IsNullOrEmpty(last) ? $"{category}: {reason}" : $"{category}: {reason} (last prediction: {last})";
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
}
