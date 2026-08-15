using System.Drawing;
using System.Diagnostics;

namespace CuePilot.Tests;

public sealed class LockpickingDetectorTests
{
    public static IEnumerable<object[]> VisibleStates()
    {
        yield return ["numbered-ready-1.jpg", "Intermediate"];
        yield return ["numbered-approaching-2.jpg", "Numbered"];
        yield return ["numbered-ready-6.jpg", "Numbered"];
        yield return ["numbered-second.jpg", "Numbered"];
        yield return ["numbered-late.jpg", "Numbered"];
        yield return ["numbered-ten.jpg", "Numbered"];
        yield return ["spin-action.jpg", "Spin"];
        yield return ["transition-empty.jpg", "Intermediate"];
    }

    [Theory]
    [MemberData(nameof(VisibleStates))]
    public void VideoFramesResolveExpectedState(string fileName, string expected)
    {
        using var frame = LoadFixture(fileName);
        var observation = LockpickingDetector.Analyze(frame);

        Assert.True(
            string.Equals(expected, observation.State.ToString(), StringComparison.Ordinal),
            $"{fileName}: expected {expected}, actual {observation.State}; {observation.Reason}; evidence={LockpickingDetector.Inspect(frame)}");
        Assert.True(observation.Confidence >= 0.30, $"{fileName}: {observation}");
        Assert.InRange(observation.HudCenterX, 0.60, 0.80);
        Assert.InRange(observation.HudCenterY, 0.40, 0.60);
        Assert.InRange(observation.HudRadius, 0.16, 0.27);
    }

    [Fact]
    public void LiveSpinStartFrameResolvesSpinWithoutPriorHudHint()
    {
        using var frame = LoadFixture("live-spin-start.jpg");
        var observation = LockpickingDetector.Analyze(frame);

        Assert.True(
            observation.State == LockpickingVisualState.Spin,
            $"actual {observation.State}; {observation.Reason}; evidence={LockpickingDetector.Inspect(frame)}");
    }

    public static IEnumerable<object[]> ViewportLayouts()
    {
        yield return [1920, 1200, new Rectangle(-107, 0, 2133, 1200)];
        yield return [3440, 1440, new Rectangle(440, 0, 2560, 1440)];
        yield return [5120, 1440, new Rectangle(1280, 0, 2560, 1440)];
        yield return [1280, 1024, new Rectangle(-270, 0, 1820, 1024)];
    }

    [Theory]
    [MemberData(nameof(ViewportLayouts))]
    public void DetectorFindsHudAcrossWindowAndMonitorAspectRatios(
        int frameWidth,
        int frameHeight,
        Rectangle contentBounds)
    {
        using var source = LoadFixture("numbered-approaching-2.jpg");
        using var frame = ComposeFrame(source, frameWidth, frameHeight, contentBounds);

        var observation = LockpickingDetector.Analyze(frame);

        Assert.True(
            observation.State == LockpickingVisualState.Numbered,
            $"{frameWidth}x{frameHeight} content={contentBounds}: {observation}; evidence={LockpickingDetector.Inspect(frame)}");
        Assert.NotNull(observation.Target);
        Assert.True(observation.Confidence >= 0.30, observation.Reason);
    }

    [Fact]
    public void NormalGameplayFrameDoesNotAcquireHud()
    {
        using var frame = LoadFixture("hidden.jpg");
        var observation = LockpickingDetector.Analyze(frame);

        Assert.Equal(LockpickingVisualState.Hidden, observation.State);
        Assert.Equal("WAIT", observation.PredictedAction);
    }

    [Fact]
    public void SingleFrameNeverReportsReadyWithoutTemporalEvidence()
    {
        using var frame = LoadFixture("numbered-approaching-2.jpg");
        var observation = LockpickingDetector.Analyze(frame);

        Assert.NotNull(observation.Target);
        Assert.Equal(LockpickingTargetPhase.Approaching, observation.Target.Phase);
        Assert.Equal("WAIT", observation.PredictedAction);
    }

