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
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "New workflow";
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.Now;
    public long DurationMicroseconds { get; set; }
    public int RecordedLeft { get; set; }
    public int RecordedTop { get; set; }
    public int RecordedWidth { get; set; }
    public int RecordedHeight { get; set; }
    public List<MacroEvent> Events { get; set; } = [];
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

        File.WriteAllText(path, JsonSerializer.Serialize(pattern, WorkflowJson.Options));
    }

    internal static WorkflowPattern Load(string path)
    {
        var pattern = JsonSerializer.Deserialize<WorkflowPattern>(File.ReadAllText(path), WorkflowJson.Options)
            ?? throw new InvalidDataException("The workflow file is empty or invalid.");
        if (pattern.FormatVersion != 1 || pattern.Events.Count == 0)
        {
            throw new InvalidDataException("This workflow file has no playable events or uses an unsupported format.");
        }

        return pattern;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Trim().Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? $"Workflow-{DateTime.Now:yyyyMMdd-HHmmss}" : safe;
    }
}
