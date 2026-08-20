using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace CuePilot.Tests;

public sealed class FishingMeterTests
{
    [Fact]
    public void AutomaticInputDoesNotSilentlyUseExperimentalBackgroundMessages()
    {
        var target = new WindowTargetSettings { ProcessName = "process-that-cannot-exist-cuepilot" };
        var capability = new TargetInputRouter(InputDeliveryMode.Automatic).Probe(target);

        Assert.False(capability.Ready);
        Assert.False(capability.SupportsCoveredWindow);
        Assert.Equal("Physical scan-code input", capability.Backend);
    }

    [Fact]
    public void DesktopDuplicationIsThePrimaryCaptureBackend()
    {
        using var capture = FrameSourceFactory.Create();

        Assert.Equal("DXGI Desktop Duplication", capture.Name);
    }

    [Fact]
    public void FishingInteractionUsesPhysicalEScanCode()
    {
        var down = InputSender.CreateScanCodeInput(InputKey.E, false);
        var up = InputSender.CreateScanCodeInput(InputKey.E, true);

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
    public void DaylightVideoReplayKeepsTheMeterVisibleAcrossChangingBackgrounds()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fishing");
        var files = new[]
        {
            "meter-video-day-00s.jpg", "meter-video-day-10s.jpg", "meter-video-day-20s.jpg",
            "meter-video-day-30s.jpg", "meter-video-day-40s.jpg", "meter-video-day-50s.jpg",
        }.Select(name => Path.Combine(fixtureDirectory, name));

        var report = FishingReplayService.Replay(files);

        Assert.Equal(6, report.FrameCount);
        Assert.Equal(6, report.MeterFrames);
        Assert.NotEmpty(report.Transitions);
        Assert.All(report.Transitions, transition => Assert.True(transition.Confidence > 0));
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
        var threshold = FishingMeterReacquisition.MissingDurationBeforeComplete;

        Assert.False(FishingMeterReacquisition.HasEnded(TimeSpan.FromMilliseconds(400), 0.05));
        Assert.False(FishingMeterReacquisition.ShouldCollectAfterMeterLoss(TimeSpan.FromMilliseconds(400), 0.65));
        Assert.False(FishingMeterReacquisition.HasEnded(
            threshold - TimeSpan.FromMilliseconds(1), 0.05));
        Assert.True(FishingMeterReacquisition.HasEnded(
            threshold, 0.05));
        Assert.True(FishingMeterReacquisition.ShouldCollectAfterMeterLoss(
            threshold, 0.65));
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
    [InlineData("video-day-glare-miss.jpg")]
    public void MeterIsFoundInDaytimeGameplayFrames(string name)
    {
        using var frame = LoadFixture(name);

        var observation = FishingMeterService.AnalyzeFrame(frame);

        Assert.True(observation.IsVisible,
            $"{observation}{Environment.NewLine}{string.Join(Environment.NewLine, FishingMeterService.InspectFrame(frame))}");
        Assert.False(observation.IsFailed, observation.ToString());
    }

    [Theory]
    [InlineData("meter-video-day-00s.jpg")]
    [InlineData("meter-video-day-10s.jpg")]
    [InlineData("meter-video-day-20s.jpg")]
    [InlineData("meter-video-day-30s.jpg")]
    [InlineData("meter-video-day-40s.jpg")]
    [InlineData("meter-video-day-50s.jpg")]
    public void SuppliedDaytimeVideoMetersAreFoundAcrossChangingBackgrounds(string name)
    {
        using var frame = LoadFixture(name);

        var analysis = FishingMeterService.AnalyzeFrameDetailed(frame);

        Assert.True(analysis.Observation.IsVisible,
            $"{analysis}{Environment.NewLine}{string.Join(Environment.NewLine, FishingMeterService.InspectFrame(frame))}");
        Assert.False(analysis.Observation.IsFailed, analysis.ToString());
    }

    [Theory]
    [InlineData("live-meter-day-reflective-water.png", 114, 101, 403, 310)]
    [InlineData("live-meter-day-bright-shore.png", 111, 114, 397, 305)]
    [InlineData("live-meter-day-sky.png", 150, 225, 410, 315)]
    public void MeterStartupAppearanceIsRecognizedAcrossLiveBackgrounds(
        string name,
        int x,
        int y,
        int width,
        int height)
    {
        using var frame = LoadFixture(name);
        using var meter = frame.Clone(new Rectangle(x, y, width, height), PixelFormat.Format32bppArgb);

        var observation = FishingMeterDetector.Analyze(meter, out var evidence);

        Assert.True(observation.IsVisible, evidence.ToString());
        Assert.False(observation.IsFailed, observation.ToString());
    }

    [Fact]
    public void LiveDaytimeSkyFailureFrameIsRecognized()
    {
        using var frame = LoadFixture("live-meter-day-failed-sky.png");

        var observation = FishingMeterService.AnalyzeFrame(frame);

        Assert.True(observation.IsVisible, observation.ToString());
        Assert.True(observation.IsFailed, observation.ToString());
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
    public void DaytimeStartupUsesTheRealLmbKeycapSignature()
    {
        using var frame = LoadFixture("video-day-initial.png");

        var analysis = FishingMeterService.AnalyzeFrameDetailed(frame);

        Assert.True(analysis.Observation.IsVisible, analysis.ToString());
        Assert.NotNull(analysis.PrimaryCandidate);
        Assert.True(analysis.PrimaryCandidate.Value.Evidence.LmbPromptStrength >= 0.97,
            analysis.PrimaryCandidate.Value.Evidence.ToString());
    }

    [Fact]
    public void KnownMeterLocationIsInspectedBeforeStaticCandidates()
    {
        using var frame = LoadFixture("video-day-active.png");
        var tracker = new FishingMeterTracker();

        var first = FishingMeterService.AnalyzeFrameDetailed(frame, tracker);
        var second = FishingMeterService.AnalyzeFrameDetailed(frame, tracker);

        Assert.True(first.Observation.IsVisible, first.ToString());
        Assert.True(tracker.HasLock);
        Assert.True(second.Observation.IsVisible, second.ToString());
        Assert.True(second.UsedTrackedRegion, second.ToString());
        Assert.Equal(1, second.CandidateCount);
    }

    [Fact]
    public void LocalContrastKeepsAnExposureLiftedMeterVisible()
    {
        using var source = LoadFixture("video-day-active.png");
        using var lifted = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(lifted))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorMatrix(new ColorMatrix(new[]
            {
                new[] { 0.65f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0.65f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0.65f, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { 0.25f, 0.25f, 0.25f, 0f, 1f },
            }));
            graphics.DrawImage(source, new Rectangle(Point.Empty, source.Size),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }

        var analysis = FishingMeterService.AnalyzeFrameDetailed(lifted);

        Assert.True(analysis.Observation.IsVisible, analysis.ToString());
        Assert.NotNull(analysis.PrimaryCandidate);
        Assert.True(analysis.PrimaryCandidate.Value.Evidence.DiskContrast >= 0.12,
            analysis.PrimaryCandidate.Value.Evidence.ToString());
    }

    [Fact]
    public void DiagnosticCaptureAnnotatesTheExactAnalyzedFrameAndWritesEvidence()
    {
        using var frame = LoadFixture("video-day-initial.png");
        var analysis = FishingMeterService.AnalyzeFrameDetailed(frame);
        var directory = Path.Combine(Path.GetTempPath(), "CuePilot.Tests", Guid.NewGuid().ToString("N"));

        string imagePath;
        using (var diagnostics = new FishingDiagnosticLog(directory))
        {
            imagePath = diagnostics.CaptureEvidence("meter-lock", frame, analysis);
        }

        var metadataPath = Path.ChangeExtension(imagePath, ".json");
        Assert.True(File.Exists(imagePath));
        Assert.True(File.Exists(metadataPath));
        using var annotated = new Bitmap(imagePath);
        Assert.Equal(frame.Size, annotated.Size);
        using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
        Assert.Equal("meter-lock", metadata.RootElement.GetProperty("eventName").GetString());
        Assert.True(metadata.RootElement.GetProperty("visible").GetBoolean());
        Assert.True(metadata.RootElement.GetProperty("candidateCount").GetInt32() >= 1);
        Assert.True(metadata.RootElement.GetProperty("evidence").GetProperty("lmbPrompt").GetDouble() >= 0.97);
    }

    [Fact]
    public void DebugSessionPersistsAReplayableManifestAndDecisiveFrame()
    {
        var root = Path.Combine(Path.GetTempPath(), "CuePilot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var frame = LoadFixture("video-day-initial.png");
            var analysis = FishingMeterService.AnalyzeFrameDetailed(frame);
            using var lease = new FrameLease(
                new Bitmap(frame),
                new FrameSourceStatus(FrameSourceState.Ready, "Test capture", "Ready", TimeSpan.Zero, 4.2));
            string sessionDirectory;
            using (var session = new FishingDebugSession(AppSettings.Defaults().Routine, root))
            {
                sessionDirectory = session.DirectoryPath;
                session.SetStage("Meter", "Test meter scan");
                session.RecordCapture("meter", lease.Status, lease.Bitmap.Size);
                session.RecordMeter(analysis, lease, 1);
                session.Complete("Test complete");
            }

            var manifestPath = Path.Combine(sessionDirectory, "session.json");
            var eventsPath = Path.Combine(sessionDirectory, "events.jsonl");
            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(eventsPath));
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.False(manifest.RootElement.GetProperty("active").GetBoolean());
            Assert.Equal("Test complete", manifest.RootElement.GetProperty("outcome").GetString());
            var savedFrame = Assert.Single(manifest.RootElement.GetProperty("frames").EnumerateArray());
            Assert.Equal("meter-confirmed", savedFrame.GetProperty("label").GetString());
            Assert.True(File.Exists(Path.Combine(sessionDirectory, savedFrame.GetProperty("imageName").GetString()!)));
            Assert.Contains("\"eventName\":\"complete\"", File.ReadAllText(eventsPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DebugSessionReplacesTheLatestPromptSampleEvenWhenTheScoreIsUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "CuePilot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var evidence = new FishingPromptEvidence(default, default, "No prompt match");
            var observation = new FishingPromptObservation(FishingPromptKind.None, 0);
            string sessionDirectory;
            using (var session = new FishingDebugSession(AppSettings.Defaults().Routine, root))
            {
                sessionDirectory = session.DirectoryPath;
                using var firstBitmap = new Bitmap(200, 100, PixelFormat.Format32bppArgb);
                firstBitmap.SetPixel(10, 10, Color.Red);
                using var first = new FrameLease(
                    firstBitmap,
                    new FrameSourceStatus(FrameSourceState.Ready, "Test capture", "Ready", TimeSpan.Zero, 1));
                session.RecordPrompt(FishingPromptKind.Cast, observation, evidence, first, 1);

                using var latestBitmap = new Bitmap(200, 100, PixelFormat.Format32bppArgb);
                latestBitmap.SetPixel(10, 10, Color.Blue);
                using var latest = new FrameLease(
                    latestBitmap,
                    new FrameSourceStatus(FrameSourceState.Ready, "Test capture", "Ready", TimeSpan.Zero, 1));
                session.RecordPrompt(FishingPromptKind.Cast, observation, evidence, latest, 10);
                session.Complete("Test complete");
            }

            using var saved = new Bitmap(Path.Combine(sessionDirectory, "prompt-latest-sample.png"));
            Assert.Equal(Color.Blue.ToArgb(), saved.GetPixel(10, 10).ToArgb());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DebugSessionKeepsABoundedRollingBottomHudSequenceForEveryPromptScan()
    {
        var root = Path.Combine(Path.GetTempPath(), "CuePilot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var evidence = new FishingPromptEvidence(default, default, "No prompt match");
            var observation = new FishingPromptObservation(FishingPromptKind.None, 0);
            string sessionDirectory;
            using (var session = new FishingDebugSession(AppSettings.Defaults().Routine, root))
            {
                sessionDirectory = session.DirectoryPath;
                for (var sample = 1; sample <= 3; sample++)
                {
                    using var bitmap = new Bitmap(200, 100, PixelFormat.Format32bppArgb);
                    bitmap.SetPixel(10, 80, Color.FromArgb(sample, 0, 0));
                    using var lease = new FrameLease(
                        bitmap,
                        new FrameSourceStatus(FrameSourceState.Ready, "Test capture", "Ready", TimeSpan.Zero, 1));
                    session.RecordPrompt(FishingPromptKind.Cast, observation, evidence, lease, sample);
                }
                session.Complete("Test complete");
            }

            var rollDirectory = Path.Combine(sessionDirectory, "prompt-roll");
            Assert.Equal(3, Directory.GetFiles(rollDirectory, "*.jpg").Length);
            Assert.Equal(3, Directory.GetFiles(rollDirectory, "*.json").Length);
            using var saved = new Bitmap(Directory.GetFiles(rollDirectory, "*.jpg").Order().Last());
            Assert.Equal(35, saved.Height);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("live-meter-day-reflective-water.png")]
    [InlineData("live-meter-day-bright-shore.png")]
    [InlineData("live-meter-day-sky.png")]
    public void SuppliedDaylightStartupMetersAreFoundFromTheFullFrame(string name)
    {
        using var frame = LoadFixture(name);
        var analysis = FishingMeterService.AnalyzeFrameDetailed(frame);

        Assert.NotNull(analysis.PrimaryCandidate);
        Assert.False(string.IsNullOrWhiteSpace(analysis.PrimaryCandidate.Value.Evidence.DecisionReason));
        Assert.True(
            analysis.Observation.IsVisible,
            $"{analysis}{Environment.NewLine}{string.Join(Environment.NewLine, FishingMeterService.InspectFrame(frame))}");
        Assert.False(analysis.Observation.IsFailed, analysis.ToString());
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

    [Theory]
    [InlineData("daylight-hud-lookalike.jpg")]
    [InlineData("daylight-reel-lookalike.jpg")]
    public void DaylightCharacterAndReelGeometryDoNotLookLikeTheMeter(string name)
    {
        using var crop = LoadFixture(name);

        var analysis = FishingMeterService.AnalyzeFrameDetailed(crop);

        Assert.False(analysis.Observation.IsVisible, analysis.ToString());
    }

    [Fact]
    public void LiveDxgiPlayerGeometryWithoutLmbIdentityDoesNotLookLikeTheMeter()
    {
        using var crop = LoadFixture("live-dxgi-player-false-meter.png");
        var tracker = new FishingMeterTracker();

        var analysis = FishingMeterService.AnalyzeFrameDetailed(crop, tracker);

        Assert.False(analysis.Observation.IsVisible, analysis.ToString());
        Assert.False(tracker.HasLock);
        Assert.NotNull(analysis.PrimaryCandidate);
        Assert.True(analysis.PrimaryCandidate.Value.Evidence.LmbPromptStrength < 0.90,
            analysis.PrimaryCandidate.Value.Evidence.ToString());
    }

    [Fact]
    public void LiveActiveMeterCanAcquireWithVerifiedLmbIdentity()
    {
        using var frame = LoadVisionFixture("live-meter-active.png");
        var tracker = new FishingMeterTracker();

        var analysis = FishingMeterService.AnalyzeFrameDetailed(frame, tracker);

        Assert.True(analysis.Observation.IsVisible, analysis.ToString());
        Assert.True(tracker.HasLock);
        Assert.NotNull(analysis.PrimaryCandidate);
        Assert.True(analysis.PrimaryCandidate.Value.Evidence.LmbPromptStrength >= 0.90,
            analysis.PrimaryCandidate.Value.Evidence.ToString());
    }

    [Fact]
    public void Live2560PlayerAndWaterCannotAcquireAMeterLock()
    {
        using var frame = LoadFixture("live-2560-false-meter-lock.png");
        var tracker = new FishingMeterTracker();

        var analysis = FishingMeterService.AnalyzeFrameDetailed(frame, tracker);

        Assert.False(analysis.Observation.IsVisible, analysis.ToString());
        Assert.False(tracker.HasLock);
    }

    [Fact]
    public void Live2560MeterStillAcquiresAtItsObservedPositionAndScale()
    {
        using var frame = LoadFixture("live-2560-real-meter-lock.png");
        var tracker = new FishingMeterTracker();

        var analysis = FishingMeterService.AnalyzeFrameDetailed(frame, tracker);

        Assert.True(analysis.Observation.IsVisible, analysis.ToString());
        Assert.True(tracker.HasLock);
        Assert.NotNull(analysis.PrimaryCandidate);
        Assert.True(analysis.PrimaryCandidate.Value.Evidence.LmbPromptStrength >= 0.90,
            analysis.PrimaryCandidate.Value.Evidence.ToString());
    }

    [Theory]
    [InlineData(3440, 440)]
    [InlineData(5120, 1280)]
    public void MeterSearchRegionsTranslateIntoCenteredUltrawideSafeViewport(int frameWidth, int safeLeft)
    {
        var standard = FishingMeterService.GetCaptureRegions(new Rectangle(0, 0, 2560, 1440));
        var ultrawide = FishingMeterService.GetCaptureRegions(new Rectangle(0, 0, frameWidth, 1440));

        Assert.True(ultrawide.Count > standard.Count);
        for (var index = 0; index < standard.Count; index++)
        {
            Assert.Equal(standard[index].X + safeLeft, ultrawide[index].X);
            Assert.Equal(standard[index].Y, ultrawide[index].Y);
            Assert.Equal(standard[index].Size, ultrawide[index].Size);
        }
    }

    [Fact]
    public void RealMeterAcquiresInsideA3440By1440CenteredSafeViewport()
    {
        using var source = LoadFixture("live-2560-real-meter-lock.png");
        using var ultrawide = new Bitmap(3440, 1440);
        using (var graphics = Graphics.FromImage(ultrawide))
        {
            graphics.Clear(Color.Black);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImage(
                source,
                new Rectangle(440, 0, source.Width, source.Height),
                new Rectangle(0, 0, source.Width, source.Height),
                GraphicsUnit.Pixel);
        }
        var tracker = new FishingMeterTracker();

        var analysis = FishingMeterService.AnalyzeFrameDetailed(ultrawide, tracker);

        Assert.True(analysis.Observation.IsVisible, analysis.ToString());
        Assert.NotNull(analysis.PrimaryCandidate);
        Assert.True(analysis.PrimaryCandidate.Value.Region.Left >= 440, analysis.PrimaryCandidate.Value.ToString());
        Assert.True(analysis.PrimaryCandidate.Value.Region.Right <= 3000, analysis.PrimaryCandidate.Value.ToString());
    }

    [Fact]
    public void UltrawideMeterSearchIncludesAFullWidthHudFallback()
    {
        var regions = FishingMeterService.GetCaptureRegions(new Rectangle(0, 0, 3440, 1440));

        Assert.Contains(new Rectangle(1664, 465, 450, 346), regions);
    }

    [Fact]
    public void NearlyBlackExclusiveFullscreenFramesAreRejected()
    {
        using var capture = new FallbackFrameSource(
            new SolidFrameSource("DXGI test", Color.Black),
            new SolidFrameSource("GDI test", Color.Black));

        var captured = capture.TryCapture(
            new WindowTargetSettings(),
            new Rectangle(0, 0, 640, 360),
            out var frame,
            out var status);

        frame?.Dispose();
        Assert.False(captured);
        Assert.Equal(FrameSourceState.CaptureFailed, status.State);
        Assert.Contains("black frame", status.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Borderless Windowed", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VisibleFallbackFrameIsUsedAfterExclusivePrimaryReturnsBlack()
    {
        using var capture = new FallbackFrameSource(
            new SolidFrameSource("DXGI test", Color.Black),
            new SolidFrameSource("GDI test", Color.FromArgb(16, 18, 20)));

        var captured = capture.TryCapture(
            new WindowTargetSettings(),
            new Rectangle(0, 0, 640, 360),
            out var frame,
            out var status);

        using (frame)
        {
            Assert.True(captured, status.Detail);
            Assert.NotNull(frame);
            Assert.Equal("GDI test", status.Backend);
            Assert.Contains("Fallback active", status.Detail, StringComparison.Ordinal);
            Assert.Contains("black frame", status.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(2560, 1440, 0, 2560)]
    [InlineData(3440, 1440, 440, 2560)]
    [InlineData(5120, 1440, 1280, 2560)]
    [InlineData(1920, 1200, -106.6667, 2133.3333)]
    [InlineData(1280, 1024, -270.2222, 1820.4444)]
    public void SafeViewportUsesOneHeightScaledCanvasAcrossAspectRatios(
        int width,
        int height,
        double expectedLeft,
        double expectedWidth)
    {
        var viewport = GameViewportGeometry.CenteredSafeViewport(new Rectangle(0, 0, width, height));

        Assert.Equal(expectedLeft, viewport.Left, 3);
        Assert.Equal(expectedWidth, viewport.Width, 3);
        Assert.Equal(0, viewport.Top);
        Assert.Equal(height, viewport.Height);
    }

    [Theory]
    [InlineData(1920, 1200)]
    [InlineData(1280, 1024)]
    public void NarrowFramesProbeVirtualAndFrameRelativeMeterLayouts(int width, int height)
    {
        var regions = FishingMeterService.GetCaptureRegions(new Rectangle(0, 0, width, height));

        Assert.True(regions.Count > 8);
        Assert.Equal(regions.Count, regions.Distinct().Count());
    }

    [Theory]
    [InlineData("live-meter-sunset-rock.png")]
    [InlineData("live-meter-hillside.png")]
    [InlineData("live-meter-sky.png")]
    [InlineData("live-meter-sunset-water.png")]
    [InlineData("live-meter-evening-sky.png")]
    public void ManuallyCapturedMetersAcquireAcrossBackgrounds(string name)
    {
        using var frame = LoadFixture(name);
        var tracker = new FishingMeterTracker();

        var analysis = FishingMeterService.AnalyzeFrameDetailed(frame, tracker);

        Assert.True(analysis.Observation.IsVisible, analysis.ToString());
        Assert.False(analysis.Observation.IsFailed, analysis.ToString());
        Assert.True(tracker.HasLock);
        Assert.NotNull(analysis.PrimaryCandidate);
        Assert.True(analysis.PrimaryCandidate.Value.Evidence.LmbPromptStrength >= 0.90,
            analysis.PrimaryCandidate.Value.Evidence.ToString());
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

    private sealed class SolidFrameSource(string name, Color color) : IFrameSource
    {
        public string Name => name;

        public bool TryCapture(
            WindowTargetSettings target,
            Rectangle relativeRegion,
            out FrameLease? frame,
            out FrameSourceStatus status)
        {
            var bitmap = new Bitmap(relativeRegion.Width, relativeRegion.Height);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
            status = new FrameSourceStatus(FrameSourceState.Ready, name, "Test frame ready.", TimeSpan.Zero, 1);
            frame = new FrameLease(bitmap, status);
            return true;
        }

        public void Dispose()
        {
        }
    }
}
