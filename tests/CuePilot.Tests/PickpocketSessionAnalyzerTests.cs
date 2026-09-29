using System.Drawing;

namespace CuePilot.Tests;

public sealed class PickpocketSessionAnalyzerTests
{
    private static PickpocketSessionAnalyzer.Sample Sample(int index, double ms, string visual, string engine = "Tracking", string reason = "",
        string detail = "Waiting for a new preparation state before automatic input.") =>
        new(index, ms, engine, visual, reason, visual == "Active" ? .9 : 0, [], 100, new Rectangle(10, 10, 500, 20), 16, detail, "", 0, 0);

    private static List<PickpocketSessionAnalyzer.Finding> Diagnose(IReadOnlyList<PickpocketSessionAnalyzer.Sample> samples) =>
        PickpocketSessionAnalyzer.Diagnose(samples, [], ms => $"{ms / 1000:F1}s");

    [Fact]
    public void ShortHiddenGapBetweenActiveFramesIsReportedAsFlicker()
    {
        var samples = new[] { Sample(1, 1000, "Active"), Sample(2, 1016, "Active"), Sample(3, 1032, "Hidden"), Sample(4, 1110, "Active"), Sample(5, 1126, "Active") };
        Assert.Contains(Diagnose(samples), f => f.Problem && f.Title == "Panel flicker.");
    }

    [Fact]
    public void HiddenForThreeSecondsBeforeCooldownWithoutAResultIsAFalseEnd()
    {
        var samples = new[]
        {
            Sample(1, 1000, "Active"), Sample(2, 1016, "Active"), Sample(3, 1032, "Hidden"),
            Sample(4, 4100, "Hidden", engine: "Cooldown", detail: "Three-minute cooldown is active. Input is disarmed."),
        };
        Assert.Contains(Diagnose(samples), f => f.Problem && f.Title == "False attempt end.");
    }

    [Fact]
    public void ResultBeforeCooldownIsNotAFalseEnd()
    {
        var samples = new[]
        {
            Sample(1, 1000, "Active"), Sample(2, 1016, "Grabbed"), Sample(3, 1032, "Hidden"),
            Sample(4, 4100, "Hidden", engine: "Cooldown"),
        };
        Assert.DoesNotContain(Diagnose(samples), f => f.Title == "False attempt end.");
    }

    [Fact]
    public void CleanTrackingReportsNoFaults()
    {
        var samples = Enumerable.Range(0, 20).Select(i => Sample(i + 1, 1000 + i * 16, i == 0 ? "Preparing" : "Active")).ToArray();
        var findings = Diagnose(samples);
        Assert.DoesNotContain(findings, f => f.Problem);
    }

    [Fact]
    public void ParseReadsTheRecorderTraceFormat()
    {
        const string line = """{"imageName":"frame-00005.png","status":{"state":"Tracking","sampleCount":5,"monotonicMilliseconds":1234.5,"sampleIntervalMilliseconds":16,"detail":"x","observation":{"state":"Active","markerX":300.5,"confidence":0.93,"reason":"ok","bar":{"x":10,"y":20,"width":500,"height":18},"bands":[{"color":"Red","left":100,"right":104}]},"automatedPressCount":0,"manualSpacePressCount":0}}""";
        var sample = PickpocketSessionAnalyzer.Parse(line)!;
        Assert.Equal(5, sample.Index);
        Assert.Equal("Active", sample.Visual);
        Assert.Equal(new Rectangle(10, 20, 500, 18), sample.Bar);
        Assert.Single(sample.BandList);
        Assert.Equal(PickpocketBandColor.Red, sample.BandList[0].Color);
    }
}
