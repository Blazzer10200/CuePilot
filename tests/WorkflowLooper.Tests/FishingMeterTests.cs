namespace WorkflowLooper.Tests;

public sealed class FishingMeterTests
{
    [Fact]
    public void AutomaticInputDoesNotSilentlyUseExperimentalBackgroundMessages()
    {
        var target = new WindowTargetSettings { ProcessName = "process-that-cannot-exist-workflow-looper" };
        var capability = new TargetInputRouter(InputDeliveryMode.Automatic).Probe(target);

        Assert.False(capability.Ready);
        Assert.False(capability.SupportsCoveredWindow);
        Assert.Equal("Physical scan-code input", capability.Backend);
    }

    [Fact]
    public void ExplicitApplicationInputExposesCoveredWindowCapability()
    {
        var target = new WindowTargetSettings { ProcessName = "process-that-cannot-exist-workflow-looper" };
        var capability = new TargetInputRouter(InputDeliveryMode.Application).Probe(target);

        Assert.False(capability.Ready);
        Assert.True(capability.SupportsCoveredWindow);
        Assert.Equal("Application-addressed input", capability.Backend);
    }

    [Fact]
    public void DesktopCaptureIsTheOnlyMeterCaptureBackend()
    {
        using var capture = FrameSourceFactory.Create();

        Assert.Equal("Desktop GDI", capture.Name);
    }

    [Fact]
    public void FishingInteractionUsesPhysicalEScanCode()
    {
        var down = InputSender.CreateScanCodeInput(Keys.E, false);
        var up = InputSender.CreateScanCodeInput(Keys.E, true);

        Assert.Equal(NativeMethods.InputKeyboard, down.Type);
        Assert.Equal(18, down.Data.Keyboard.ScanCode);
        Assert.Equal(NativeMethods.KeyeventfScancode, down.Data.Keyboard.Flags);
        Assert.Equal(18, up.Data.Keyboard.ScanCode);
        Assert.Equal(NativeMethods.KeyeventfScancode | NativeMethods.KeyeventfKeyup, up.Data.Keyboard.Flags);
    }

    [Fact]
    public void RecordedWaitingFrameDoesNotLookLikeFishingMeter()
    {
        var observation = AnalyzeFixture("waiting.png");

        Assert.False(observation.IsVisible);
        Assert.False(observation.IsCaught);
    }

    [Fact]
    public void RecordedActiveFramesExposeTensionAndProgress()
    {
        var small = AnalyzeFixture("small.png");
        var large = AnalyzeFixture("large.png");
        var active = AnalyzeFixture("active.png");

        Assert.True(small.IsVisible);
        Assert.True(large.IsVisible);
        Assert.True(active.IsVisible);
        Assert.InRange(small.TensionRatio, 0.30, 1.10);
        Assert.InRange(large.TensionRatio, 0.30, 1.10);
        Assert.True(large.TensionRatio >= small.TensionRatio + 0.08);
        Assert.True(active.ProgressRatio > small.ProgressRatio);
        Assert.False(active.IsCaught);
    }

    [Fact]
    public void InitialMeterFrameIsVisibleBeforeProgressArcDevelops()
    {
        var initial = AnalyzeFixture("initial.png");

        Assert.True(initial.IsVisible);
        Assert.True(initial.ProgressRatio < 0.03);
        Assert.False(initial.IsCaught);
    }

    [Fact]
    public void RecordedCatchFrameIsRecognized()
    {
        var caught = AnalyzeFixture("caught.png");

        Assert.True(caught.IsVisible);
        Assert.True(caught.IsCaught);
        Assert.True(caught.ProgressRatio > 0.35);
    }

    [Fact]
    public void MeterTransitionsNeedTwoSecondsOfDetectorMissesBeforeAStartedCatchEnds()
    {
        Assert.False(FishingMeterReacquisition.HasEnded(7, 0.05));
        Assert.False(FishingMeterReacquisition.ShouldCollectAfterMeterLoss(7, 0.65));
        Assert.False(FishingMeterReacquisition.HasEnded(
            FishingMeterReacquisition.ConsecutiveMissingSamplesBeforeComplete - 1, 0.05));
        Assert.True(FishingMeterReacquisition.HasEnded(
            FishingMeterReacquisition.ConsecutiveMissingSamplesBeforeComplete, 0.05));
        Assert.True(FishingMeterReacquisition.ShouldCollectAfterMeterLoss(
            FishingMeterReacquisition.ConsecutiveMissingSamplesBeforeComplete, 0.65));
    }

