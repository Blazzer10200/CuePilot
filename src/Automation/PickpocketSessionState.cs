using System.Text.Json;
using System.Text.Json.Serialization;

namespace CuePilot;

internal sealed record PickpocketRecentAttempt(string Id, long EndedAtUnixMs, string Outcome,
    PickpocketBandColor? Color, double? WidthPixels, double? OffsetPixels, int AutomaticPresses,
    string? ItemName = null, int? RedAdvanceMs = null, int? YellowAdvanceMs = null,
    string? EngineVersion = null, string? SessionId = null, string? InputMode = null, string? TargetPolicy = null,
    double? SpeedPixelsPerSecond = null, double? AppliedAdvanceMs = null);

/// <summary>Per-color thin-target early correction derived from saved shot results.</summary>
internal sealed record PickpocketAdvanceCalibration(PickpocketBandColor Color, int BaseMs, double AppliedMs, int Samples, int LegacySamples, double MedianOffsetPixels)
{
    internal string Describe() => Samples < PickpocketSessionState.CalibrationMinimumSamples
        ? $"{Color} {BaseMs} ms as saved ({Samples} of {PickpocketSessionState.CalibrationMinimumSamples} thin-target shots recorded)"
        : $"{Color} {BaseMs} -> {AppliedMs:F1} ms from {Samples} thin-target shots (median stop {MedianOffsetPixels:+0.0;-0.0} px{(LegacySamples > 0 ? $", {LegacySamples} older shots assume {PickpocketSessionState.LegacySpeedPixelsPerSecond:F0} px/s" : "")})";
}

/// <summary>Small completion-only state. No image data, input ownership or capture state is persisted.</summary>
internal sealed class PickpocketSessionState
{
    internal const int MaximumAttempts = 1000;
    internal const int CalibrationMinimumSamples = 6;
    internal const int CalibrationWindow = 20;
    internal const double CalibrationMaximumShiftMs = 6;
    internal const double LegacySpeedPixelsPerSecond = 390;
    private sealed record SavedState(int Version, long CooldownUntilUnixMs, PickpocketRecentAttempt[] Recent);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string? path;
    private readonly Func<long> utcNow;
    private SavedState state = new(1, 0, []);
    internal string? Error { get; private set; }
    internal IReadOnlyList<PickpocketRecentAttempt> Recent => state.Recent.Take(5).ToArray();
    internal object Query(int page, string outcome)
    {
        if (page < 0 || page >= MaximumAttempts || outcome is not ("All" or "Grabbed" or "Missed" or "Ended"))
            throw new ArgumentException("Invalid history page or outcome.");
        var matches = state.Recent.Where(entry => outcome == "All" || entry.Outcome == outcome).ToArray();
        var current = Math.Min(page, Math.Max(0, (matches.Length - 1) / 5));
        return new { protocolVersion = UiBridge.ProtocolVersion, attempts = matches.Skip(current * 5).Take(5),
            total = matches.Length, page = current, pageSize = 5, retention = MaximumAttempts, error = Error };
    }
    internal long RemainingMs => Math.Clamp(state.CooldownUntilUnixMs - utcNow(), 0, 180_000);

