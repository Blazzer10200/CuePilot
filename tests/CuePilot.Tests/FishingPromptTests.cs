using System.Drawing;
using System.Drawing.Imaging;

namespace CuePilot.Tests;

public sealed class FishingPromptTests
{
    [Fact]
    public async Task RoutineWorkRunsOffTheUiCallingThread()
    {
        var previousContext = SynchronizationContext.Current;
        var uiContext = new SynchronizationContext();
        SynchronizationContext? workerContext = uiContext;
        SynchronizationContext.SetSynchronizationContext(uiContext);

        try
        {
            await RoutineWorker.Start(() =>
            {
                workerContext = SynchronizationContext.Current;
                return Task.CompletedTask;
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        Assert.Null(workerContext);
    }

    [Theory]
    [InlineData("cast-ready.png")]
    public void CastReferenceIsRecognizedWithoutGuessing(string name)
    {
        using var bitmap = LoadFixture(name);

        var observation = FishingPromptDetector.Analyze(bitmap);

        Assert.Equal(FishingPromptKind.Cast, observation.Kind);
        Assert.True(observation.Confidence >= 0.75);
    }

    [Fact]
    public void PromptEvidenceExplainsItsFinalDecision()
    {
        using var bitmap = LoadFixture("cast-ready.png");

        var observation = FishingPromptDetector.Analyze(bitmap, out var evidence);

        Assert.Equal(FishingPromptKind.Cast, observation.Kind);
        Assert.Contains("Accepted Cast", evidence.DecisionReason);
    }

    [Fact]
    public void ReadyStateNeedsBothCastAndStopSignals()
    {
        using var bitmap = LoadFixture("cast-ready.png");

        var state = FishingPromptDetector.AnalyzeHudState(bitmap, out var evidence);

        Assert.Equal(FishingHudState.Ready, state.State);
        Assert.True(state.Confidence >= 0.70, $"{state}{Environment.NewLine}{evidence}");
        Assert.Equal(FishingPromptKind.Cast, state.Prompt.Kind);
        Assert.True(evidence.StopScore >= 0.65, evidence.ToString());
    }

    [Fact]
    public void WaitingStateRecognizesStopWithoutCast()
    {
        using var source = LoadFixture("cast-ready.png");
        using var prompt = new Bitmap(220, 68);
        using (var graphics = Graphics.FromImage(prompt))
        {
            graphics.Clear(Color.FromArgb(15, 20, 23));
            graphics.DrawImage(source, new Rectangle(0, 0, 170, 44), new Rectangle(270, 39, 170, 44), GraphicsUnit.Pixel);
        }

        var state = FishingPromptDetector.AnalyzeHudState(prompt, out var evidence);

        Assert.Equal(FishingHudState.Waiting, state.State);
        Assert.True(state.Confidence >= 0.65, $"{state}{Environment.NewLine}{evidence}");
        Assert.Equal(FishingPromptKind.None, state.Prompt.Kind);
    }

    [Fact]
    public void DecisionStateNeedsReleaseAndKeepSignals()
    {
        using var bitmap = LoadFixture("collect-ready.png");

        var state = FishingPromptDetector.AnalyzeHudState(bitmap, out var evidence);

        Assert.Equal(FishingHudState.Decision, state.State);
        Assert.True(state.Confidence >= 0.70, $"{state}{Environment.NewLine}{evidence}");
        Assert.Equal(FishingPromptKind.Collect, state.Prompt.Kind);
        Assert.True(evidence.ReleaseScore >= 0.65, evidence.ToString());
        Assert.True(evidence.KeepScore >= 0.65, evidence.ToString());
    }

    [Fact]
    public void ResultStateUsesCatchCardDecisionSignalsWithoutMatchingFishDetails()
    {
        using var bitmap = LoadFixture("catch-card.png");

        var state = FishingPromptDetector.AnalyzeHudState(bitmap, out var evidence);

        Assert.Equal(FishingHudState.Result, state.State);
        Assert.True(state.Confidence >= 0.70, $"{state}{Environment.NewLine}{evidence}");
        Assert.Equal(FishingPromptKind.Collect, state.Prompt.Kind);
        Assert.True(evidence.ReleaseScore >= 0.65, evidence.ToString());
        Assert.True(evidence.CatchKeepScore >= 0.65, evidence.ToString());
    }

    [Fact]
    public void CastingStateReusesTheVerifiedLmbMeterPath()
    {
        using var bitmap = LoadFishingFixture("active.png");

        var state = FishingPromptDetector.AnalyzeHudState(bitmap, out var evidence);

        Assert.Equal(FishingHudState.Casting, state.State);
        Assert.True(state.Confidence >= 0.65, $"{state}{Environment.NewLine}{evidence}");
        Assert.Equal(FishingPromptKind.None, state.Prompt.Kind);
    }

    [Theory]
    [InlineData("cast-ready.png", "Cast", "Ready")]
    [InlineData("collect-ready.png", "Collect", "Decision")]
    public void PromptStatesSurviveBusyFoliageBackgrounds(
        string name,
        string expectedPrompt,
        string expectedState)
    {
        using var prompt = LoadFixture(name);
        using var frame = CreateFoliageFrame(prompt);

        var state = FishingPromptDetector.AnalyzeHudState(frame, out var evidence);

        Assert.Equal(expectedPrompt, state.Prompt.Kind.ToString());
        Assert.True(expectedState == state.State.ToString(), $"Expected {expectedState}; actual {state.State}{Environment.NewLine}{evidence}");
        Assert.True(state.Prompt.Confidence >= 0.65, $"{state}{Environment.NewLine}{evidence}");
    }

    [Theory]
    [InlineData("collect-ready.png")]
    [InlineData("catch-card.png")]
    public void CollectReferencesAreRecognizedWithoutGuessing(string name)
    {
        using var bitmap = LoadFixture(name);

        var observation = FishingPromptDetector.Analyze(bitmap);

        Assert.Equal(FishingPromptKind.Collect, observation.Kind);
        Assert.True(observation.Confidence >= 0.75);
    }

    [Fact]
    public void StandaloneKeepFishPromptIsRecognizedWithoutItsReleaseFishNeighbor()
    {
        using var source = LoadFixture("collect-ready.png");
        using var prompt = new Bitmap(200, 68);
        using (var graphics = Graphics.FromImage(prompt))
        {
            graphics.Clear(Color.FromArgb(15, 20, 23));
            graphics.DrawImage(source, new Rectangle(0, 0, 146, 44), new Rectangle(240, 12, 146, 44), GraphicsUnit.Pixel);
        }

        var observation = FishingPromptDetector.Analyze(prompt);

        Assert.Equal(FishingPromptKind.Collect, observation.Kind);
        Assert.True(observation.Confidence >= 0.65, observation.ToString());
    }

    [Fact]
    public void BlankFrameDoesNotTriggerAnInputPrompt()
    {
        using var bitmap = new Bitmap(640, 360);

        var observation = FishingPromptDetector.Analyze(bitmap);

        Assert.Equal(FishingPromptKind.None, observation.Kind);
        Assert.True(observation.Confidence < 0.5);
    }

    [Fact]
    public void PromptMustRemainStableBeforeInputIsAllowed()
    {
        var gate = new FishingPromptStabilityGate(FishingPromptKind.Collect);

        Assert.False(gate.Observe(new FishingPromptObservation(FishingPromptKind.Collect, 0.92)));
        Assert.False(gate.Observe(new FishingPromptObservation(FishingPromptKind.Cast, 0.91)));
        Assert.False(gate.Observe(new FishingPromptObservation(FishingPromptKind.Collect, 0.94)));
        Assert.True(gate.Observe(new FishingPromptObservation(FishingPromptKind.Collect, 0.95)));
    }

    [Fact]
    public void PromptStabilityToleratesOneMissedFrame()
    {
        var gate = new FishingPromptStabilityGate(FishingPromptKind.Cast);

        Assert.False(gate.Observe(new FishingPromptObservation(FishingPromptKind.Cast, 0.81)));
        Assert.False(gate.Observe(new FishingPromptObservation(FishingPromptKind.None, 0.63)));
        Assert.True(gate.Observe(new FishingPromptObservation(FishingPromptKind.Cast, 0.79)));
    }

    [Fact]
    public void VisibleMeterSuppressesAnOtherwiseActionablePrompt()
    {
        var prompt = new FishingPromptObservation(FishingPromptKind.Cast, 0.92);
        var meter = new FishingMeterObservation(true, 0.55, 0.18, false, 0.94);

        Assert.True(FishingPromptArbitration.ShouldSuppress(prompt, meter));
        Assert.False(FishingPromptArbitration.ShouldSuppress(prompt, FishingMeterObservation.Missing));
    }

    [Fact]
    public void VisibleMeterDoesNotSuppressAConfirmedCollectPrompt()
    {
        // The completed minigame can leave a meter-looking region behind the result panel,
        // particularly over bright reflective daytime water.  E Keep Fish must still win.
        var prompt = new FishingPromptObservation(FishingPromptKind.Collect, 0.99);
        var meter = new FishingMeterObservation(true, 0.55, 1.00, true, 0.94);

        Assert.False(FishingPromptArbitration.ShouldSuppress(prompt, meter));
    }

    [Fact]
    public void ConfirmedReadyCastOutranksAStaleTrackedMeter()
    {
        using var meterFrame = LoadFishingFixture("live-2560-real-meter-lock.png");
        using var castFrame = LoadFishingFixture("live-2560-ready-cast-after-collect.png");
        var tracker = new FishingMeterTracker();

        _ = FishingMeterService.AnalyzeFrameDetailed(meterFrame, tracker);
        _ = FishingMeterService.AnalyzeFrameDetailed(meterFrame, tracker);
        var staleMeter = FishingMeterService.AnalyzeFrameDetailed(castFrame, tracker).Observation;
        var prompt = FishingPromptDetector.Analyze(castFrame);

        Assert.True(staleMeter.IsVisible, "The captured regression frame must reproduce the stale meter lock.");
        Assert.Equal(FishingPromptKind.Cast, prompt.Kind);
        Assert.Equal(FishingHudState.Ready, prompt.State);
        Assert.True(prompt.StateConfidence >= 0.90, prompt.ToString());
        Assert.False(FishingPromptArbitration.ShouldSuppress(prompt, staleMeter));
    }

    [Fact]
    public void ReplayReportsTheCapturedReadyCastAsActionableDespiteTheStaleMeter()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fishing");
        var meter = Path.Combine(fixtureDirectory, "live-2560-real-meter-lock.png");
        var cast = Path.Combine(fixtureDirectory, "live-2560-ready-cast-after-collect.png");

        var report = FishingReplayService.Replay(new[] { meter, meter, cast });
        var castTransition = Assert.Single(report.Transitions, item => item.Prompt == FishingPromptKind.Cast);

        Assert.True(castTransition.MeterVisible);
        Assert.False(castTransition.PromptSuppressed);
        Assert.Equal(0, report.SuppressedPromptFrames);
    }

    [Fact]
    public void PromptGateCanBeResetAfterCrossDetectorSuppression()
    {
        var gate = new FishingPromptStabilityGate(FishingPromptKind.Cast);
        var cast = new FishingPromptObservation(FishingPromptKind.Cast, 0.9);

        Assert.False(gate.Observe(cast));
        gate.Reset();
        Assert.False(gate.Observe(cast));
        Assert.True(gate.Observe(cast));
    }

    [Fact]
    public void PromptClearNeedsStrongEvidenceInsteadOfTwoDetectorMisses()
    {
        var gate = new FishingPromptClearGate(FishingPromptKind.Cast);

        Assert.False(gate.Observe(new FishingPromptObservation(FishingPromptKind.None, 0.62)));
        Assert.False(gate.Observe(new FishingPromptObservation(FishingPromptKind.None, 0.61)));
        Assert.False(gate.Observe(new FishingPromptObservation(FishingPromptKind.Cast, 0.78)));
        Assert.False(gate.Observe(new FishingPromptObservation(FishingPromptKind.None, 0.60)));
        Assert.False(gate.Observe(new FishingPromptObservation(FishingPromptKind.None, 0.59)));
        Assert.True(gate.Observe(new FishingPromptObservation(FishingPromptKind.None, 0.58)));
    }

    [Fact]
    public void DifferentVerifiedPromptImmediatelyConfirmsThePreviousPromptCleared()
    {
        var gate = new FishingPromptClearGate(FishingPromptKind.Collect);

        Assert.True(gate.Observe(new FishingPromptObservation(FishingPromptKind.Cast, 0.82)));
    }

    [Fact]
    public void LiveMeterFrameIsRejectedQuicklyInsteadOfBecomingAFalseCast()
    {
        using var bitmap = LoadFixture("live-meter-active.png");
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var observation = FishingPromptDetector.Analyze(bitmap);

        Assert.True(observation.Kind == FishingPromptKind.None, observation.ToString());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Prompt rejection took {clock.Elapsed.TotalSeconds:F1} seconds.");
    }

    [Theory]
    [InlineData("video-day-initial.png")]
    [InlineData("video-day-active.png")]
    [InlineData("video-day-glare-miss.jpg")]
    [InlineData("video-day-late.png")]
    [InlineData("video-day-caught.jpg")]
    [InlineData("video-day-stop-prompt-lookalike.jpg")]
    public void DaylightMeterFramesDoNotBecomeInputPrompts(string name)
    {
        using var bitmap = LoadFishingFixture(name);

        var observation = FishingPromptDetector.Analyze(bitmap, out var evidence);

        Assert.True(observation.Kind == FishingPromptKind.None, $"{observation}{Environment.NewLine}{evidence}");
    }

    [Theory]
    [InlineData("live-cast-ready.png")]
    [InlineData("live-cast-gdi.png")]
    [InlineData("live-cast-day-blue-water.png")]
    [InlineData("live-cast-day-reflective-water.png")]
    [InlineData("live-cast-day-sky.png")]
    public void LiveBottomCenterCastFrameIsRecognized(string name)
    {
        using var bitmap = LoadFixture(name);

        var observation = FishingPromptDetector.Analyze(bitmap, out var evidence);

        Assert.True(observation.Kind == FishingPromptKind.Cast,
            $"{observation}{Environment.NewLine}{evidence}");
        Assert.True(observation.Confidence >= 0.65,
            $"{observation}{Environment.NewLine}{evidence}");
    }

    [Fact]
    public void BrightShorelineCastUsesLocalTextContrastInsteadOfWorldBrightness()
    {
        using var bitmap = LoadFixture("live-cast-day-blue-water.png");

        var observation = FishingPromptDetector.Analyze(bitmap, out var evidence);

        Assert.Equal(FishingPromptKind.Cast, observation.Kind);
        Assert.True(evidence.Cast.BackgroundScore < 0.05, evidence.ToString());
        Assert.True(evidence.Cast.ContrastScore >= 0.65, evidence.ToString());
        Assert.True(evidence.Cast.Score >= evidence.Collect.Score + 0.08, evidence.ToString());
    }

    [Fact]
    public void BrightBushAndReflectiveWaterReadyFrameIsRecognized()
    {
        using var bitmap = LoadFixture("bush-bright-reflection-ready.png");

        var observation = FishingPromptDetector.Analyze(bitmap, out var evidence);

        Assert.Equal(FishingPromptKind.Cast, observation.Kind);
        Assert.Equal(FishingHudState.Ready, observation.State);
        Assert.True(observation.Confidence >= 0.80, $"{observation}{Environment.NewLine}{evidence}");
        Assert.True(evidence.StopScore >= 0.80, evidence.ToString());
        Assert.True(evidence.Cast.ContrastScore >= 0.80, evidence.ToString());
    }

    [Theory]
    [InlineData("cast-ready.png", "Cast")]
    [InlineData("collect-ready.png", "Collect")]
    public void PromptIsFoundAtAnOddOffsetInsideALargerFrame(string name, string expectedName)
    {
        using var prompt = LoadFixture(name);
        using var frame = new Bitmap(900, 540);
        frame.SetResolution(prompt.HorizontalResolution, prompt.VerticalResolution);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(9, 14, 16));
            graphics.DrawImageUnscaled(prompt, 317, 367);
        }

        var observation = FishingPromptDetector.Analyze(frame);

        Assert.True(expectedName == observation.Kind.ToString(), observation.ToString());
        Assert.True(observation.Confidence >= 0.72);
    }