    [Fact]
    public void MeterStartupToleratesOneMissedFrame()
    {
        var gate = new FishingMeterStabilityGate();
        var visible = new FishingMeterObservation(true, 0.45, 0.03, false, 0.9);

        Assert.False(gate.Observe(visible));
        Assert.False(gate.Observe(FishingMeterObservation.Missing));
        Assert.True(gate.Observe(visible));
    }

    [Fact]
    public void FailureNeedsTwoConsecutiveFramesBeforeTheRoutineRecasts()
    {
        var gate = new FishingFailureGate();
        var failed = new FishingMeterObservation(true, 0.2, 0, false, 0.9, IsFailed: true);

        Assert.False(gate.Observe(failed));
        Assert.False(gate.Observe(FishingMeterObservation.Missing));
        Assert.False(gate.Observe(failed));
        Assert.True(gate.Observe(failed));
    }

    [Theory]
    [InlineData("bright-water-initial.png")]
    [InlineData("bright-sky-initial.png")]
    public void MeterIsFoundInSuppliedBrightSceneFrames(string name)
    {
        using var frame = LoadFixture(name);
        var observation = FishingMeterService.AnalyzeFrame(frame);

        Assert.True(observation.IsVisible, observation.ToString());
        Assert.False(observation.IsCaught, observation.ToString());
    }

    [Fact]
    public void SuppliedGoneAwayFrameIsRecognizedAsAFailedMeter()
    {
        using var frame = LoadFixture("bright-water-got-away.png");

        var observation = FishingMeterService.AnalyzeFrame(frame);

        Assert.True(observation.IsVisible, observation.ToString());
        Assert.True(observation.IsFailed, observation.ToString());
    }

    [Theory]
    [InlineData("video-day-initial.png")]
    [InlineData("video-day-early.png")]
    [InlineData("video-day-active.png")]
    [InlineData("video-day-mid.png")]
    [InlineData("video-day-progress.png")]
    [InlineData("video-day-late.png")]
    public void MeterIsFoundInFullResolutionDaytimeVideoFrames(string name)
    {
        using var frame = LoadFixture(name);

        var observation = FishingMeterService.AnalyzeFrame(frame);

        Assert.True(observation.IsVisible, observation.ToString());
        Assert.False(observation.IsFailed, observation.ToString());
    }

    [Fact]
    public void InitialPulseStateFromTheDaytimeReplayStaysLocked()
    {
        using var frame = LoadFixture("video-day-pulse-initial.png");

        var observation = FishingMeterService.AnalyzeFrame(frame);

        Assert.True(observation.IsVisible, observation.ToString());
        Assert.False(observation.IsFailed, observation.ToString());
    }

    [Fact]
    public void CompletedDaytimeReplayFrameIsRecognizedAsCaught()
    {
        using var frame = LoadFixture("video-day-caught.jpg");

        var observation = FishingMeterService.AnalyzeFrame(frame);

        Assert.True(observation.IsVisible, observation.ToString());
        Assert.True(observation.IsCaught, observation.ToString());
    }

    [Fact]
    public void RecordedFullScreenMeterIsFoundAtItsRightOfCenterPosition()
    {
        using var frame = LoadVisionFixture("live-meter-active.png");

        var observation = FishingMeterService.AnalyzeFrame(frame);

        Assert.True(observation.IsVisible, observation.ToString());
        Assert.False(observation.IsCaught, observation.ToString());
        Assert.False(observation.IsFailed, observation.ToString());
    }

    [Theory]
    [InlineData("small.png")]
    [InlineData("large.png")]
    [InlineData("initial.png")]
    [InlineData("active.png")]
    [InlineData("caught.png")]
    public void RecordedMeterFramesAreFoundThroughTheFullFrameLocator(string name)
    {
        using var frame = LoadFixture(name);

        var observation = FishingMeterService.AnalyzeFrame(frame);

        Assert.True(observation.IsVisible, observation.ToString());
    }

    [Theory]
    [InlineData("live-cast-gdi.png")]
    [InlineData("live-cast-ready.png")]
    public void FullScreenCastScenesDoNotTriggerTheMeter(string name)
    {
        using var frame = LoadVisionFixture(name);

        var observation = FishingMeterService.AnalyzeFrame(frame);

        Assert.False(observation.IsVisible, observation.ToString());
    }

