using System.Drawing;

namespace CuePilot.Tests;

public sealed class PickpocketTargetTrackerTests
{
    private static readonly PickpocketBand Green = new(PickpocketBandColor.PaleGreen, 200, 270);
    private static readonly PickpocketBand Phantom = new(PickpocketBandColor.White, 350, 520);
    private static readonly PickpocketBand Purple = new(PickpocketBandColor.Purple, 90, 104);
    private static readonly PickpocketBand Blue = new(PickpocketBandColor.Blue, 300, 330);
    private static PickpocketObservation Frame(double marker, params PickpocketBand[] bands) =>
        new(PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), marker, bands, 1, "Fixture");

    [Fact]
    public void CustomOrderControlsSelectionPromotionAndExclusions()
    {
        PickpocketBandColor[] order = [PickpocketBandColor.Purple, PickpocketBandColor.PaleGreen, PickpocketBandColor.Yellow];
        var yellow = new PickpocketBand(PickpocketBandColor.Yellow, 420, 423);
        var red = new PickpocketBand(PickpocketBandColor.Red, 440, 444);
        var tracker = new PickpocketTargetTracker();
        for (var i = 1; i <= 4; i++) tracker.Observe(Frame(i * 5, red, yellow, Green, Phantom), "Custom", i * 16, order);
        Assert.Equal(Green, tracker.Chosen);
        for (var i = 5; i <= 7; i++) tracker.Observe(Frame(i * 5, yellow, Purple, Green), "Custom", i * 16, order);
        Assert.Equal(Purple, tracker.Chosen);
        Assert.Null(PickpocketObserverEngine.SelectBand(Frame(0, red, Blue, Phantom), "Custom", customPriority: order));
    }

    [Fact]
    public void RarestFirstUsesRequestedOrderForEveryColorCombinationInBothDirections()
    {
        PickpocketBand[] order = [new(PickpocketBandColor.Yellow, 70, 73), new(PickpocketBandColor.Red, 440, 444), Purple, Blue, Phantom];
        for (var mask = 1; mask < 32; mask++)
        foreach (var direction in new[] { -1, 1 })
        {
            var available = order.Where((_, index) => (mask & (1 << index)) != 0).ToArray();
            var bands = available.Append(Green).ToList();
            var tracker = new PickpocketTargetTracker();
            for (var i = 1; i <= 4; i++)
            {
                bands.Reverse();
                tracker.Observe(Frame(180 + direction * i * 5, bands.ToArray()), "RarestFirst", i * 16);
            }
            Assert.Equal(available[0], tracker.Chosen);
        }
    }

    [Fact]
    public void RarestFirstPromotesRedThenYellowAndIgnoresAOneFrameYellowFlash()
    {
        var tracker = new PickpocketTargetTracker();
        var red = new PickpocketBand(PickpocketBandColor.Red, 420, 424);
        var yellow = new PickpocketBand(PickpocketBandColor.Yellow, 70, 73);
        for (var i = 1; i <= 4; i++) tracker.Observe(Frame(120 + i * 5, Purple, Blue, Phantom), "RarestFirst", i * 16);
        Assert.Equal(Purple, tracker.Chosen);
        for (var i = 5; i <= 7; i++) tracker.Observe(Frame(120 + i * 5, red, Purple, Blue, Phantom), "RarestFirst", i * 16);
        Assert.Equal(red, tracker.Chosen);
        Assert.Null(tracker.Observe(Frame(160, yellow, red, Purple), "RarestFirst", 128));
        Assert.Equal(0, tracker.Observe(Frame(165, red, Purple), "RarestFirst", 144));
        Assert.Equal(red, tracker.Chosen);
        for (var i = 10; i <= 12; i++) tracker.Observe(Frame(120 + i * 5, red, yellow, Purple), "RarestFirst", i * 16);
        Assert.Equal(yellow, tracker.Chosen); // Behind the marker: wait for its return.
        for (var i = 13; i <= 16; i++) Assert.Null(tracker.Observe(Frame(71, red, Purple), "RarestFirst", i * 16));
        Assert.Equal(yellow, tracker.Chosen); // Marker occlusion must not downgrade.
    }

    [Fact]
    public void RarestFirstSelectsWidestOfSameRarityAndDoesNotSubstituteGreen()
    {
        var narrow = new PickpocketBand(PickpocketBandColor.Yellow, 70, 72);
        var wider = new PickpocketBand(PickpocketBandColor.Yellow, 220, 224);
        Assert.Equal(2, PickpocketObserverEngine.SelectBand(Frame(300, narrow, Phantom, wider), "RarestFirst"));
        Assert.Null(PickpocketObserverEngine.SelectBand(Frame(100, Green), "RarestFirst"));
    }

    [Theory]
    [InlineData(true, true, "Purple")]
    [InlineData(false, true, "Blue")]
    [InlineData(false, false, "White")]
    public void PriorityUsesPresenceBeforeWidthPositionOrArrayOrder(bool purple, bool blue, string expected)
    {
        var bands = new List<PickpocketBand> { Phantom, Green, new(PickpocketBandColor.Red, 540, 544) };
        if (blue) bands.Add(Blue);
        if (purple) bands.Add(Purple);
        foreach (var direction in new[] { -1, 1 })
        {
            var tracker = new PickpocketTargetTracker();
            for (var i = 1; i <= 4; i++)
            {
                bands.Reverse();
                tracker.Observe(Frame(180 + direction * i * 5, bands.ToArray()), "PurpleBlueWhite", i * 16);
            }
            Assert.Equal(expected, tracker.Chosen!.Color.ToString());
        }
    }

    [Fact]
    public void PriorityWaitsForReturnAndKeepsPurpleThroughOcclusion()
    {
        var tracker = new PickpocketTargetTracker();
        for (var i = 1; i <= 4; i++) tracker.Observe(Frame(120 + i * 5, Blue, Purple, Phantom), "PurpleBlueWhite", i * 16);
        Assert.Equal(Purple, tracker.Chosen); // Purple is behind the rightward marker.
        for (var i = 5; i <= 9; i++) Assert.Null(tracker.Observe(Frame(99, Blue, Phantom), "PurpleBlueWhite", i * 16));
        Assert.Equal(Purple, tracker.Chosen);
        Assert.Equal(2, tracker.Observe(Frame(85, Phantom, Blue, Purple), "PurpleBlueWhite", 160));
    }

    [Fact]
    public void PriorityIgnoresPreparingAndRequiresStableActiveFallback()
    {
        var tracker = new PickpocketTargetTracker();
        tracker.Observe(Frame(0, Purple) with { State = PickpocketVisualState.Preparing }, "PurpleBlueWhite", 0);
        for (var i = 1; i <= 3; i++) Assert.Null(tracker.Observe(Frame(i * 5, Blue, Phantom), "PurpleBlueWhite", i * 16));
        Assert.Equal(0, tracker.Observe(Frame(20, Blue, Phantom), "PurpleBlueWhite", 64));
        Assert.Equal(Blue, tracker.Chosen);
    }

    [Fact]
    public void PriorityPromotesARevealedHigherColorOnlyAfterFreshConfirmation()
    {
        var tracker = new PickpocketTargetTracker();
        for (var i = 1; i <= 4; i++) tracker.Observe(Frame(i * 5, Blue, Phantom), "PurpleBlueWhite", i * 16);
        Assert.Equal(Blue, tracker.Chosen);
        var revision = tracker.Revision;
        Assert.Null(tracker.Observe(Frame(25, Purple, Blue, Phantom), "PurpleBlueWhite", 80));
        Assert.Equal(Blue, tracker.Chosen);
        Assert.Null(tracker.Observe(Frame(30, Phantom, Purple, Blue), "PurpleBlueWhite", 96));
        Assert.Equal(2, tracker.Observe(Frame(35, Blue, Phantom, Purple), "PurpleBlueWhite", 112));
        Assert.Equal(Purple, tracker.Chosen);
        Assert.Equal(revision + 1, tracker.Revision);
    }

    [Fact]
    public void PriorityPromotionRejectsOneFrameFlickerAndRepeatedTimestamps()
    {
        var tracker = new PickpocketTargetTracker();
        for (var i = 1; i <= 4; i++) tracker.Observe(Frame(i * 5, Blue, Phantom), "PurpleBlueWhite", i * 16);
        Assert.Null(tracker.Observe(Frame(25, Purple, Blue), "PurpleBlueWhite", 80));
        Assert.Equal(0, tracker.Observe(Frame(30, Blue, Phantom), "PurpleBlueWhite", 96));
        for (var i = 0; i < 4; i++) Assert.Null(tracker.Observe(Frame(35 + i, Purple, Blue), "PurpleBlueWhite", 112));
        Assert.Equal(Blue, tracker.Chosen);
    }

    [Fact]
    public void PriorityDoesNotSubstituteColorsOutsideTheRequestedChain()
    {
        var tracker = new PickpocketTargetTracker();
        for (var i = 1; i <= 8; i++) Assert.Null(tracker.Observe(Frame(i * 5, Green,
            new(PickpocketBandColor.Red, 300, 304), new(PickpocketBandColor.Yellow, 400, 403)), "PurpleBlueWhite", i * 16));
        Assert.Null(tracker.Chosen);
    }

    [Fact]
    public void PreparationPixelsAndRepeatedTimestampsCannotConfirmATarget()
    {
        var tracker = new PickpocketTargetTracker();
        Assert.Null(tracker.Observe(Frame(0, Green, Phantom) with { State = PickpocketVisualState.Preparing }, "Widest", 0));
        for (var i = 0; i < 5; i++) Assert.Null(tracker.Observe(Frame(10 + i, Green), "Widest", 16));
        Assert.Null(tracker.Chosen);
        for (var i = 2; i <= 4; i++) tracker.Observe(Frame(10 + i * 5, Green), "Widest", i * 16);
        Assert.Equal(Green, tracker.Chosen);
    }

    [Fact]
    public void VanishedTargetReacquiresAfterConfirmationWithoutInheritingItsOldSlot()
    {
        var tracker = new PickpocketTargetTracker();
        for (var i = 1; i <= 4; i++) tracker.Observe(Frame(i * 5, Green, Phantom), "Widest", i * 16);
        Assert.Equal(Phantom, tracker.Chosen);
        // The broad false target disappears while the marker is nowhere near it.
        Assert.Null(tracker.Observe(Frame(25, Green), "Widest", 80));
        Assert.Equal(Phantom, tracker.Chosen);
        for (var i = 6; i <= 9; i++) tracker.Observe(Frame(i * 5, Green), "Widest", i * 16);
        Assert.Equal(Green, tracker.Chosen);
    }

    [Fact]
    public void OcclusionDoesNotTransferChoiceAndPreferredColorDoesNotFallBack()
    {
        var tracker = new PickpocketTargetTracker();
        for (var i = 1; i <= 4; i++) tracker.Observe(Frame(i * 5, Green, Phantom), "White", i * 16);
        for (var i = 5; i <= 9; i++) Assert.Null(tracker.Observe(Frame(380 + i, Green), "White", i * 16));
        Assert.Equal(Phantom, tracker.Chosen);
        for (var i = 10; i <= 17; i++) Assert.Null(tracker.Observe(Frame(100 + i, Green), "White", i * 16));
        Assert.Null(tracker.Chosen);
    }

    [Fact]
    public void RecordedFadeInHasFalseWhiteButActivePixelReaderConfirmsGreen()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "Live");
        using var preparing = new Bitmap(Path.Combine(folder, "fourth-21.png"));
        using var active = new Bitmap(Path.Combine(folder, "fourth-25.png"));
        var prep = PickpocketDetector.Analyze(preparing);
        var play = PickpocketDetector.Analyze(active);
        Assert.Equal(PickpocketVisualState.Preparing, prep.State);
        Assert.Contains(prep.Bands, b => b.Color == PickpocketBandColor.White && b.Width > 100);
        Assert.Equal(PickpocketVisualState.Active, play.State);
        Assert.DoesNotContain(play.Bands, b => b.Color == PickpocketBandColor.White);
        var tracker = new PickpocketTargetTracker();
        tracker.Observe(prep, "Widest", 0);
        for (var i = 1; i <= 4; i++) tracker.Observe(play with { MarkerX = play.MarkerX + i * 5 }, "Widest", i * 16);
        Assert.Equal(PickpocketBandColor.PaleGreen, tracker.Chosen!.Color);
    }
}
