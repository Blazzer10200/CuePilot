using System.Text.Json;
using System.Text.Json.Serialization;

namespace CuePilot;

internal sealed record PickpocketRecentAttempt(string Id, long EndedAtUnixMs, string Outcome,
    PickpocketBandColor? Color, double? WidthPixels, double? OffsetPixels, int AutomaticPresses,
    string? ItemName = null, int? RedAdvanceMs = null, int? YellowAdvanceMs = null,
    string? EngineVersion = null, string? SessionId = null, string? InputMode = null, string? TargetPolicy = null);

/// <summary>Small completion-only state. No image data, input ownership or capture state is persisted.</summary>
internal sealed class PickpocketSessionState
{
    internal const int MaximumAttempts = 1000;
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
            string.IsNullOrEmpty(status.EvidenceDirectory) ? null : Path.GetFileName(status.EvidenceDirectory), status.InputMode, status.TargetPolicy);
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