    /// <summary>
    /// Shifts the saved thin-target advance for one color by the median of recent
    /// precision shots. Each shot implies the advance that would have stopped the
    /// marker on center: applied advance plus (stop offset / marker speed). A
    /// positive offset means the marker stopped past center, so press earlier.
    /// Entries saved before speed was recorded assume the measured live speed.
    /// The result never moves more than six ms from the saved value.
    /// </summary>
    internal PickpocketAdvanceCalibration Calibrate(PickpocketBandColor color, int baseMs)
    {
        var shots = state.Recent
            .Where(r => r.Color == color && r.AutomaticPresses == 1 && r.InputMode == "PrecisionAttempt"
                && r.Outcome is "Grabbed" or "Missed" && r.OffsetPixels is { } offset && double.IsFinite(offset) && Math.Abs(offset) <= 12
                && (r.AppliedAdvanceMs is not null || r.WidthPixels is <= 6))
            .OrderByDescending(r => r.EndedAtUnixMs).Take(CalibrationWindow)
            .Select(r =>
            {
                var legacy = r.SpeedPixelsPerSecond is null;
                double? applied = r.AppliedAdvanceMs ?? (color == PickpocketBandColor.Red ? r.RedAdvanceMs : color == PickpocketBandColor.Yellow ? r.YellowAdvanceMs : 8);
                var speed = legacy ? LegacySpeedPixelsPerSecond : Math.Abs(r.SpeedPixelsPerSecond!.Value);
                var implied = applied is { } a && double.IsFinite(speed) && speed >= 50 ? a + r.OffsetPixels!.Value / speed * 1000 : double.NaN;
                return (Implied: implied, Offset: r.OffsetPixels!.Value, Legacy: legacy);
            })
            .Where(s => double.IsFinite(s.Implied)).ToArray();
        var legacyCount = shots.Count(s => s.Legacy);
        if (shots.Length < CalibrationMinimumSamples) return new(color, baseMs, baseMs, shots.Length, legacyCount, 0);
        var applied = Math.Clamp(Math.Round(Math.Clamp(Median(shots.Select(s => s.Implied)), baseMs - CalibrationMaximumShiftMs, baseMs + CalibrationMaximumShiftMs), 1), 0, 20);
        return new(color, baseMs, applied, shots.Length, legacyCount, Median(shots.Select(s => s.Offset)));
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }

    internal PickpocketSessionState(string? path = null, Func<long>? utcNow = null)
    {
        this.path = path;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (path is null || !File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Saved state is too large.");
            var loaded = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path), Json);
            if (loaded is null || loaded.Version is not (1 or 2) || loaded.CooldownUntilUnixMs < 0 || loaded.Recent is null
                || loaded.Recent.Length > MaximumAttempts
                || loaded.Recent.Any(r => r is null || string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 80 || r.EndedAtUnixMs < 0
                    || r.Outcome is not ("Grabbed" or "Missed" or "Ended") || r.AutomaticPresses is < 0 or > 1
                    || (r.Color is { } color && !Enum.IsDefined(color))
                    || (r.WidthPixels is { } width && (!double.IsFinite(width) || width <= 0))
                    || (r.OffsetPixels is { } offset && !double.IsFinite(offset))
                    || r.RedAdvanceMs is < 0 or > 20 || r.YellowAdvanceMs is < 0 or > 20
                    || (r.SpeedPixelsPerSecond is { } speed && !double.IsFinite(speed))
                    || (r.AppliedAdvanceMs is { } applied && (!double.IsFinite(applied) || applied is < 0 or > 20))
                    || r.ItemName?.Length > 100 || r.EngineVersion?.Length > 100 || r.SessionId?.Length > 100
                    || r.InputMode?.Length > 50 || r.TargetPolicy?.Length > 50))
                throw new InvalidDataException("Saved state is invalid.");
            state = loaded with { Version = 2, Recent = loaded.Recent.OrderByDescending(r => r.EndedAtUnixMs).DistinctBy(r => r.Id).Take(MaximumAttempts).ToArray() };
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { Error = "Could not restore recent results and cooldown. " + error.Message; }
    }

    internal void Complete(PickpocketObserveStatus status, PickpocketShotResult? result, PickpocketBand? target)
    {
        var now = utcNow();
        var outcome = status.Observation.State is PickpocketVisualState.Grabbed or PickpocketVisualState.Missed
            ? status.Observation.State.ToString() : "Ended";
        var entry = new PickpocketRecentAttempt(Guid.NewGuid().ToString("N"), now, outcome,
            result?.Color ?? target?.Color, result?.WidthPixels ?? target?.Width, result?.OffsetPixels, Math.Clamp(status.AutomatedPressCount, 0, 1),
            target?.ItemName, status.RedAdvanceMs, status.YellowAdvanceMs,
            typeof(PickpocketSessionState).Assembly.GetName().Version?.ToString(),
            string.IsNullOrEmpty(status.EvidenceDirectory) ? null : Path.GetFileName(status.EvidenceDirectory), status.InputMode, status.TargetPolicy,
            result?.SpeedPixelsPerSecond, result?.AppliedAdvanceMs);
        state = new(2, now + 180_000, new[] { entry }.Concat(state.Recent).Take(MaximumAttempts).ToArray());
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".pending";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
            File.Move(temporary, path, overwrite: true);
            Error = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Error = "Recent results and cooldown could not be saved. " + error.Message; }
    }
}
