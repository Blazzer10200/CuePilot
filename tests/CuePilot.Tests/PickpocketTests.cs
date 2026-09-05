using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Xunit.Abstractions;

namespace CuePilot.Tests;

public sealed class PickpocketTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, "Hidden")]
    [InlineData(2, "Preparing")]
    [InlineData(3, "Active")]
    [InlineData(4, "Active")]
    [InlineData(5, "Active")]
    [InlineData(6, "Active")]
    [InlineData(7, "Active")]
    [InlineData(8, "Active")]
    [InlineData(9, "Active")]
    [InlineData(10, "Active")]
    [InlineData(11, "Grabbed")]
    [InlineData(12, "Grabbed")]
    [InlineData(13, "Hidden")]
    [InlineData(14, "Active")]
    [InlineData(15, "Active")]
    public void RecordedStatesAndTargetBands(int index, string expected)
    {
        using var frame = Load(index);
        var observation = PickpocketDetector.Analyze(frame);
        output.WriteLine($"{observation} bands={string.Join(';', observation.Bands)}");
        Assert.Equal(expected, observation.State.ToString());
        if (expected == "Hidden") return;
        Assert.InRange(observation.Bar.Left, 50, 54);
        Assert.InRange(observation.Bar.Width, 570, 580);
        var purple = Assert.Single(observation.Bands, b => b.Color == PickpocketBandColor.Purple);
        // The green marker occludes the purple interval at the recorded hit.
        Assert.InRange(purple.Left, 266, 279);
        Assert.InRange(purple.Right, 278, 285);
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void DetectorHandlesScaledAndTranslatedPanel(double scale)
    {
        using var source = Load(5);
        using var frame = new Bitmap(1920, 1080);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(65, 60, 48));
            graphics.DrawImage(source, new Rectangle(410, 610, (int)(source.Width * scale), (int)(source.Height * scale)));
        }
        var observation = PickpocketDetector.Analyze(frame);
        Assert.Equal(PickpocketVisualState.Active, observation.State);
        Assert.InRange(observation.MarkerX, 410 + (764 - 620) * scale - 5, 410 + (764 - 620) * scale + 5);
    }

    [Fact]
    public void ColorfulBackgroundAndPanelWithoutHeaderAreNotActive()
    {
        using var frame = Load(5);
        using (var graphics = Graphics.FromImage(frame)) graphics.FillRectangle(Brushes.Black, 45, 5, 130, 40);
        Assert.Equal(PickpocketVisualState.Hidden, PickpocketDetector.Analyze(frame).State);
    }

    internal static Bitmap Load(int index) => new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", $"clip-{index:00}.png"));

    private static readonly PickpocketTimingBudget Budget = new(8, 12);
    private static PickpocketObservation Sample(double x, double width = 14) => new(
        PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), x,
        [new PickpocketBand(PickpocketBandColor.Purple, 200 - width / 2, 200 + width / 2)], 1, "Synthetic motion.");

    private static PickpocketTimingPrediction Feed(PickpocketTimingPredictor predictor, double age = 0, double width = 14)
    {
        PickpocketTimingPrediction? result = null;
        for (var t = 0; t <= 60; t += 20)
            result = predictor.Observe(Sample(100 + t, width), PickpocketBandColor.Purple, t, t + age, Budget);
        return result!;
    }

    [Fact]
    public void PredictionUsesPresentationTimeAndInputDelay()
    {
        var fresh = Feed(new PickpocketTimingPredictor());
        var delayed = Feed(new PickpocketTimingPredictor(), 15);
        Assert.True(fresh.CanSchedule, fresh.Reason);
        Assert.True(delayed.CanSchedule, delayed.Reason);
        Assert.Equal(90, fresh.PressAtMs!.Value, 5);
        Assert.Equal(fresh.PressAtMs, delayed.PressAtMs);
    }

    [Fact]
    public void NarrowTargetAndStaleFramesWithholdPrediction()
    {
        Assert.False(Feed(new PickpocketTimingPredictor(), width: 4).CanSchedule);
        Assert.False(Feed(new PickpocketTimingPredictor(), age: 50).CanSchedule);
    }

    [Fact]
    public void AttemptIsLatchedThroughDisappearanceAndOnlyPreparationRearms()
    {
        var predictor = new PickpocketTimingPredictor();
        Assert.True(Feed(predictor).CanSchedule);
        predictor.MarkAttempted();
        predictor.Observe(PickpocketObservation.Missing, PickpocketBandColor.Purple, 80, 80, Budget);
        Assert.False(Feed(predictor).CanSchedule);
        predictor.Observe(Sample(0) with { State = PickpocketVisualState.Preparing }, PickpocketBandColor.Purple, 0, 0, Budget);
        Assert.True(Feed(predictor).CanSchedule);
    }

    [Fact]
    public void FrozenReversedAndRepeatedFramesInvalidateMotion()
    {
        foreach (var (time, x) in new[] { (80d, 160d), (80d, 155d), (60d, 160d), (140d, 240d) })
        {
            var predictor = new PickpocketTimingPredictor();
            Feed(predictor);
            var result = predictor.Observe(Sample(x), PickpocketBandColor.Purple, time, time, Budget);
            Assert.False(result.CanSchedule, result.Reason);
        }
    }

    [Fact]
    public void ChangedTargetAndAmbiguousBandsInvalidateHistory()
    {
        var predictor = new PickpocketTimingPredictor();
        Feed(predictor);
        var changed = Sample(180) with { Bands = [new(PickpocketBandColor.Purple, 250, 264)] };
        Assert.False(predictor.Observe(changed, PickpocketBandColor.Purple, 80, 80, Budget).CanSchedule);
        Feed(predictor);
        var ambiguous = Sample(180) with { Bands = [new(PickpocketBandColor.Purple, 193, 207), new(PickpocketBandColor.Purple, 300, 320)] };
        Assert.False(predictor.Observe(ambiguous, PickpocketBandColor.Purple, 80, 80, Budget).CanSchedule);
    }

    [Fact]
    public void AbruptSpeedChangeAndInvalidTimingAreRejected()
    {
        var predictor = new PickpocketTimingPredictor();
        Feed(predictor);
        Assert.False(predictor.Observe(Sample(185), PickpocketBandColor.Purple, 70, 70, Budget).CanSchedule);
        Assert.False(predictor.Observe(Sample(190), PickpocketBandColor.Purple, double.NaN, 80, Budget).CanSchedule);
        Assert.False(predictor.Observe(Sample(190), PickpocketBandColor.Purple, 80, 80, new(20, 10)).CanSchedule);
    }

    [Fact]
    public void RecordedCrossingPredictsOnePressInsidePurpleWithLatencyEnvelope()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket");
        var frames = JsonSerializer.Deserialize<PickpocketReplay.Frame[]>(File.ReadAllText(Path.Combine(directory, "crossing.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var predictor = new PickpocketTimingPredictor();
        var predictions = new List<PickpocketTimingPrediction>();
        var positions = new List<(double Time, double X)>();
        foreach (var frame in frames)
        {
            using var bitmap = new Bitmap(Path.Combine(directory, frame.File));
            var observation = PickpocketDetector.Analyze(bitmap);
            Assert.Equal(frame.ExpectedState, observation.State.ToString());
            positions.Add((frame.PresentationMs, observation.MarkerX));
            var prediction = predictor.Observe(observation, PickpocketBandColor.Purple, frame.PresentationMs, frame.PresentationMs + 2, new(0, 16));
            if (!prediction.CanSchedule) continue;
            predictions.Add(prediction);
            predictor.MarkAttempted();
        }
        var press = Assert.Single(predictions).PressAtMs!.Value;
        // Independent video truth: purple is x=268..283 in the retained crop.
        // Both ends of the simulated delivery envelope must land inside it.
        foreach (var time in new[] { press, press + 16 })
        {
            var before = positions.Last(p => p.Time <= time);
            var after = positions.First(p => p.Time >= time);
            var x = after.Time == before.Time ? before.X
                : before.X + (after.X - before.X) * (time - before.Time) / (after.Time - before.Time);
            Assert.InRange(x, 268, 283);
        }
    }

    [Fact]
    public void TrackedCropFitsOne60FpsFrameBudget()
    {
        using var frame = Load(5);
        for (var i = 0; i < 5; i++) PickpocketDetector.Analyze(frame);
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 60; i++) PickpocketDetector.Analyze(frame);
        var mean = clock.Elapsed.TotalMilliseconds / 60;
        output.WriteLine($"crop_detector_mean_ms={mean:F3}");
        Assert.True(mean < 1000d / 60, $"Detector mean {mean:F2} ms exceeds one 60 fps frame.");
    }
}
