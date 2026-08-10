namespace WorkflowLooper;

internal sealed record WorkflowPreset(string Name, string Description, int IntervalMilliseconds, int HoldMilliseconds)
{
    public override string ToString() => Name;
}

internal static class PresetFactory
{
    internal static IReadOnlyList<WorkflowPreset> BuiltIn { get; } =
    [
        new("Rapid Tap", "Quick 25 ms click every 150 ms.", 150, 25),
        new("Steady Tap", "Short 40 ms click every 500 ms.", 500, 40),
        new("Balanced Hold", "Measured hold pattern: 520 ms down every 1.3 seconds.", 1_300, 520),
        new("Slow Hold", "Controlled 700 ms hold every 2 seconds.", 2_000, 700),
    ];

    internal static WorkflowPattern Create(WorkflowPreset preset)
    {
        var bounds = SystemInformation.VirtualScreen;
        var cursor = Cursor.Position;
        return new WorkflowPattern
        {
            Name = preset.Name,
            RecordedAt = DateTimeOffset.Now,
            DurationMicroseconds = preset.IntervalMilliseconds * 1_000L,
            RecordedLeft = bounds.Left,
            RecordedTop = bounds.Top,
            RecordedWidth = bounds.Width,
            RecordedHeight = bounds.Height,
            Events =
            [
                new MacroEvent
                {
                    OffsetMicroseconds = 0,
                    Type = MacroEventType.MouseDown,
                    X = cursor.X,
                    Y = cursor.Y,
                    Data = 1,
                },
                new MacroEvent
                {
                    OffsetMicroseconds = preset.HoldMilliseconds * 1_000L,
                    Type = MacroEventType.MouseUp,
                    X = cursor.X,
                    Y = cursor.Y,
                    Data = 1,
                },
            ],
        };
    }
}
