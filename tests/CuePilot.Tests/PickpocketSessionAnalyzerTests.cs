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
    public void NoFramesIsNotOk()
    {
        var findings = Diagnose([Sample(1, 1000, "Hidden"), Sample(2, 1016, "Hidden")]);
        Assert.Contains(findings, f => f.Problem && f.Title == "No frames audited.");
        Assert.DoesNotContain(findings, f => f.Title == "No detector faults found.");
    }

    [Fact]
    public void ObserveModeCannotArmSoNeverArmedIsNotFlagged()
    {
        var samples = Enumerable.Range(0, 20).Select(i => Sample(i + 1, 1000 + i * 16, "Active") with { InputMode = "Observe" }).ToArray();
        Assert.DoesNotContain(Diagnose(samples), f => f.Title == "Never armed.");
        var precision = samples.Select(s => s with { InputMode = "PrecisionAttempt" }).ToArray();
        Assert.Contains(Diagnose(precision), f => f.Title == "Never armed.");
    }

    [Theory]
    [InlineData("Limited")]
    [InlineData("Error")]
    public void LimitedOrErroredRecorderIsNotReportedAsClean(string recorderState)
    {
        var samples = Enumerable.Range(0, 20).Select(i => Sample(i + 1, 1000 + i * 16, i == 0 ? "Preparing" : "Active")).ToArray();
        Assert.Contains(PickpocketSessionAnalyzer.Diagnose(samples, [], ms => $"{ms / 1000:F1}s", recorderState), f => f.Problem && f.Title == "Evidence incomplete.");
        var report = PickpocketSessionAnalyzer.Build("s", samples, [], true, recorderState);
        Assert.DoesNotContain("finished cleanly", report);
        Assert.Contains(recorderState, report);
        Assert.Contains("finished cleanly", PickpocketSessionAnalyzer.Build("s", samples, [], true, "Saved"));
    }

    [Fact]
    public void ReadTraceSkipsNonObjectAndMalformedLines()
    {
        var path = Path.Combine(Path.GetTempPath(), "CuePilotTests", $"analyzer-trace-{Guid.NewGuid():N}.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllLines(path, ["[1,2]", "42", "\"text\"", "null", """{"status":5}""", """{"status":{"state":"Tracking","sampleCount":7,"inputMode":"Observe"}}""", """{"status":{"sta"""]);
            var samples = PickpocketSessionAnalyzer.ReadTrace(path);
            Assert.Equal(7, Assert.Single(samples).Index);
            Assert.Equal("Observe", samples[0].InputMode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RunCorpus_NoQualifyingSessions_PrintsMessageAndExitsCleanly()
    {
        var root = Path.Combine(Path.GetTempPath(), "CuePilotTests", $"analyzer-corpus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "20260101-000000-stub"));
        try { Assert.Equal(2, PickpocketSessionAnalyzer.RunCorpus(root)); }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ReplayReportsManifestValidationFailuresInsteadOfThrowing()
    {
        var manifest = Path.Combine(Path.GetTempPath(), "CuePilotTests", $"replay-manifest-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        try
        {
            File.WriteAllText(manifest, "[]");
            Assert.Equal(2, PickpocketReplay.Run(manifest, "Red"));
            File.WriteAllText(manifest, """[{"file":"a.png","presentationMs":5},{"file":"b.png","presentationMs":5}]""");
            Assert.Equal(2, PickpocketReplay.Run(manifest, "Red"));
        }
        finally { File.Delete(manifest); }
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
