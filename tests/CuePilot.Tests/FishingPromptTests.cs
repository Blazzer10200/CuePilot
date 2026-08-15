using System.Drawing;

namespace CuePilot.Tests;

public sealed class FishingPromptTests
{
    [Fact]
    public async Task RoutineWorkRunsOffTheUiCallingThread()
    {
        var callingThread = Environment.CurrentManagedThreadId;
        var workerThread = callingThread;

        await RoutineWorker.Start(() =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            return Task.CompletedTask;
        });

        Assert.NotEqual(callingThread, workerThread);
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
    [InlineData("cast-ready.png", "Cast", 0.75f)]
    [InlineData("cast-ready.png", "Cast", 0.85f)]
    [InlineData("cast-ready.png", "Cast", 0.95f)]
    [InlineData("cast-ready.png", "Cast", 1.05f)]
    [InlineData("cast-ready.png", "Cast", 1.15f)]
    [InlineData("cast-ready.png", "Cast", 1.25f)]
    [InlineData("cast-ready.png", "Cast", 1.35f)]
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
    [InlineData(3440, 440)]
    [InlineData(5120, 1280)]
    public void CastPromptIsFoundInsideCenteredUltrawideSafeViewport(int frameWidth, int safeLeft)
    {
        using var prompt = LoadFixture("cast-ready.png");
        using var frame = new Bitmap(frameWidth, 1440);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.FromArgb(9, 14, 16));
            graphics.DrawImageUnscaled(prompt, safeLeft + 720, 960);
        }

        var observation = FishingPromptDetector.Analyze(frame, out var evidence);

        Assert.True(observation.Kind == FishingPromptKind.Cast,
            $"{frameWidth}x1440: {observation}{Environment.NewLine}{evidence}");
        Assert.True(observation.Confidence >= 0.65, observation.ToString());
    }

    private static Bitmap LoadFixture(string name) =>
        new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Prompts", name));

    private static Bitmap LoadFishingFixture(string name) =>
        new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fishing", name));
}
