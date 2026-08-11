using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkflowLooper;

internal enum MacroEventType
{
    KeyDown,
    KeyUp,
    MouseMove,
    MouseDown,
    MouseUp,
    MouseWheel,
    MouseHorizontalWheel,
}

internal sealed class MacroEvent
{
    public bool Enabled { get; set; } = true;
    public long OffsetMicroseconds { get; set; }
    public MacroEventType Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Data { get; set; }
    public int VirtualKey { get; set; }
    public int ScanCode { get; set; }
    public bool Extended { get; set; }
}

internal sealed class WorkflowPattern
{
    public int FormatVersion { get; set; } = 2;
    public string Name { get; set; } = "New workflow";
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.Now;
    public long DurationMicroseconds { get; set; }
    public int RecordedLeft { get; set; }
    public int RecordedTop { get; set; }
    public int RecordedWidth { get; set; }
    public int RecordedHeight { get; set; }
    public int LoopCount { get; set; } = 1;
    public int PlaybackSpeedPercent { get; set; } = 100;
    public bool TrackCursor { get; set; }
    public string Notes { get; set; } = string.Empty;
    public WindowTargetSettings TargetWindow { get; set; } = new();
    public List<MacroEvent> Events { get; set; } = [];
}

internal sealed record ClickTimingAnalysis(
    int ClickCount,
    double MedianIntervalMilliseconds,
    double MeanIntervalMilliseconds,
    double MinimumIntervalMilliseconds,
    double MaximumIntervalMilliseconds,
    double MedianHoldMilliseconds);

internal static class PatternTiming
{
    internal static ClickTimingAnalysis AnalyzeLeftClicks(WorkflowPattern pattern)
    {
        var clicks = PairLeftClicks(pattern).ToList();
        var intervals = clicks.Zip(clicks.Skip(1), (left, right) => (right.Down.OffsetMicroseconds - left.Down.OffsetMicroseconds) / 1_000d).ToList();
        var holds = clicks.Select(click => (click.Up.OffsetMicroseconds - click.Down.OffsetMicroseconds) / 1_000d).ToList();
        return new ClickTimingAnalysis(
            clicks.Count,
            Median(intervals),
            intervals.Count == 0 ? 0 : intervals.Average(),
            intervals.Count == 0 ? 0 : intervals.Min(),
            intervals.Count == 0 ? 0 : intervals.Max(),
            Median(holds));
    }

    internal static int NormalizeLeftClicks(WorkflowPattern pattern, int intervalMilliseconds, int holdMilliseconds)
    {
        var clicks = PairLeftClicks(pattern).ToList();
        if (clicks.Count == 0)
        {
            return 0;
        }

        var interval = Math.Max(holdMilliseconds + 1, intervalMilliseconds) * 1_000L;
        var hold = Math.Clamp(holdMilliseconds, 1, intervalMilliseconds - 1) * 1_000L;
        var first = clicks[0].Down.OffsetMicroseconds;
        for (var index = 0; index < clicks.Count; index++)
        {
            clicks[index].Down.OffsetMicroseconds = first + index * interval;
            clicks[index].Up.OffsetMicroseconds = clicks[index].Down.OffsetMicroseconds + hold;
        }

        pattern.Events.Sort((left, right) => left.OffsetMicroseconds.CompareTo(right.OffsetMicroseconds));
        pattern.DurationMicroseconds = Math.Max(pattern.DurationMicroseconds, clicks[^1].Up.OffsetMicroseconds + Math.Max(1_000, interval - hold));
        return clicks.Count;
    }

    internal static void ApplyDelays(WorkflowPattern pattern, IReadOnlyList<double> delaysMilliseconds)
    {
        if (delaysMilliseconds.Count != pattern.Events.Count)
        {
            throw new ArgumentException("The delay list does not match the workflow event count.", nameof(delaysMilliseconds));
        }

        long offset = 0;
        for (var index = 0; index < pattern.Events.Count; index++)
        {
            offset += (long)Math.Round(Math.Max(0, delaysMilliseconds[index]) * 1_000d);
            pattern.Events[index].OffsetMicroseconds = offset;
        }

        pattern.DurationMicroseconds = Math.Max(offset, 1_000);
    }

    private static IEnumerable<(MacroEvent Down, MacroEvent Up)> PairLeftClicks(WorkflowPattern pattern)
    {
        MacroEvent? down = null;
        foreach (var item in pattern.Events.OrderBy(item => item.OffsetMicroseconds))
        {
            if (!item.Enabled)
            {
                continue;
            }

            if (item is { Type: MacroEventType.MouseDown, Data: 1 })
            {
                down = item;
            }
            else if (down is not null && item is { Type: MacroEventType.MouseUp, Data: 1 })
            {
                yield return (down, item);
                down = null;
            }
        }
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2d : ordered[middle];
    }
}

internal static class WorkflowJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}

internal static class WorkflowStore
{
    internal static string PatternDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkflowLooper",
        "Patterns");

    internal static string SaveNew(WorkflowPattern pattern)
    {
        Directory.CreateDirectory(PatternDirectory);
        var stem = SanitizeFileName(pattern.Name);
        var path = Path.Combine(PatternDirectory, $"{stem}.workflow.json");
        if (File.Exists(path))
        {
            path = Path.Combine(PatternDirectory, $"{stem}-{DateTime.Now:yyyyMMdd-HHmmss}.workflow.json");
        }

        Save(pattern, path);
        return path;
    }

    internal static void Save(WorkflowPattern pattern, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        pattern.FormatVersion = 2;
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(pattern, WorkflowJson.Options));
        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", true);
        }

        File.Move(temporaryPath, path, true);
    }

    internal static WorkflowPattern Load(string path)
    {
        var pattern = JsonSerializer.Deserialize<WorkflowPattern>(File.ReadAllText(path), WorkflowJson.Options)
            ?? throw new InvalidDataException("The workflow file is empty or invalid.");
        if (pattern.FormatVersion is not (1 or 2) || pattern.Events.Count == 0)
        {
            throw new InvalidDataException("This workflow file has no playable events or uses an unsupported format.");
        }

        pattern.FormatVersion = 2;
        pattern.LoopCount = Math.Max(0, pattern.LoopCount);
        pattern.PlaybackSpeedPercent = Math.Clamp(pattern.PlaybackSpeedPercent, 25, 400);
        pattern.TargetWindow ??= new WindowTargetSettings();
        return pattern;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Trim().Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? $"Workflow-{DateTime.Now:yyyyMMdd-HHmmss}" : safe;
    }
}