    [Fact]
    public void ControllerUsesBoundedPulsesAndPredictiveBraking()
    {
        var controller = new FishingTensionController(55, 68, 35, 90, 70);
        var frequency = System.Diagnostics.Stopwatch.Frequency;
        var visible = new FishingMeterObservation(true, 0.45, 0.1, false, 0.9);

        var first = controller.Observe(visible, frequency);
        Assert.Equal(FishingControlAction.Pulse, first.Action);
        Assert.InRange(first.PulseMilliseconds, 35, 90);

        var fastRise = controller.Observe(visible with { TensionRatio = 0.63 }, frequency + frequency / 10);
        Assert.Equal(FishingControlAction.None, fastRise.Action);
        Assert.True(fastRise.VelocityPerSecond > 1);

        var settled = controller.Observe(visible with { TensionRatio = 0.52 }, frequency * 2);
        Assert.Equal(FishingControlAction.Pulse, settled.Action);
        Assert.InRange(settled.PulseMilliseconds, 35, 90);

        var complete = controller.Observe(visible with { IsCaught = true }, frequency * 3);
        Assert.Equal(FishingControlAction.Complete, complete.Action);
    }

    [Fact]
    public void DefaultControllerPreservesTheOriginalPulseEnvelope()
    {
        var settings = AppSettings.Defaults().Routine;
        var controller = new FishingTensionController(
            settings.FishingLowerTensionPercent,
            settings.FishingUpperTensionPercent,
            settings.FishingMinimumPulseMilliseconds,
            settings.FishingMaximumPulseMilliseconds,
            settings.FishingMinimumRestMilliseconds);
        var frequency = System.Diagnostics.Stopwatch.Frequency;
        var observation = new FishingMeterObservation(true, 0.40, 0.05, false, 1);

        var first = controller.Observe(observation, frequency);
        var tooSoon = controller.Observe(observation, frequency + frequency / 20);
        var afterRest = controller.Observe(observation, frequency + frequency / 10);

        Assert.InRange(first.PulseMilliseconds, 35, 90);
        Assert.Equal(FishingControlAction.None, tooSoon.Action);
        Assert.Equal(FishingControlAction.Pulse, afterRest.Action);
        Assert.InRange(afterRest.PulseMilliseconds, 35, 90);
    }

    [Fact]
    public void FailedMeterNeverCommandsAMousePulse()
    {
        var controller = new FishingTensionController(55, 68, 35, 90, 70);
        var observation = new FishingMeterObservation(true, 0.15, 0, false, 1, IsFailed: true);

        var decision = controller.Observe(observation, System.Diagnostics.Stopwatch.Frequency);

        Assert.Equal(FishingControlAction.None, decision.Action);
    }

    [Fact]
    public void FailedLiveTraceNeverCommandsAnUnboundedHold()
    {
        var controller = new FishingTensionController(55, 68, 35, 90, 70);
        var frequency = System.Diagnostics.Stopwatch.Frequency;
        var samples = new (int Milliseconds, double Tension)[]
        {
            (52, 0.536), (160, 0.457), (270, 0.631), (377, 0.662), (471, 0.820),
            (583, 0.647), (692, 0.631), (798, 0.647), (909, 0.552), (1014, 0.552),
            (1108, 0.552), (1219, 0.442), (1326, 0.457), (1436, 0.536), (1541, 0.536),
            (1635, 0.662), (1741, 0.647), (1838, 0.678), (1947, 0.694), (2052, 0.836),
        };

        var decisions = samples.Select(sample => controller.Observe(
            new FishingMeterObservation(true, sample.Tension, 0.05, false, 1),
            sample.Milliseconds * frequency / 1_000)).ToArray();

        Assert.Contains(decisions, decision => decision.Action == FishingControlAction.Pulse);
        Assert.All(decisions.Where(decision => decision.Action == FishingControlAction.Pulse),
            decision => Assert.InRange(decision.PulseMilliseconds, 35, 90));
        Assert.DoesNotContain(decisions, decision => decision.PulseMilliseconds > 90);
        Assert.All(decisions.Select((decision, index) => (decision, index))
                .Where(item => item.decision.Action == FishingControlAction.Pulse),
            item => Assert.True(samples[item.index].Tension <= 0.55));
    }

    private static FishingMeterObservation AnalyzeFixture(string name)
    {
        using var bitmap = LoadFixture(name);
        return FishingMeterDetector.Analyze(bitmap);
    }

    private static Bitmap LoadFixture(string name) =>
        new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fishing", name));

    private static Bitmap LoadVisionFixture(string name) =>
        new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Prompts", name));
}
