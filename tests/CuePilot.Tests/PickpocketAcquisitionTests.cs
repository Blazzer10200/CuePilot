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
