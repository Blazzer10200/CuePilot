using System.Drawing;

namespace CuePilot.Tests;

public sealed class PickpocketPrecisionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(20)]
    public void ConfiguredThinTargetAdvanceMovesOnlyThePlannedDeadline(int advance)
    {
        var baseline = new PickpocketTimingPredictor();
        var adjusted = new PickpocketTimingPredictor();
        PickpocketTimingPrediction? original = null, changed = null;
        for (var t = 0; t <= 224; t += 16)
        {
            var observation = new PickpocketObservation(PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), 100 + .4 * t,
                [new(PickpocketBandColor.Yellow, 206, 210)], 1, "Advance fixture");
            original = baseline.Observe(observation, PickpocketBandColor.Yellow, t, t + 2, new(0,16), precision: true);
            changed = adjusted.Observe(observation, PickpocketBandColor.Yellow, t, t + 2, new(0,16), precision: true, tinyAdvanceMs: advance);
        }
        Assert.True(original!.CanSchedule);
        Assert.True(changed!.CanSchedule);
        Assert.Equal(advance - 8, original.PressAtMs!.Value - changed.PressAtMs!.Value, 5);
        Assert.Equal(original.SpeedPixelsPerSecond, changed.SpeedPixelsPerSecond);
    }
    [Theory]
    [InlineData("Purple", 16, false)]
    [InlineData("Red", 4, true)]
    [InlineData("Yellow", 3, true)]
    [InlineData("Yellow", 4, true)]
    public void SmallTargetsScheduleOneCenterPressWithNominalDelay(string colorName, double width, bool experimental)
    {
        var color = Enum.Parse<PickpocketBandColor>(colorName);
        var predictor = new PickpocketTimingPredictor();
        var time = 0d;
        var keys = new List<(bool Up, double Time)>();
        var input = new PickpocketInputController(up => keys.Add((up, time)), () => time);
        input.BeginRun();
        PickpocketTimingPrediction? sent = null;
        for (var t = 0; t <= 400; t += 16)
        {
            time = Math.Max(time, t + 4);
            var observation = new PickpocketObservation(PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), 100 + .4 * t,
                [new(color, 208 - width / 2, 208 + width / 2)], 1, "Controlled narrow-target motion");
            var prediction = predictor.Observe(observation, color, t, time, new(0, 16), precision: true);
            input.TryTap(prediction, t, () => true, (deadline, _) => time = Math.Max(time, deadline), CancellationToken.None, precision: true);
            if (input.PressCount > 0 && sent is null) { sent = prediction; predictor.MarkAttempted(); }
        }
        Assert.Equal(1, input.PressCount);
        Assert.Equal(2, keys.Count);
        Assert.False(keys[0].Up);
        Assert.True(keys[1].Up);
        Assert.Equal(35, keys[1].Time - keys[0].Time, 5);
        // Slivers now trial 8 ms additional lead. This checks the corresponding
        // simulated delay, not a claim that 16 ms is measured game latency.
        Assert.InRange(100 + .4 * (keys[0].Time + (experimental ? 16 : 8)), 208 - width / 2, 208 + width / 2);
        Assert.Equal(experimental, sent!.Reason.StartsWith("Experimental", StringComparison.Ordinal));
        // Red's nominal hit must not be misrepresented as covering all latency.
        if (experimental) Assert.True(100 + .4 * keys[0].Time < 208 - width / 2);
    }

    [Fact]
    public void PrecisionDeliveryWindowDoesNotShortenTheKeyHold()
    {
        var now = 100d;
        var keys = new List<(bool Up, double Time)>();
        var input = new PickpocketInputController(up => keys.Add((up, now)), () => now);
        input.BeginRun();
        input.TryTap(new(true, 120, 124, 400, 2, "Narrow center"), 95, () => true,
            (deadline, _) => now = keys.Count == 0 ? deadline + 2 : deadline, CancellationToken.None, precision: true);
        Assert.Equal([(false, 122d), (true, 157d)], keys);
        Assert.True(input.Stop());
    }

    [Theory]
    [InlineData(true, 125)]
    [InlineData(false, 120)]
    public void PrecisionDoesNotSendAfterDeadlineOrFailedValidation(bool valid, double wake)
    {
        var now = 100d;
        var input = new PickpocketInputController(_ => Assert.Fail("Unexpected Space"), () => now);
        input.BeginRun();
        Assert.Throws<InvalidOperationException>(() => input.TryTap(new(true, 120, 124, 400, 2, "Narrow center"), 95,
            () => valid, (_, _) => now = wake, CancellationToken.None, precision: true));
        Assert.True(input.Consumed);
        Assert.Equal(0, input.PressCount);
    }

    [Fact]
    public void PrecisionKeepsCapturingWhenScheduledFrameWouldBeTooOld()
    {
        var input = new PickpocketInputController(_ => Assert.Fail("Unexpected Space"), () => 100);
        input.BeginRun();
        input.TryTap(new(true, 125, 129, 400, 2, "Narrow center"), 88, () => true,
            (_, _) => Assert.Fail("Should capture a newer frame"), CancellationToken.None, precision: true);
        Assert.False(input.Consumed);
        Assert.Equal(0, input.PressCount);
    }

    [Fact]
    public void PrecisionStopDuringWaitPreventsDelivery()
    {
        var now = 100d;
        var input = new PickpocketInputController(_ => Assert.Fail("Unexpected Space"), () => now);
        input.BeginRun();
        Assert.Throws<OperationCanceledException>(() => input.TryTap(new(true, 120, 124, 400, 2, "Narrow center"), 95,
            () => true, (deadline, _) => { now = deadline; input.Stop(); }, CancellationToken.None, precision: true));
        Assert.Equal(0, input.PressCount);
    }
}
