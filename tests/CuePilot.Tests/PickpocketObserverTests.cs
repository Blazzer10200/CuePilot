using System.Drawing;
using System.Text.Json;

namespace CuePilot.Tests;

public sealed class PickpocketObserverTests
{
    [Fact]
    public void ThirdClipGeometrySelectionTracksLooseChangeDespiteOtherWhitePixels()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket");
        var frames = JsonSerializer.Deserialize<PickpocketReplay.Frame[]>(File.ReadAllText(Path.Combine(directory, "clip3-timing.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var predictor = new PickpocketTimingPredictor();
        PickpocketBand? chosen = null;
        var candidates = 0;
        foreach (var frame in frames)
        {
            using var bitmap = new Bitmap(Path.Combine(directory, frame.File));
            var observation = PickpocketDetector.Analyze(bitmap);
            Assert.Equal(PickpocketVisualState.Active, observation.State);
            var index = chosen is null ? PickpocketObserverEngine.SelectBand(observation, "Widest") : PickpocketObserverEngine.MatchBand(observation, chosen);
            Assert.True(index.HasValue, $"t={frame.PresentationMs} marker={observation.MarkerX} chosen={chosen} bands={string.Join(';', observation.Bands)}");
            chosen = observation.Bands[index.Value];
            Assert.Equal(PickpocketBandColor.White, chosen.Color);
            Assert.InRange(chosen.Center, 273, 279);
            var prediction = predictor.Observe(observation, chosen.Color, frame.PresentationMs, frame.PresentationMs, new(0, 16), index);
            if (!prediction.CanSchedule) continue;
            candidates++;
            Assert.InRange(prediction.PressAtMs!.Value, 1980, 2100);
            predictor.MarkAttempted();
        }
        Assert.Equal(1, candidates);
    }

    [Theory]
    [InlineData(1, "Hidden")]
    [InlineData(2, "Preparing")]
    [InlineData(3, "Active")]
    [InlineData(4, "Active")]
    [InlineData(5, "Active")]
    [InlineData(6, "Active")]
    [InlineData(7, "Active")]
    [InlineData(8, "Grabbed")]
    [InlineData(9, "Grabbed")]
    [InlineData(10, "Hidden")]
    public void ThirdClipRecognizesRemixedLayoutAndElectronicPartGrab(int index, string state)
    {
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", $"clip3-{index:00}.png"));
        var observation = PickpocketDetector.Analyze(frame);
        Assert.Equal(state, observation.State.ToString());
        if (state == "Hidden") { Assert.Empty(observation.Bands); return; }
        Assert.Contains(observation.Bands, b => b.Color == PickpocketBandColor.Yellow);
        // The marker covers Ruby at 2.5 s; a grabbed item flashes white afterward.
        if (index != 6) Assert.Contains(observation.Bands, b => b.Color == PickpocketBandColor.Red);
        Assert.Contains(observation.Bands, b => b.Color == PickpocketBandColor.White && b.Center > 220 && b.Center < 330);
        if (state != "Grabbed") Assert.Contains(observation.Bands, b => b.Color == PickpocketBandColor.PaleGreen && b.Center > 510 && b.Center < 552);
    }

    [Theory]
    [InlineData(1, "Hidden")]
    [InlineData(2, "Preparing")]
    [InlineData(3, "Active")]
    [InlineData(4, "Active")]
    [InlineData(5, "Active")]
    [InlineData(6, "Active")]
    [InlineData(7, "Active")]
    [InlineData(8, "Active")]
    [InlineData(9, "Missed")]
    [InlineData(10, "Missed")]
    [InlineData(11, "Hidden")]
    public void SecondClipRecognizesShuffledColorsAndMissedResult(int index, string state)
    {
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", $"clip2-{index:00}.png"));
        var observation = PickpocketDetector.Analyze(frame);
        Assert.Equal(state, observation.State.ToString());
        if (state is "Missed" or "Hidden") { Assert.Empty(observation.Bands); return; }
        Assert.Contains(observation.Bands, b => b.Color == PickpocketBandColor.Blue);
        Assert.Contains(observation.Bands, b => b.Color == PickpocketBandColor.Purple);
        Assert.Contains(observation.Bands, b => b.Color == PickpocketBandColor.White);
        if (index != 6) Assert.Contains(observation.Bands, b => b.Color == PickpocketBandColor.Yellow);
    }

    [Fact]
    public void LeftwardPredictionUsesCorrectEntryExitAndDeliveryEnvelope()
    {
        var predictor = new PickpocketTimingPredictor();
        PickpocketTimingPrediction? result = null;
        for (var t = 0; t <= 60; t += 20)
        {
            var observation = new PickpocketObservation(PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), 300 - t,
                [new(PickpocketBandColor.Blue, 193, 207)], 1, "Returning marker");
            result = predictor.Observe(observation, PickpocketBandColor.Blue, t, t, new(8, 12), 0);
        }
        Assert.True(result!.CanSchedule, result.Reason);
        Assert.Equal(-1000, result.SpeedPixelsPerSecond, 4);
        Assert.InRange(300 - (result.PressAtMs!.Value + 8), 193, 207);
        Assert.InRange(300 - (result.PressAtMs.Value + 12), 193, 207);
        var reverse = new PickpocketObservation(PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), 260,
            [new(PickpocketBandColor.Blue, 193, 207)], 1, "Reversal");
        Assert.False(predictor.Observe(reverse, PickpocketBandColor.Blue, 80, 80, new(8, 12), 0).CanSchedule);
    }

