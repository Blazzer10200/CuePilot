using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CuePilot.Tests;

public sealed class PickpocketSweepTests
{
    private static PickpocketObservation Frame(double marker) => new(PickpocketVisualState.Active,
        new Rectangle(0, 100, 576, 23), marker, [new(PickpocketBandColor.Red, 298, 302)], 1, "Sweep fixture");

    [Fact]
    public void FirstPassRequiresSubstantialTravelAndThreeReturnSamplesAtTheEdge()
    {
        var tracker = new PickpocketSweepTracker();
        for (var i = 0; i <= 92; i++)
        {
            tracker.Observe(Frame(20 + i * 6), i * 16);
            Assert.False(tracker.FirstPassComplete);
        }
        tracker.Observe(Frame(572), 93 * 16); // paused at the edge
        Assert.False(tracker.FirstPassComplete);
        for (var i = 1; i <= 3; i++)
        {
            tracker.Observe(Frame(572 - i * 6), (93 + i) * 16);
            Assert.Equal(i == 3, tracker.FirstPassComplete);
        }
        tracker.Observe(Frame(20) with { State = PickpocketVisualState.Preparing }, 2000);
        Assert.False(tracker.FirstPassComplete);
    }

    [Fact]
    public void MidBarZigzagAndSkippedFramesDoNotQualifyAsASurvey()
    {
        var tracker = new PickpocketSweepTracker();
        for (var i = 0; i <= 50; i++) tracker.Observe(Frame(20 + i * 6), i * 16);
        for (var i = 1; i <= 5; i++) tracker.Observe(Frame(320 - i * 6), (50 + i) * 16);
        Assert.False(tracker.FirstPassComplete);
        tracker = new();
        for (var i = 0; i <= 92; i++) tracker.Observe(Frame(20 + i * 6), i * 16);
        for (var i = 1; i <= 5; i++) tracker.Observe(Frame(572 - i * 6), 92 * 16 + 100 + i * 16);
        Assert.False(tracker.FirstPassComplete);
    }

    [Fact]
    public void RecordedTimedAttemptCompletesSurveyWithTimeLeftForReturnShots()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        var rows = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "Pickpocket", "Live", "third-attempt.json")), options)!;
        var tracker = new PickpocketSweepTracker();
        var activeStart = rows.First(s => s.Observation.State == PickpocketVisualState.Active).PresentationMilliseconds;
        double? surveyed = null;
        foreach (var row in rows)
        {
            tracker.Observe(row.Observation, row.PresentationMilliseconds);
            if (tracker.FirstPassComplete) { surveyed = row.PresentationMilliseconds; break; }
        }
        Assert.NotNull(surveyed);
        Assert.InRange(surveyed.Value - activeStart, 1000, 3000);
    }

    private sealed record Sample(double PresentationMilliseconds, PickpocketObservation Observation);
}
