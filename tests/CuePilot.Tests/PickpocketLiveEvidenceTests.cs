using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit.Abstractions;

namespace CuePilot.Tests;

public sealed class PickpocketLiveEvidenceTests(ITestOutputHelper output)
{
    private static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "Live");
    private sealed record Sample(int SampleCount, double MonotonicMilliseconds, double PresentationMilliseconds, PickpocketObservation Observation,
        PickpocketTimingPrediction? Prediction = null, int? SelectedBandIndex = null);

    [Fact]
    public void SuccessfulAutomaticRingPixelsKeepPurpleAndGrabbedResult()
    {
        PickpocketObservation? previous = null;
        foreach (var sample in new[] { 95, 96, 97 })
        {
            using var frame = new Bitmap(Path.Combine(DirectoryPath, $"automatic-ring-{sample}.png"));
            var observation = PickpocketDetector.Analyze(frame, previous: previous);
            Assert.Equal(sample == 97 ? PickpocketVisualState.Grabbed : PickpocketVisualState.Active, observation.State);
            var purple = Assert.Single(observation.Bands, b => b.Color == PickpocketBandColor.Purple);
            Assert.InRange(purple.Width, 28, 32);
            if (sample == 97) Assert.InRange(observation.MarkerX, purple.Left, purple.Right);
            previous = observation;
        }
    }

    [Fact]
    public void ThirdLivePixelsPreserveOnlyTheMarkerOccludedEdge()
    {
        using var before = new Bitmap(Path.Combine(DirectoryPath, "third-117.png"));
        using var edge = new Bitmap(Path.Combine(DirectoryPath, "third-121.png"));
        var prior = PickpocketDetector.Analyze(before);
        var raw = PickpocketDetector.Analyze(edge);
        var tracked = PickpocketDetector.Analyze(edge, previous: prior);
        var oldBand = Assert.Single(prior.Bands, b => b.Color == PickpocketBandColor.PaleGreen);
        Assert.True(Assert.Single(raw.Bands, b => b.Color == oldBand.Color).Left > oldBand.Left + 3);
        Assert.Equal(oldBand.Left, Assert.Single(tracked.Bands, b => b.Color == oldBand.Color).Left);
        // A changed opposite edge or an absent target cannot be reconstructed.
        var altered = raw with { Bands = [new(oldBand.Color, oldBand.Left + 8, oldBand.Right - 20)] };
        Assert.Equal(altered.Bands, PickpocketDetector.StabilizeMarkerOcclusion(altered, prior).Bands);
        var missing = raw with { Bands = [] };
        Assert.Empty(PickpocketDetector.StabilizeMarkerOcclusion(missing, prior).Bands);
        Assert.Equal(raw, PickpocketDetector.StabilizeMarkerOcclusion(raw, prior with { State = PickpocketVisualState.Grabbed }));
    }

    [Fact]
    public void ThirdLiveGreenEdgeOcclusionStillAllowsOneTimedTap()
    {
        var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        var frames = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(Path.Combine(DirectoryPath, "third-attempt.json")), json)!;
        var predictor = new PickpocketTimingPredictor();
        var now = 0d;
        var input = new PickpocketInputController(_ => { }, () => now);
        input.BeginRun();
        PickpocketObservation? previous = null;
        PickpocketBand? chosen = null;
        foreach (var frame in frames)
        {
            now = frame.MonotonicMilliseconds;
            var observation = PickpocketDetector.StabilizeMarkerOcclusion(frame.Observation, previous);
            if (chosen is null && observation.State == PickpocketVisualState.Preparing)
                chosen = observation.Bands[PickpocketObserverEngine.SelectBand(observation, "Widest")!.Value];
            var index = chosen is null ? null : PickpocketObserverEngine.MatchBand(observation, chosen);
            if (index is int selected)
            {
                var prediction = predictor.Observe(observation, chosen!.Color, frame.PresentationMilliseconds, now, new(0, 16), selected);
                input.TryTap(prediction, frame.PresentationMilliseconds, () => true, (time, _) => now = time, CancellationToken.None);
                if (input.PressCount > 0) break;
            }
            previous = observation;
        }
        Assert.Equal(1, input.PressCount);
        Assert.Equal(PickpocketBandColor.PaleGreen, chosen!.Color);
        output.WriteLine($"Third-live corrected tap: down={input.Delivery!.KeyDownMs:F3} ms, up={input.Delivery.KeyUpMs:F3} ms from first captured presentation.");
        foreach (var delay in new[] { 0d, 8d, 16d })
        {
            var delivery = input.Delivery!.KeyDownMs!.Value + delay;
            var before = frames.Last(f => f.PresentationMilliseconds <= delivery && f.Observation.State == PickpocketVisualState.Active);
            var after = frames.First(f => f.PresentationMilliseconds >= delivery && f.Observation.State == PickpocketVisualState.Active);
            var x = before.Observation.MarkerX + (after.Observation.MarkerX - before.Observation.MarkerX)
                * (delivery - before.PresentationMilliseconds) / (after.PresentationMilliseconds - before.PresentationMilliseconds);
            Assert.InRange(x, 1125, 1178);
        }
        input.Stop();
    }

    [Fact]
    public void SecondLiveCandidateFitsRecordedWhiteCrossingAndEndingRemainsDetectable()
    {
        var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        var frames = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(Path.Combine(DirectoryPath, "second-attempt.json")), json)!;
        var candidate = Assert.Single(frames, f => f.Prediction?.CanSchedule == true);
        var band = candidate.Observation.Bands[candidate.SelectedBandIndex!.Value];
        Assert.Equal(PickpocketBandColor.White, band.Color);
        Assert.True(candidate.Prediction!.SpeedPixelsPerSecond > 0);
        foreach (var delay in new[] { 0d, 8d, 16d })
        {
            var delivery = candidate.Prediction.PressAtMs!.Value + delay;
            var before = frames.Last(f => f.PresentationMilliseconds <= delivery && f.Observation.State == PickpocketVisualState.Active);
            var after = frames.First(f => f.PresentationMilliseconds >= delivery && f.Observation.State == PickpocketVisualState.Active);
            var span = after.PresentationMilliseconds - before.PresentationMilliseconds;
            Assert.InRange(span, 0.001, 40);
            var marker = before.Observation.MarkerX + (after.Observation.MarkerX - before.Observation.MarkerX)
                * (delivery - before.PresentationMilliseconds) / span;
            Assert.InRange(marker, band.Left, band.Right);
        }
        using var active = new Bitmap(Path.Combine(DirectoryPath, "second-candidate.png"));
        using var result = new Bitmap(Path.Combine(DirectoryPath, "second-grab.png"));
        Assert.Equal(PickpocketVisualState.Active, PickpocketDetector.Analyze(active).State);
        var grabbed = PickpocketDetector.Analyze(result);
        Assert.Equal(PickpocketVisualState.Grabbed, grabbed.State);
        // Saved crop origin is x=492, y=46 within the original capture.
        Assert.InRange(grabbed.MarkerX + 492, 788, 834);
    }

    [Theory]
    [InlineData("Widest", 1)]
    [InlineData("Blue", 1)]
    [InlineData("Purple", 0)]
    public void RecordedLiveTimingUsesSelectedGeometry(string policy, int expectedCandidates)
    {
        var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        var frames = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(Path.Combine(DirectoryPath, "first-attempt.json")), json)!;
        var predictor = new PickpocketTimingPredictor();
        PickpocketBand? chosen = null;
        PickpocketObservation? prior = null;
        var candidates = 0;
        foreach (var sample in frames)
        {
            var observation = sample.Observation;
            if (observation.State is PickpocketVisualState.Preparing or PickpocketVisualState.Hidden)
            {
                chosen = null;
                predictor.Reset();
            }
            if (chosen is null)
            {
                var direction = observation.State == PickpocketVisualState.Preparing ? 1 : prior?.State == PickpocketVisualState.Active ? Math.Sign(observation.MarkerX - prior.MarkerX) : 0;
                var selection = direction == 0 ? null : PickpocketObserverEngine.SelectBand(observation, policy, direction);
                if (selection.HasValue) chosen = observation.Bands[selection.Value];
            }
            var index = chosen is null ? null : PickpocketObserverEngine.MatchBand(observation, chosen);
            if (index.HasValue)
            {
                var prediction = predictor.Observe(observation, chosen!.Color, sample.PresentationMilliseconds, sample.MonotonicMilliseconds, new(0, 16), index);
                if (prediction.CanSchedule)
                {
                    candidates++;
                    output.WriteLine($"{policy}: sample={sample.SampleCount} press={prediction.PressAtMs:F2} latest={prediction.LatestPressAtMs:F2} speed={prediction.SpeedPixelsPerSecond:F2}");
                    // Check the hypothetical delivery against subsequent recorded positions,
                    // independently of the predictor's fitted line. This remains sampled
                    // video evidence, not validation of real input delivery.
                    foreach (var delay in new[] { 0d, 8d, 16d })
                    {
                        var delivery = prediction.PressAtMs!.Value + delay;
                        var before = frames.Last(f => f.PresentationMilliseconds <= delivery && f.Observation.State == PickpocketVisualState.Active);
                        var after = frames.First(f => f.PresentationMilliseconds >= delivery && f.Observation.State == PickpocketVisualState.Active);
                        var span = after.PresentationMilliseconds - before.PresentationMilliseconds;
                        Assert.InRange(span, 0, 60);
                        var position = span == 0 ? before.Observation.MarkerX : before.Observation.MarkerX
                            + (after.Observation.MarkerX - before.Observation.MarkerX) * (delivery - before.PresentationMilliseconds) / span;
                        Assert.InRange(position, chosen!.Left, chosen.Right);
                    }
                    if (policy == "Widest") Assert.True(prediction.SpeedPixelsPerSecond < 0, "Retained live Widest candidate is on the return pass.");
                    predictor.MarkAttempted();
                }
            }
            prior = observation;
        }
        Assert.Equal(expectedCandidates, candidates);
    }

    [Fact]
    public void LivePanelCropPreservesDetectionAndFitsWholeAttemptImageBudget()
    {
        using var full = new Bitmap(Path.Combine(DirectoryPath, "first-attempt.png"));
        var detected = PickpocketDetector.Analyze(full);
        var bounds = PickpocketDiagnosticSession.EvidenceRegion(full.Size, detected);
        using var cropped = full.Clone(bounds, PixelFormat.Format32bppArgb);
        var replay = PickpocketDetector.Analyze(cropped);
        Assert.Equal(PickpocketVisualState.Active, replay.State);
        Assert.InRange(Math.Abs(replay.MarkerX + bounds.X - detected.MarkerX), 0, 2);
        Assert.Equal(detected.Bands.Select(b => b.Color), replay.Bands.Select(b => b.Color));
        using var png = new MemoryStream();
        cropped.Save(png, ImageFormat.Png);
        // The first live attempt requested 56 images and lost its ending at 64 MiB.
        Assert.True(png.Length * 56 < 48L * 1024 * 1024, $"Cropped frame bytes={png.Length}");
        Assert.True(bounds.Width * bounds.Height < full.Width * full.Height / 2);
        output.WriteLine($"Live crop: {full.Width}x{full.Height} -> {cropped.Width}x{cropped.Height}; PNG={png.Length} bytes; 56 frames={png.Length * 56 / 1048576d:F2} MiB");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SampleTimerCanBeInterruptedAndDoesNotSkipItsDeadline(bool inputDeadline)
    {
        using var clock = new PickpocketSampleClock();
        Action<double, CancellationToken> wait = inputDeadline ? clock.WaitForInputDeadline : clock.WaitUntil;
        var start = PickpocketSampleClock.NowMilliseconds;
        wait(start + 16, CancellationToken.None);
        Assert.True(PickpocketSampleClock.NowMilliseconds - start >= 15);
        using var cancel = new CancellationTokenSource();
        cancel.CancelAfter(20);
        var watch = Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => wait(PickpocketSampleClock.NowMilliseconds + 2000, cancel.Token));
        Assert.True(watch.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public async Task CompareWaitPacingAtMeasuredLiveWorkDuration()
    {
        using var clock = new PickpocketSampleClock();
        var oldIntervals = new List<double>();
        var newIntervals = new List<double>();
        for (var mode = 0; mode < 2; mode++)
        for (var i = 0; i < 40; i++)
        {
            var start = PickpocketSampleClock.NowMilliseconds;
            // Bounded CPU work approximates the live run's measured ~9 ms processing.
            while (PickpocketSampleClock.NowMilliseconds - start < 9) Thread.SpinWait(50);
            if (mode == 0) await Task.Delay(Math.Max(2, (int)Math.Ceiling(16 - (PickpocketSampleClock.NowMilliseconds - start))));
            else clock.WaitUntil(start + 16, CancellationToken.None);
            (mode == 0 ? oldIntervals : newIntervals).Add(PickpocketSampleClock.NowMilliseconds - start);
        }
        output.WriteLine($"Pacing benchmark (not live capture): Task.Delay mean={oldIntervals.Average():F2} ms; waitable timer mean={newIntervals.Average():F2} ms, max={newIntervals.Max():F2} ms");
        Assert.All(newIntervals, interval => Assert.True(interval >= 15));
    }
}
