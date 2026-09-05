using System.Drawing;
using System.Diagnostics;

namespace CuePilot.Tests;

public sealed class PickpocketItemTests
{
    [Theory]
    [InlineData("clip-05.png", "Rope", "Luxury Watch", "Ruby", "Broken Electronic Part")]
    [InlineData("clip2-04.png", "Ring", "Cuff Medicine", "TNT Recipe", "Loose Change")]
    [InlineData("Live/automatic-ruby-185.png", "Rope", "Pocket Watch", "Ruby", "Lucky Charm")]
    [InlineData("Live/automatic-ring-96.png", "Lucky Charm", "Broken Hard Drive", "Ring", "Ruby")]
    [InlineData("Live/automatic-rope-101.png", "Broken Electronic Part", "Ruby", "Rope", "TNT Recipe")]
    [InlineData("Live/first-attempt.png", "Ruby", "Pocket Watch", "Broken Electronic Part", "Wrist Band")]
    public void HeldOutNativeCardsMatchNames(string file, params string[] expected)
    {
        using var bitmap = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", file));
        var observation = PickpocketDetector.Analyze(bitmap, new(0, 0, bitmap.Width, bitmap.Height));
        Assert.Equal(PickpocketVisualState.Active, observation.State);
        var names = observation.Bands.Select(b => PickpocketItemReader.Recognize(bitmap, observation.Bar, b)).ToArray();
        Assert.Equal(expected, names);
    }

    [Fact]
    public void ShuffledSameColorCardsAreConfirmedTwiceThenCachedOutsideTheTimingPath()
    {
        using var watchFrame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "clip-06.png"));
        using var ringFrame = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "clip2-05.png"));
        using var shuffled = new Bitmap(680, 300);
        using (var g = Graphics.FromImage(shuffled))
        {
            g.DrawImage(watchFrame, new Rectangle(398, 140, 104, 30), new Rectangle(224, 140, 104, 30), GraphicsUnit.Pixel);
            g.DrawImage(ringFrame, new Rectangle(88, 140, 104, 30), new Rectangle(96, 140, 104, 30), GraphicsUnit.Pixel);
        }
        var observation = new PickpocketObservation(PickpocketVisualState.Active, new Rectangle(52, 218, 576, 23), 200,
            [new(PickpocketBandColor.Purple, 126, 154), new(PickpocketBandColor.Purple, 442, 458)], 1, "Two purple cards, shuffled");
        var reader = new PickpocketItemReader();
        Assert.All(reader.Annotate(shuffled, observation, 0).Bands, b => Assert.Null(b.ItemName));
        var confirmed = reader.Annotate(shuffled, observation, 120);
        Assert.Equal(new[] { "Ring", "Luxury Watch" }, confirmed.Bands.Select(b => b.ItemName));
        Assert.True(reader.ReadyToChoose);
        using var empty = new Bitmap(2, 2);
        Assert.Equal(confirmed.Bands, reader.Annotate(empty, observation, 500).Bands);
        Assert.Equal(1, PickpocketObserverEngine.SelectBand(confirmed, "RarestFirst", itemPriority: ["Luxury Watch", "Ring"]));
        var tracker = new PickpocketTargetTracker();
        for (var i = 0; i < 4; i++) tracker.Observe(observation with { MarkerX = 200 + i * 5 }, "RarestFirst", i * 16, itemPriority: ["Luxury Watch", "Ring"]);
        Assert.Equal(140, tracker.Chosen!.Center); // Initially wider, unrecognized Ring.
        for (var i = 4; i < 7; i++) tracker.Observe(confirmed with { MarkerX = 200 + i * 5 }, "RarestFirst", i * 16, itemPriority: ["Luxury Watch", "Ring"]);
        Assert.Equal("Luxury Watch", tracker.Chosen!.ItemName);
        reader.Annotate(shuffled, observation with { State = PickpocketVisualState.Preparing }, 600);
        Assert.All(reader.Annotate(empty, observation, 616).Bands, b => Assert.Null(b.ItemName));
    }

    [Fact]
    public void ItemOrderBreaksColorTiesAndWaitsForPreferredPassedCard()
    {
        var observation = new PickpocketObservation(PickpocketVisualState.Active, new Rectangle(0, 200, 576, 23), 200,
            [new(PickpocketBandColor.Purple, 100, 116, "Luxury Watch"), new(PickpocketBandColor.Purple, 300, 330, "Ring")], 1, "Shuffled cards");
        Assert.Equal(0, PickpocketObserverEngine.SelectBand(observation, "RarestFirst", itemPriority: ["Luxury Watch", "Ring"]));
        Assert.Equal(1, PickpocketObserverEngine.SelectBand(observation, "RarestFirst", itemPriority: ["Ring", "Luxury Watch"]));
        Assert.Equal(1, PickpocketObserverEngine.SelectBand(observation with { Bands = observation.Bands.Select(b => b with { ItemName = null }).ToArray() }, "RarestFirst", itemPriority: ["Luxury Watch", "Ring"]));
        using var blank = new Bitmap(680, 300);
        Assert.Null(PickpocketItemReader.Recognize(blank, observation.Bar, observation.Bands[1]));
    }
}
