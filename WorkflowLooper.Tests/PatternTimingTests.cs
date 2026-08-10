namespace WorkflowLooper.Tests;

public sealed class PatternTimingTests
{
    [Fact]
    public void NormalizeLeftClicksUsesExactIntervalAndHold()
    {
        var pattern = ClickPattern();

        var count = PatternTiming.NormalizeLeftClicks(pattern, 150, 25);
        var analysis = PatternTiming.AnalyzeLeftClicks(pattern);

        Assert.Equal(2, count);
        Assert.Equal(150, analysis.MedianIntervalMilliseconds);
        Assert.Equal(25, analysis.MedianHoldMilliseconds);
        Assert.Equal(185_000, pattern.Events[3].OffsetMicroseconds);
    }

    [Fact]
    public void ApplyDelaysRebuildsAbsoluteOffsets()
    {
        var pattern = ClickPattern();

        PatternTiming.ApplyDelays(pattern, [10, 25, 125, 25]);

        Assert.Equal([10_000L, 35_000L, 160_000L, 185_000L], pattern.Events.Select(item => item.OffsetMicroseconds));
        Assert.Equal(185_000, pattern.DurationMicroseconds);
    }

    private static WorkflowPattern ClickPattern() => new()
    {
        DurationMicroseconds = 700_000,
        Events =
        [
            new MacroEvent { OffsetMicroseconds = 10_000, Type = MacroEventType.MouseDown, Data = 1 },
            new MacroEvent { OffsetMicroseconds = 90_000, Type = MacroEventType.MouseUp, Data = 1 },
            new MacroEvent { OffsetMicroseconds = 430_000, Type = MacroEventType.MouseDown, Data = 1 },
            new MacroEvent { OffsetMicroseconds = 520_000, Type = MacroEventType.MouseUp, Data = 1 },
        ],
    };
}