    [Fact]
    public void MissingRegionCannotTransferItsSelectionToAnotherListSlot()
    {
        var white = new PickpocketBand(PickpocketBandColor.White, 480, 570);
        var observation = new PickpocketObservation(PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), 400,
            [new(PickpocketBandColor.Purple, 130, 155), white], 1, "Yellow occluded");
        Assert.Null(PickpocketObserverEngine.MatchBand(observation, new(PickpocketBandColor.Yellow, 399, 403)));
        Assert.Equal(1, PickpocketObserverEngine.MatchBand(observation, white));
        Assert.Equal(0, PickpocketObserverEngine.SelectBand(observation, "Widest", -1));
    }

    [Fact]
    public void MissedResultImmediatelyStartsCooldownAndLatchesPrediction()
    {
        var tracker = new PickpocketAttemptTracker();
        tracker.Observe(PickpocketVisualState.Active, 6800);
        Assert.True(tracker.Observe(PickpocketVisualState.Missed, 6900));
        Assert.Equal(180000, tracker.RemainingMs(6900));
        Assert.False(tracker.Observe(PickpocketVisualState.Missed, 7400));
        var predictor = new PickpocketTimingPredictor();
        var miss = PickpocketObservation.Missing with { State = PickpocketVisualState.Missed };
        Assert.Contains("Attempt complete", predictor.Observe(miss, PickpocketBandColor.Yellow, 6900, 6900, new(0, 16)).Reason);
    }

    [Fact]
    public void CooldownLastsThreeMinutesAndSurvivesMotionReset()
    {
        var tracker = new PickpocketAttemptTracker();
        Assert.False(tracker.Observe(PickpocketVisualState.Hidden, 0));
        tracker.Observe(PickpocketVisualState.Active, 1000);
        Assert.True(tracker.Observe(PickpocketVisualState.Grabbed, 2000));
        Assert.Equal(180_000, tracker.RemainingMs(2000));
        tracker.ResetMotion();
        Assert.False(tracker.Observe(PickpocketVisualState.Grabbed, 3000));
        Assert.Equal(179_000, tracker.RemainingMs(3000));
        Assert.False(tracker.Observe(PickpocketVisualState.Preparing, 181999));
        Assert.Equal(1, tracker.RemainingMs(181999));
        Assert.Equal(0, tracker.RemainingMs(182000));
        tracker.Observe(PickpocketVisualState.Preparing, 182000);
        tracker.Observe(PickpocketVisualState.Active, 182100);
        Assert.Equal(2, tracker.Attempt);
        Assert.True(tracker.Observe(PickpocketVisualState.Grabbed, 183000));
    }

    [Fact]
    public void DisappearanceNeedsAnActiveAttemptAndThreeSecondsMissing()
    {
        var tracker = new PickpocketAttemptTracker();
        for (var i = 0; i < 8; i++) Assert.False(tracker.Observe(PickpocketVisualState.Hidden, i * 1000));
        tracker.Observe(PickpocketVisualState.Active, 10_000);
        // Recorded 2026-09-23 on grass: the live panel read Hidden for 2.6 s mid-minigame.
        for (var t = 10_100; t <= 12_700; t += 100) Assert.False(tracker.Observe(PickpocketVisualState.Hidden, t));
        tracker.Observe(PickpocketVisualState.Active, 12_800);
        Assert.Equal(0, tracker.RemainingMs(12_800));
        Assert.False(tracker.Observe(PickpocketVisualState.Hidden, 12_900));
        Assert.False(tracker.Observe(PickpocketVisualState.Hidden, 15_899));
        Assert.True(tracker.Observe(PickpocketVisualState.Hidden, 15_900));
        Assert.Equal(180000, tracker.RemainingMs(15_900));
        // Once the run's tap is spent, a false end blocks nothing: three frames suffice.
        var spent = new PickpocketAttemptTracker();
        spent.Observe(PickpocketVisualState.Active, 0);
        Assert.False(spent.Observe(PickpocketVisualState.Hidden, 20, inputSpent: true));
        Assert.False(spent.Observe(PickpocketVisualState.Hidden, 40, inputSpent: true));
        Assert.True(spent.Observe(PickpocketVisualState.Hidden, 60, inputSpent: true));
    }

    [Fact]
    public void ResultWithoutActivePlayIsSceneryAndStartsNoCooldown()
    {
        // Recorded 2026-09-23: grass beside a car read as Missed with no minigame.
        var tracker = new PickpocketAttemptTracker();
        Assert.False(tracker.Observe(PickpocketVisualState.Missed, 1000));
        Assert.False(tracker.Observe(PickpocketVisualState.Grabbed, 1100));
        Assert.Equal(0, tracker.RemainingMs(1100));
        Assert.Equal(0, tracker.Attempt);
        tracker.Observe(PickpocketVisualState.Preparing, 2000);
        tracker.Observe(PickpocketVisualState.Active, 2200);
        Assert.True(tracker.Observe(PickpocketVisualState.Grabbed, 3000));
        Assert.Equal(1, tracker.Attempt);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void DetectorFindsArbitrarySpacingCountsAndRepeatedColors(int count)
    {
        using var frame = PickpocketTests.Load(5);
        using (var g = Graphics.FromImage(frame))
        {
            g.FillRectangle(Brushes.Black, 52, 218, 576, 23);
            // Uneven spacing, all the same color: no fixed slot or color order.
            var starts = new[] { 180, 230, 350, 407, 540 };
            using var purple = new SolidBrush(Color.FromArgb(173, 102, 231));
            for (var i = 0; i < count; i++) g.FillRectangle(purple, starts[i], 218, 12 + i * 3, 23);
            using var marker = new SolidBrush(Color.FromArgb(190, 245, 50));
            g.FillRectangle(marker, 143, 218, 3, 23);
        }
        var observation = PickpocketDetector.Analyze(frame);
        Assert.Equal(PickpocketVisualState.Active, observation.State);
        Assert.Equal(count, observation.Bands.Count);
        Assert.All(observation.Bands, band => Assert.Equal(PickpocketBandColor.Purple, band.Color));
        Assert.Equal(count - 1, PickpocketObserverEngine.SelectBand(observation, "Widest"));
        Assert.Null(PickpocketObserverEngine.SelectBand(observation, "Red"));
    }

    [Fact]
    public void ExplicitBandIndexDisambiguatesRepeatedColorsAndDropoutKeepsAttemptLatch()
    {
        var predictor = new PickpocketTimingPredictor();
        PickpocketTimingPrediction? result = null;
        for (var t = 0; t <= 60; t += 20)
        {
            var observation = new PickpocketObservation(PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), 100 + t,
                [new(PickpocketBandColor.Purple, 193, 207), new(PickpocketBandColor.Purple, 400, 450)], 1, "Test");
            result = predictor.Observe(observation, PickpocketBandColor.Purple, t, t, new(8, 12), 0);
        }
        Assert.True(result!.CanSchedule, result.Reason);
        predictor.MarkAttempted();
        predictor.Observe(PickpocketObservation.Missing, PickpocketBandColor.Purple, 80, 80, new(8, 12));
        var next = new PickpocketObservation(PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), 190,
            [new(PickpocketBandColor.Purple, 193, 207)], 1, "Test");
        Assert.Contains("Attempt complete", predictor.Observe(next, PickpocketBandColor.Purple, 90, 90, new(8, 12), 0).Reason);
    }

    [Fact]
    public async Task ObserverCapturesEvidenceAndRetainsCooldownAcrossRestart()
    {
        var keySamples = 0;
        using var observer = new PickpocketObserverEngine(() => new FixtureSource(),
            _ => new(IntPtr.Zero, 3258, "FiveM_b3258_GTAProcess", "Fixture", new Rectangle(0, 0, 1920, 1080), true, false),
            _ => Interlocked.Increment(ref keySamples) is 1 or 2 or 4);
        var target = new WindowTargetSettings { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 3258 };
        var completion = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.StatusChanged += (_, status) => { if (status.State == "Cooldown") completion.TrySetResult(status); };
        observer.Configure("Purple");
        observer.Start(target);
        var cooldown = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.InRange(cooldown.CooldownUntilUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 178000, 180001);
        Assert.False(cooldown.Prediction!.CanSchedule);
        observer.Stop();
        Assert.False(observer.IsObserving);
        Assert.Equal(cooldown.CooldownUntilUnixMs, observer.Status.CooldownUntilUnixMs);
        var trace = File.ReadAllLines(Path.Combine(cooldown.EvidenceDirectory, "trace.jsonl"));
        Assert.NotEmpty(trace);
        using var record = JsonDocument.Parse(trace[^1]);
        Assert.Equal("Cooldown", record.RootElement.GetProperty("status").GetProperty("state").GetString());
        Assert.Equal(2, observer.Status.ManualSpacePressCount);
        Assert.Equal("Saved", observer.Status.Debug!.State);
        using (var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(cooldown.EvidenceDirectory, "summary.json"))))
        {
            Assert.Equal("Stopped", summary.RootElement.GetProperty("status").GetProperty("state").GetString());
            Assert.Equal(2, summary.RootElement.GetProperty("status").GetProperty("manualSpacePressCount").GetInt32());
        }
        // Active decisions are retained between the UI's 100 ms publications.
        Assert.True(trace.Length >= 8);
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.Start(target);
        var restarted = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.InRange(Math.Abs(restarted.CooldownUntilUnixMs - cooldown.CooldownUntilUnixMs), 0, 5);
        Assert.Equal(0, restarted.PredictedPressCount);
        observer.Stop();
    }

    private sealed class FixtureSource : IFrameSource
    {
        private int index;
        public string Name => "DXGI fixture";
        public bool TryCapture(WindowTargetSettings target, Rectangle relativeRegion, out FrameLease? frame, out FrameSourceStatus status)
        {
            var fixture = ++index switch { 1 => 2, < 8 => 5, _ => 11 };
            status = new(FrameSourceState.Ready, Name, "Fixture", TimeSpan.Zero, 0);
            frame = new(PickpocketTests.Load(fixture), status);
            return true;
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task LosingForegroundPausesAndResumesWithClearedPredictions()
    {
        var resolves = 0;
        // Foreground, then two unfocused polls (one minimized), then foreground again.
        using var observer = new PickpocketObserverEngine(() => new FixtureSource(),
            _ =>
            {
                var call = Interlocked.Increment(ref resolves);
                return new(IntPtr.Zero, 3258, "FiveM_b3258_GTAProcess", "Fixture", new Rectangle(0, 0, 1920, 1080), call is 1 or >= 4, call == 3);
            });
        var paused = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumed = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.StatusChanged += (_, status) =>
        {
            if (status.Detail.Contains("Paused", StringComparison.Ordinal)) paused.TrySetResult(status);
            else if (paused.Task.IsCompleted && status.State is "Searching" or "Tracking" or "Cooldown") resumed.TrySetResult(status);
        };
        observer.Start(new WindowTargetSettings { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 3258 });
        var pause = await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(pause.Observing);
        Assert.Equal("Waiting", pause.State);
        Assert.Null(pause.Prediction);
        await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(observer.IsObserving);
        observer.Stop();
        Assert.False(observer.IsObserving);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(pause.EvidenceDirectory, "summary.json")));
        Assert.Equal("Stopped", report.RootElement.GetProperty("status").GetProperty("state").GetString());
    }
}