    [Fact]
    public void TemporalTrackerRequiresOuterRingAndInwardMotionBeforeReady()
    {
        var tracker = new LockpickingObservationTracker();

        var first = tracker.Track(Numbered(0.70, 0.50, 1.50), AtMilliseconds(1000), TimeSpan.Zero, 20);
        var second = tracker.Track(Numbered(0.70, 0.50, 1.40), AtMilliseconds(1033), TimeSpan.Zero, 20);
        var ready = tracker.Track(Numbered(0.70, 0.50, 1.24), AtMilliseconds(1066), TimeSpan.Zero, 20);

        Assert.Equal("VERIFY", first.PredictedAction);
        Assert.Equal("WAIT", second.PredictedAction);
        Assert.Equal(LockpickingTargetPhase.Ready, ready.Target?.Phase);
        Assert.Equal("CLICK (OBSERVE ONLY)", ready.PredictedAction);
        Assert.True(ready.Target?.RadialVelocity < 0);
    }

    [Fact]
    public void StaticTargetOutlineNeverBecomesReady()
    {
        var tracker = new LockpickingObservationTracker();

        tracker.Track(Numbered(0.70, 0.50, 1.22), AtMilliseconds(1000), TimeSpan.Zero, 20);
        tracker.Track(Numbered(0.70, 0.50, 1.22), AtMilliseconds(1033), TimeSpan.Zero, 20);
        var result = tracker.Track(Numbered(0.70, 0.50, 1.22), AtMilliseconds(1066), TimeSpan.Zero, 20);

        Assert.Equal(LockpickingTargetPhase.Approaching, result.Target?.Phase);
        Assert.Equal("WAIT", result.PredictedAction);
        Assert.Contains("distinct outer", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaleFrameWithholdsReadyPrediction()
    {
        var tracker = new LockpickingObservationTracker();

        tracker.Track(Numbered(0.70, 0.50, 1.50), AtMilliseconds(1000), TimeSpan.Zero, 20);
        tracker.Track(Numbered(0.70, 0.50, 1.40), AtMilliseconds(1033), TimeSpan.Zero, 20);
        var result = tracker.Track(Numbered(0.70, 0.50, 1.22), AtMilliseconds(1066), TimeSpan.FromMilliseconds(140), 20);

        Assert.Equal("WAIT", result.PredictedAction);
        Assert.Contains("old", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalHighRefreshFrameBatchDoesNotWithholdFreshReadyPrediction()
    {
        var tracker = new LockpickingObservationTracker();

        tracker.Track(Numbered(0.70, 0.50, 1.50), AtMilliseconds(1000), TimeSpan.Zero, 20);
        tracker.Track(Numbered(0.70, 0.50, 1.40), AtMilliseconds(1033), TimeSpan.Zero, 20);
        var result = tracker.Track(
            Numbered(0.70, 0.50, 1.22),
            AtMilliseconds(1066),
            TimeSpan.Zero,
            20,
            accumulatedFrames: 12);

        Assert.Equal("CLICK (OBSERVE ONLY)", result.PredictedAction);
    }

    [Fact]
    public void ExtremeAccumulatedCaptureFramesWithholdReadyPrediction()
    {
        var tracker = new LockpickingObservationTracker();
        tracker.Track(Numbered(0.70, 0.50, 1.50), AtMilliseconds(1000), TimeSpan.Zero, 20);
        tracker.Track(Numbered(0.70, 0.50, 1.40), AtMilliseconds(1033), TimeSpan.Zero, 20);

        var result = tracker.Track(
            Numbered(0.70, 0.50, 1.22),
            AtMilliseconds(1066),
            TimeSpan.Zero,
            20,
            accumulatedFrames: 40);

        Assert.Equal("WAIT", result.PredictedAction);
        Assert.Contains("skipped ahead", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StableTargetBrightFillIsReadyWithoutInferredOuterRing()
    {
        var tracker = new LockpickingObservationTracker();
        tracker.Track(Numbered(0.70, 0.50, 1.22), AtMilliseconds(1000), TimeSpan.Zero, 20);
        tracker.Track(Numbered(0.70, 0.50, 1.22), AtMilliseconds(1033), TimeSpan.Zero, 20);

        var result = tracker.Track(Numbered(0.70, 0.50, 1.22, fillDensity: 0.34), AtMilliseconds(1066), TimeSpan.Zero, 20);

        Assert.Equal(LockpickingTargetPhase.Ready, result.Target?.Phase);
        Assert.Equal("CLICK (OBSERVE ONLY)", result.PredictedAction);
        Assert.Contains("bright-green", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SequenceNumberRequiresTwoMatchingFramesAtNewCenter()
    {
        var tracker = new LockpickingObservationTracker();
        tracker.Track(Numbered(0.70, 0.50, 1.50), AtMilliseconds(1000), TimeSpan.Zero, 20);
        var firstCommitted = tracker.Track(Numbered(0.70, 0.50, 1.40), AtMilliseconds(1033), TimeSpan.Zero, 20);

        var candidate = tracker.Track(Numbered(0.62, 0.44, 1.50), AtMilliseconds(1066), TimeSpan.Zero, 20);
        var secondCommitted = tracker.Track(Numbered(0.62, 0.44, 1.40), AtMilliseconds(1099), TimeSpan.Zero, 20);

        Assert.Equal(1, firstCommitted.Target?.Number);
        Assert.Equal("VERIFY", candidate.PredictedAction);
        Assert.Equal(2, secondCommitted.Target?.Number);
    }

    [Fact]
    public void SpinTrackerMeasuresClockwiseTravelAcrossAngleWrap()
    {
        var tracker = new LockpickingSpinTracker();
        var bounds = new Rectangle(100, 200, 1000, 1000);
        var observation = SpinObservation();

        tracker.Track(observation, true, CursorAt(bounds, 350), bounds, AtMilliseconds(1000));
        var result = tracker.Track(observation, true, CursorAt(bounds, 10), bounds, AtMilliseconds(1050));

        Assert.NotNull(result);
        Assert.True(result.AngularVelocityDegreesPerSecond > 0);
        Assert.InRange(result.ClockwiseTravelDegrees, 18, 22);
        Assert.InRange(result.RadiusRatio, 0.95, 1.05);
        Assert.InRange(result.CursorX, 0.69, 0.70);
        Assert.InRange(result.CursorY, 0.53, 0.54);
        Assert.InRange(result.ElapsedMilliseconds, 49, 51);
    }

    [Fact]
    public void SpinTrackerResetsOutsideSpinState()
    {
        var tracker = new LockpickingSpinTracker();
        var bounds = new Rectangle(0, 0, 1000, 1000);
        tracker.Track(SpinObservation(), true, CursorAt(bounds, 90), bounds, AtMilliseconds(1000));

        var hidden = tracker.Track(LockpickingObservation.Hidden(), true, CursorAt(bounds, 100), bounds, AtMilliseconds(1050));
        var restarted = tracker.Track(SpinObservation(), true, CursorAt(bounds, 110), bounds, AtMilliseconds(1100));

        Assert.Null(hidden);
        Assert.NotNull(restarted);
        Assert.Equal(0, restarted.AngularVelocityDegreesPerSecond);
        Assert.Equal(0, restarted.ClockwiseTravelDegrees);
    }

    [Fact]
    public void AbsoluteCursorCoordinatesCoverTheVirtualDesktop()
    {
        Assert.Equal(new Point(0, 0), InputSender.NormalizeAbsolute(-1920, 0, -1920, 0, 3840, 1080));
        Assert.Equal(new Point(65535, 65535), InputSender.NormalizeAbsolute(1919, 1079, -1920, 0, 3840, 1080));

        var center = InputSender.NormalizeAbsolute(0, 540, -1920, 0, 3840, 1080);
        Assert.InRange(center.X, 32775, 32785);
        Assert.InRange(center.Y, 32795, 32805);
    }

    [Fact]
    public void ClassCSpinPathMovesClockwiseAtTheRecordedCalibrationSpeed()
    {
        var center = new Point(500, 400);
        var radius = 100d;

        var profile = LockpickingClassProfiles.ClassC;
        Assert.Equal(new Point(600, 400), LockpickingClassController.SpinPoint(center, radius, 0, 0, profile.SpinDegreesPerSecond));
        Assert.Equal(new Point(500, 500), LockpickingClassController.SpinPoint(
            center,
            radius,
            0,
            90d / profile.SpinDegreesPerSecond,
            profile.SpinDegreesPerSecond));
        Assert.Equal(new Point(600, 400), LockpickingClassController.SpinPoint(
            center,
            radius,
            0,
            360d / profile.SpinDegreesPerSecond,
            profile.SpinDegreesPerSecond));
    }

    [Fact]
    public void OnlyEvidenceBackedVehicleClassesCanResolveAnInputProfile()
    {
        Assert.True(LockpickingClassProfiles.TryGet("C", out var classC));
        Assert.Equal("C", classC.VehicleClass);
        Assert.False(LockpickingClassProfiles.TryGet("A", out _));
        Assert.False(LockpickingClassProfiles.TryGet("B", out _));
        Assert.False(LockpickingClassProfiles.TryGet("D", out _));
    }

    [Fact]
    public async Task ClassCControllerClicksEachVerifiedTargetOnlyOnce()
    {
        var input = new RecordingLockpickingInputDriver();
        using var controller = new LockpickingClassController(
            new WindowTargetSettings { ProcessName = "FiveM" },
            LockpickingClassProfiles.ClassC,
            input);
        var bounds = new Rectangle(100, 200, 1000, 800);
        var ready = ReadyTarget(1, 0.7, 0.5);

        var first = await controller.HandleAsync(ready, bounds, CancellationToken.None);
        var duplicate = await controller.HandleAsync(ready, bounds, CancellationToken.None);

        Assert.Equal("CLICKED TARGET 1", first.PredictedAction);
        Assert.Equal(1, first.ActionCount);
        Assert.Equal(1, duplicate.ActionCount);
        Assert.Equal([new Point(800, 600)], input.Moves);
        Assert.Equal([false, true], input.ButtonUps);
    }

    [Fact]
    public async Task ClassCControllerAcceptsMeasuredFastFollowUpConfidenceInSequence()
    {
        var input = new RecordingLockpickingInputDriver();
        using var controller = new LockpickingClassController(
            new WindowTargetSettings { ProcessName = "FiveM" },
            LockpickingClassProfiles.ClassC,
            input);
        var bounds = new Rectangle(100, 200, 1000, 800);

        var first = await controller.HandleAsync(ReadyTarget(1, 0.7, 0.5), bounds, CancellationToken.None);
        var second = await controller.HandleAsync(
            ReadyTarget(2, 0.66, 0.58) with
            {
                Confidence = 0.453,
                Target = ReadyTarget(2, 0.66, 0.58).Target! with { Confidence = 0.497 },
            },
            bounds,
            CancellationToken.None);

        Assert.Equal("CLICKED TARGET 1", first.PredictedAction);
        Assert.Equal("CLICKED TARGET 2", second.PredictedAction);
        Assert.Equal(2, second.ActionCount);
        Assert.Equal(2, input.Moves.Count);
    }

    [Fact]
    public async Task ClassCControllerAcceptsRecordedTemporalReadyForFirstTarget()
    {
        var input = new RecordingLockpickingInputDriver();
        using var controller = new LockpickingClassController(
            new WindowTargetSettings { ProcessName = "FiveM" },
            LockpickingClassProfiles.ClassC,
            input);
        var bounds = new Rectangle(0, 0, 2560, 1440);
        var recordedReady = ReadyTarget(1, 0.71602, 0.3875) with
        {
            Confidence = 0.594,
            Target = ReadyTarget(1, 0.71602, 0.3875).Target! with
            {
                Confidence = 0.510,
                RadialVelocity = -0.2,
                FillDensity = 0.141,
            },
        };

        var result = await controller.HandleAsync(recordedReady, bounds, CancellationToken.None);

        Assert.Equal("CLICKED TARGET 1", result.PredictedAction);
        Assert.Equal(new Point(1833, 558), Assert.Single(input.Moves));
        Assert.Equal([false, true], input.ButtonUps);
    }

    public static IEnumerable<object[]> WindowLayouts()
    {
        yield return [new Rectangle(0, 0, 1920, 1080), new Point(1344, 540)];
        yield return [new Rectangle(0, 0, 3440, 1440), new Point(2408, 720)];
        yield return [new Rectangle(-5120, 0, 5120, 1440), new Point(-1536, 720)];
        yield return [new Rectangle(320, 180, 1600, 1000), new Point(1440, 680)];
    }

    [Theory]
    [MemberData(nameof(WindowLayouts))]
    public async Task ClassCControllerMapsNormalizedTargetsAcrossWindowLayouts(
        Rectangle bounds,
        Point expectedScreenPoint)
    {
        var input = new RecordingLockpickingInputDriver();
        using var controller = new LockpickingClassController(
            new WindowTargetSettings { ProcessName = "FiveM" },
            LockpickingClassProfiles.ClassC,
            input);

        var result = await controller.HandleAsync(
            ReadyTarget(1, 0.70, 0.50),
            bounds,
            CancellationToken.None);

        Assert.Equal("CLICKED TARGET 1", result.PredictedAction);
        Assert.Equal(expectedScreenPoint, Assert.Single(input.Moves));
        Assert.Equal([false, true], input.ButtonUps);
    }

    [Fact]
    public async Task ClassCControllerNeverSkipsAnExpectedTarget()
    {
        var input = new RecordingLockpickingInputDriver();
        using var controller = new LockpickingClassController(
            new WindowTargetSettings { ProcessName = "FiveM" },
            LockpickingClassProfiles.ClassC,
            input);
        var bounds = new Rectangle(100, 200, 1000, 800);

        await controller.HandleAsync(ReadyTarget(1, 0.7, 0.5), bounds, CancellationToken.None);
        var skipped = await controller.HandleAsync(ReadyTarget(3, 0.75, 0.6), bounds, CancellationToken.None);

        Assert.Equal(1, skipped.ActionCount);
        Assert.Single(input.Moves);
    }

    [Fact]
    public async Task ClassCControllerRejectsReadyPointOutsideTheDetectedHud()
    {
        var input = new RecordingLockpickingInputDriver();
        using var controller = new LockpickingClassController(
            new WindowTargetSettings { ProcessName = "FiveM" },
            LockpickingClassProfiles.ClassC,
            input);
        var bounds = new Rectangle(0, 0, 2560, 1440);
        var outsideHud = ReadyTarget(1, 0.98, 0.05);

        var result = await controller.HandleAsync(outsideHud, bounds, CancellationToken.None);

        Assert.Equal(0, result.ActionCount);
        Assert.Empty(input.Moves);
        Assert.Empty(input.ButtonUps);
    }

    private static LockpickingObservation Numbered(
        double x,
        double y,
        double approachRatio,
        double fillDensity = 0) => new(
        LockpickingVisualState.Numbered,
        0.9,
        0.7,
        0.5,
        0.21,
        new LockpickingTargetObservation(
            x,
            y,
            0.21 * 0.13 * approachRatio,
            LockpickingTargetPhase.Approaching,
            0.85,
            ApproachRatio: approachRatio,
            FillDensity: fillDensity),
        4,
        "WAIT",
        "Raw detector observation.");

    private static LockpickingObservation SpinObservation() => new(
        LockpickingVisualState.Spin,
        0.8,
        0.5,
        0.5,
        0.2,
        null,
        0,
        "GATED",
        "SPIN calibration.");

    private static LockpickingObservation ReadyTarget(int number, double x, double y) => new(
        LockpickingVisualState.Numbered,
        0.95,
        0.7,
        0.5,
        0.21,
        new LockpickingTargetObservation(
            x,
            y,
            0.025,
            LockpickingTargetPhase.Ready,
            0.95,
            number,
            ApproachRatio: 1.2,
            FillDensity: 0.35),
        4,
        "CLICK (OBSERVE ONLY)",
        "Verified temporal READY state.");

    private static NativeMethods.CursorPoint CursorAt(Rectangle bounds, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180d;
        const double radius = 200;
        return new NativeMethods.CursorPoint
        {
            X = bounds.Left + bounds.Width / 2 + (int)Math.Round(Math.Cos(radians) * radius),
            Y = bounds.Top + bounds.Height / 2 + (int)Math.Round(Math.Sin(radians) * radius),
        };
    }

    private static long AtMilliseconds(double milliseconds) =>
        (long)(Stopwatch.Frequency * milliseconds / 1000d);

    private static Bitmap LoadFixture(string fileName) => new(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Lockpicking", fileName));

    private static Bitmap ComposeFrame(Bitmap source, int width, int height, Rectangle contentBounds)
    {
        var frame = new Bitmap(width, height);
        using var background = LoadFixture("hidden.jpg");
        using var graphics = Graphics.FromImage(frame);
        // Model native game rendering at the requested resolution without
        // introducing a second screenshot-resampling blur into the fixture.
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(background, new Rectangle(0, 0, width, height));
        graphics.DrawImage(source, contentBounds);
        return frame;
    }

    private sealed class RecordingLockpickingInputDriver : ILockpickingInputDriver
    {
        internal List<Point> Moves { get; } = [];
        internal List<bool> ButtonUps { get; } = [];

        public void MoveCursor(WindowTargetSettings target, int screenX, int screenY) =>
            Moves.Add(new Point(screenX, screenY));

        public void SendLeftButton(WindowTargetSettings target, bool up) => ButtonUps.Add(up);
    }
}