    [Theory]
    [InlineData("cast-ready.png", "Cast", 0.65f)]
    [InlineData("cast-ready.png", "Cast", 0.70f)]
    [InlineData("cast-ready.png", "Cast", 0.75f)]
    [InlineData("cast-ready.png", "Cast", 0.85f)]
    [InlineData("cast-ready.png", "Cast", 0.95f)]
    [InlineData("cast-ready.png", "Cast", 1.05f)]
    [InlineData("cast-ready.png", "Cast", 1.15f)]
    [InlineData("cast-ready.png", "Cast", 1.25f)]
    [InlineData("cast-ready.png", "Cast", 1.35f)]
    [InlineData("collect-ready.png", "Collect", 0.65f)]
    [InlineData("collect-ready.png", "Collect", 0.70f)]
    [InlineData("collect-ready.png", "Collect", 0.75f)]
    [InlineData("collect-ready.png", "Collect", 0.85f)]
    [InlineData("collect-ready.png", "Collect", 0.95f)]
    [InlineData("collect-ready.png", "Collect", 1.05f)]
    [InlineData("collect-ready.png", "Collect", 1.15f)]
    [InlineData("collect-ready.png", "Collect", 1.25f)]
    [InlineData("collect-ready.png", "Collect", 1.35f)]
    public void PromptIsRecognizedAcrossCommonUiScales(string name, string expectedName, float scale)
    {
        using var prompt = LoadFixture(name);
        using var frame = new Bitmap(1000, 620);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(9, 14, 16));
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(prompt, new Rectangle(320, 410,
                (int)Math.Round(prompt.Width * scale),
                (int)Math.Round(prompt.Height * scale)));
        }

        var observation = FishingPromptDetector.Analyze(frame, out var evidence);

        Assert.True(expectedName == observation.Kind.ToString(), $"{observation}{Environment.NewLine}{evidence}");
        Assert.True(observation.Confidence >= 0.65, $"{observation}{Environment.NewLine}{evidence}");
    }

    [Theory]
    [InlineData(2.0f)]
    [InlineData(3.0f)]
    [InlineData(4.0f)]
    public void KeepFishPromptIsRecognizedAtLargeUiScales(float scale)
    {
        using var prompt = LoadFixture("collect-ready.png");
        using var frame = new Bitmap(2_400, 1_400);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(9, 14, 16));
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(prompt, new Rectangle(800, 940,
                (int)Math.Round(prompt.Width * scale),
                (int)Math.Round(prompt.Height * scale)));
        }

        var observation = FishingPromptDetector.Analyze(frame, out var evidence);

        Assert.True(observation.Kind == FishingPromptKind.Collect,
            $"{observation}{Environment.NewLine}{evidence}");
        Assert.True(observation.Confidence >= 0.65, observation.ToString());
    }

    [Theory]
    [InlineData(3440, 1440, 440)]
    [InlineData(5120, 1440, 1280)]
    [InlineData(3840, 1440, 640)]
    [InlineData(3840, 1600, 498)]
    [InlineData(3840, 1080, 960)]
    public void CastPromptIsFoundInsideCenteredUltrawideSafeViewport(int frameWidth, int frameHeight, int safeLeft)
    {
        using var prompt = LoadFixture("cast-ready.png");
        var promptTop = frameHeight * 2 / 3;
        using var frame = new Bitmap(frameWidth, frameHeight);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(9, 14, 16));
            graphics.DrawImageUnscaled(prompt, safeLeft + 720, promptTop);
        }

        var observation = FishingPromptDetector.Analyze(frame, out var evidence);

        Assert.True(observation.Kind == FishingPromptKind.Cast,
            $"{frameWidth}x{frameHeight}: {observation}{Environment.NewLine}{evidence}");
        Assert.True(observation.Confidence >= 0.65, observation.ToString());
    }

    [Theory]
    [InlineData(3840, 1440, 940, 960)]
    [InlineData(3840, 1600, 1040, 1060)]
    [InlineData(3840, 1080, 1000, 700)]
    public void CastPromptIsFoundInFullWidthHudLayoutsAcrossLongDisplays(
        int frameWidth,
        int frameHeight,
        int promptLeft,
        int promptTop)
    {
        using var prompt = LoadFixture("cast-ready.png");
        using var frame = new Bitmap(frameWidth, frameHeight);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(9, 14, 16));
            graphics.DrawImageUnscaled(prompt, promptLeft, promptTop);
        }

        var observation = FishingPromptDetector.Analyze(frame, out var evidence);

        Assert.Equal(FishingPromptKind.Cast, observation.Kind);
        Assert.True(observation.Confidence >= 0.65,
            $"{frameWidth}x{frameHeight}: {observation}{Environment.NewLine}{evidence}");
    }

    [Fact]
    public void SuppliedUltrawidePreviewNeverBecomesAnActionablePrompt()
    {
        using var sharedPreview = LoadFishingFixture("live-ultrawide-waiting-preview.png");
        using var frame = new Bitmap(3440, 1440);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(
                sharedPreview,
                new Rectangle(0, 0, frame.Width, frame.Height),
                new Rectangle(0, 0, sharedPreview.Width, sharedPreview.Height),
                GraphicsUnit.Pixel);
        }

        var hud = FishingPromptDetector.AnalyzeHudState(frame, out _);

        Assert.Equal(FishingPromptKind.None, hud.Prompt.Kind);
    }

    [Fact]
    public void CastPromptIsFoundInAFullWidthUltrawideHudLayout()
    {
        using var prompt = LoadFixture("cast-ready.png");
        using var frame = new Bitmap(3440, 1440);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(9, 14, 16));
            graphics.DrawImage(
                prompt,
                new Rectangle(900, 960, prompt.Width, prompt.Height),
                new Rectangle(0, 0, prompt.Width, prompt.Height),
                GraphicsUnit.Pixel);
        }

        var observation = FishingPromptDetector.Analyze(frame, out var evidence);

        Assert.Equal(FishingPromptKind.Cast, observation.Kind);
        Assert.True(observation.Confidence >= 0.65, evidence.ToString());
    }

    [Theory]
    [InlineData(1920, 1200, -107, 0, 2133, 1200)]
    [InlineData(1280, 1024, -270, 0, 1820, 1024)]
    public void LiveCastPromptIsFoundOnCenterCroppedNarrowLayouts(
        int frameWidth,
        int frameHeight,
        int contentLeft,
        int contentTop,
        int contentWidth,
        int contentHeight)
    {
        using var source = LoadFixture("cast-ready.png");
        using var frame = new Bitmap(frameWidth, frameHeight);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(9, 14, 16));
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(
                contentLeft + (int)Math.Round(contentWidth * 0.40),
                contentTop + (int)Math.Round(contentHeight * 0.70),
                (int)Math.Round(source.Width * 0.75),
                (int)Math.Round(source.Height * 0.75)));
        }

        var observation = FishingPromptDetector.Analyze(frame, out var evidence);

        Assert.True(observation.Kind == FishingPromptKind.Cast,
            $"{frameWidth}x{frameHeight}: {observation}{Environment.NewLine}{evidence}");
        Assert.True(observation.Confidence >= 0.65,
            $"{frameWidth}x{frameHeight}: {observation}{Environment.NewLine}{evidence}");
    }

    private static Bitmap LoadFixture(string name) =>
        new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Prompts", name));

    private static Bitmap LoadFishingFixture(string name) =>
        new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fishing", name));

    // Deterministic high-detail green/brown luminance noise mimics foliage
    // crossing a translucent GTA HUD.  It exercises the real prompt detector
    // without binding correctness to one copied full-frame background.
    private static Bitmap CreateFoliageFrame(Bitmap prompt)
    {
        var frame = new Bitmap(1280, 720);
        var random = new Random(913);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(Color.FromArgb(22, 36, 22));
        for (var index = 0; index < 2_400; index++)
        {
            var green = random.Next(50, 180);
            var color = index % 11 == 0
                ? Color.FromArgb(random.Next(150, 235), random.Next(130, 210), random.Next(85, 150))
                : Color.FromArgb(random.Next(20, 85), green, random.Next(15, 75));
            using var brush = new SolidBrush(color);
            var width = random.Next(2, 12);
            var height = random.Next(2, 18);
            graphics.FillEllipse(brush, random.Next(frame.Width), random.Next(frame.Height), width, height);
        }

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.84f });
        var x = (frame.Width - prompt.Width) / 2;
        graphics.DrawImage(prompt, new Rectangle(x, 572, prompt.Width, prompt.Height), 0, 0,
            prompt.Width, prompt.Height, GraphicsUnit.Pixel, attributes);
        return frame;
    }
}
