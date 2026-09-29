using System.Drawing;
using Xunit.Abstractions;

namespace CuePilot.Tests;

public sealed class PickpocketAcquisitionTests(ITestOutputHelper output)
{
    [Fact]
    public void AcquiresVisibleBarAgainstBrightGrassWithoutPreviousFrame()
    {
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "live-grass-acquisition.png"));
        var observation = PickpocketDetector.Analyze(frame);
        output.WriteLine(observation.ToString());
        Assert.Equal(PickpocketVisualState.Active, observation.State);
        Assert.InRange(observation.MarkerX, 666, 672);
        Assert.InRange(observation.Bar.Left, 510, 516);
        Assert.Contains(observation.Bands, band => band.Color == PickpocketBandColor.Yellow);
        Assert.Contains(observation.Bands, band => band.Color == PickpocketBandColor.Red);
    }

    // Grass plus white clothing yields 100+ marker-like stems. The wide pass used to spend
    // the whole header budget on the first of them, so the solid real marker was never checked.
    [Theory]
    [InlineData("busy-scene-acquisition-a.png", 582)]
    [InlineData("busy-scene-acquisition-b.png", 625)]
    public void BusySceneStillFindsThePanelFromColdStart(string file, int markerX)
    {
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", file));
        var observation = PickpocketDetector.Analyze(frame);
        output.WriteLine(observation.ToString());
        Assert.Equal(PickpocketVisualState.Active, observation.State);
        Assert.InRange(observation.MarkerX, markerX - 3, markerX + 3);
        Assert.NotEmpty(observation.Bands);
    }

    // Grass joins the marker stem (80 px instead of ~60), which inflates the length-based scale
    // estimate and moves the stem's middle off the bar row. Only the resolution scale finds it.
    [Fact]
    public void GrassInflatedStemStillFindsThePanelFromColdStart()
    {
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "busy-scene-inflated-stem.png"));
        var observation = PickpocketDetector.Analyze(frame);
        output.WriteLine(observation.ToString());
        Assert.Equal(PickpocketVisualState.Active, observation.State);
        Assert.InRange(observation.MarkerX, 805, 811);
        Assert.Equal(4, observation.Bands.Count);
    }

    [Fact]
    public void RecoveryStillRequiresTheStatusHeader()
    {
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "live-grass-acquisition.png"));
        using (var graphics = Graphics.FromImage(frame)) graphics.FillRectangle(Brushes.Black, 500, 70, 180, 45);
        Assert.Equal(PickpocketVisualState.Hidden, PickpocketDetector.Analyze(frame).State);
    }

    [Fact]
    public void SameGrassSceneWithoutMinigameRemainsHidden()
    {
        using var frame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "live-grass-hidden.png"));
        Assert.Equal(PickpocketVisualState.Hidden, PickpocketDetector.Analyze(frame).State);
    }
}
