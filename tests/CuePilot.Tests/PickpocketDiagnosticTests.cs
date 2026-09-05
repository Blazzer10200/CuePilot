using System.Drawing;
using System.Text.Json;

namespace CuePilot.Tests;

public sealed class PickpocketDiagnosticTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "CuePilotTests", "pickpocket-debug");
    [Theory]
    [InlineData(400, 243, "3.00")]
    [InlineData(-400, 237, "3.00")]
    [InlineData(-400, 242, "-2.00")]
    public void ShotReportKeepsOccludedTargetAndDistinguishesTravelDirection(double speed, double marker, string offset)
    {
        using var session = new PickpocketDiagnosticSession("Yellow", root: Root);
        var fired = Sample(1) with { SelectedBandIndex = 0, AutomatedPressCount = 1,
            Observation = Sample(1).Observation with { Bands = [new(PickpocketBandColor.Yellow, 238.5, 241.5)] },
            Prediction = new(true, 10, 13, speed, 2, "Fixture"), InputDelivery = new("Sent", "Fixture", 10, 11, 46) };
        session.Queue(fired, null, false);
        session.Queue(fired with { SampleCount = 2, Observation = fired.Observation with
            { State = PickpocketVisualState.Missed, MarkerX = marker, Bands = [] } }, null, false);
        session.Complete(Sample(3) with { Observing = false, Observation = PickpocketObservation.Missing });
        Assert.Contains($"Shot result: Missed | Yellow width 3.00 px | stopped marker {offset} px past center", session.Snapshot().Report);
        Assert.Contains("not measured input latency", session.Snapshot().Report);
        Assert.Equal(3, session.Snapshot().Result!.WidthPixels);
        Assert.Equal((marker - 240) * Math.Sign(speed), session.Snapshot().Result!.OffsetPixels);
    }

    [Fact]
    public void PrecisionReportRecordsInputEnabledAndSeparatesHostTimingFromGameReceipt()
    {
        using var session = new PickpocketDiagnosticSession("Red", root: Root);
        session.SetInputMode("PrecisionAttempt");
        session.Complete(Sample(1) with { Observing = false, InputMode = "PrecisionAttempt", AutomatedPressCount = 1,
            InputDelivery = new("Sent", "Fixture", 100, 102, 137) });
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(session.DirectoryPath, "session.json")));
        Assert.True(metadata.RootElement.GetProperty("inputEnabled").GetBoolean());
        var report = session.Snapshot().Report;
        Assert.Contains("experimental center shot", report);
        Assert.Contains("Host key-down lateness: 2.000 ms | key hold: 35.000 ms", report);
        Assert.DoesNotContain("Automatic input is OFF", report);
    }
    private static PickpocketObserveStatus Sample(int index) => PickpocketObserveStatus.Stopped with
    {
        Observing = true, State = "Tracking", SampleCount = index, CaptureBackend = "DXGI fixture",
        MonotonicMilliseconds = index * 16, PresentationMilliseconds = index * 16 - 5, FrameAgeMilliseconds = 5,
        Observation = new(PickpocketVisualState.Active, new Rectangle(52, 218, 576, 23), 240, [new(PickpocketBandColor.White, 230, 322)], 1, "Fixture"),
        Prediction = PickpocketTimingPrediction.Wait("Gathering fresh motion samples.")
    };

    [Fact]
    public void LosslessEvidenceAndFinalStopReasonSurviveShutdown()
    {
        using var session = new PickpocketDiagnosticSession(root: Root);
        using var frame = PickpocketTests.Load(5);
        session.Queue(Sample(1), frame, true, true);
        session.Complete(Sample(1) with { Observing = false, State = "Faulted", Detail = "FiveM lost focus.", Prediction = null });
        var debug = session.Snapshot();
        Assert.Equal("Saved", debug.State);
        Assert.Equal(1, debug.RecordsSaved);
        Assert.Equal(1, debug.ImagesSaved);
        Assert.Contains("FiveM lost focus.", File.ReadAllText(Path.Combine(session.DirectoryPath, "REPORT.md")));
        using var saved = new Bitmap(Path.Combine(session.DirectoryPath, "frame-00001.png"));
        var crop = PickpocketDiagnosticSession.EvidenceRegion(frame.Size, Sample(1).Observation);
        Assert.Equal(crop.Size, saved.Size);
        Assert.Equal(frame.GetPixel(144, 229), saved.GetPixel(144 - crop.X, 229 - crop.Y));
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(session.DirectoryPath, "summary.json")));
        Assert.Equal("Faulted", summary.RootElement.GetProperty("status").GetProperty("state").GetString());
        Assert.Null(summary.RootElement.GetProperty("status").GetProperty("prediction").GetString());
        Assert.Single(File.ReadAllLines(Path.Combine(session.DirectoryPath, "frames.jsonl")));
        using var mapping = JsonDocument.Parse(File.ReadAllLines(Path.Combine(session.DirectoryPath, "frames.jsonl"))[0]);
        Assert.Equal(crop.X, mapping.RootElement.GetProperty("imageRegion").GetProperty("x").GetInt32());
    }

    [Fact]
    public void ReportSeparatesActiveCadenceFromAcquisitionAndIdleAndExplainsAbsentPolicy()
    {
        using var session = new PickpocketDiagnosticSession("Purple", root: Root);
        var idle = Sample(1) with { Observation = PickpocketObservation.Missing, CaptureMilliseconds = 200, SampleIntervalMilliseconds = 500 };
        session.Queue(idle, null, false);
        session.Queue(Sample(2) with { CaptureMilliseconds = 2, AnalysisMilliseconds = 6, SampleIntervalMilliseconds = 67 }, null, false);
        session.Queue(Sample(3) with { CaptureMilliseconds = 2, AnalysisMilliseconds = 6, SampleIntervalMilliseconds = 16 }, null, false);
        session.Queue(Sample(3) with { SampleIntervalMilliseconds = 999 }, null, false);
        session.Queue(Sample(4) with { CaptureMilliseconds = 2, AnalysisMilliseconds = 6, SampleIntervalMilliseconds = 32 }, null, false);
        session.Queue(Sample(5) with { CaptureMilliseconds = 2, AnalysisMilliseconds = 6, SampleIntervalMilliseconds = 48 }, null, false);
        session.Queue(idle with { SampleCount = 6 }, null, false);
        session.Complete(Sample(6) with { Observing = false, State = "Stopped" });
        var report = session.Snapshot().Report;
        Assert.Contains("Active-only mean capture/analysis: 2.00/6.00 ms (4 samples)", report);
        Assert.Contains("Active sample interval mean/max: 32.00/48.00 ms | intervals: 3 | over 24 ms: 2", report);
        Assert.Contains("Preferred color Purple was never detected", report);
    }

    [Theory]
    [InlineData("White")]
    [InlineData("PurpleBlueWhite")]
    public void ReportDoesNotInventCadenceAcrossMissingSamplesOrClaimVisibleColorAbsent(string policy)
    {
        using var session = new PickpocketDiagnosticSession(policy, root: Root);
        session.Queue(Sample(1) with { SampleIntervalMilliseconds = 67 }, null, false);
        session.Queue(Sample(3) with { SampleIntervalMilliseconds = 32 }, null, false);
        session.Queue(Sample(4) with { SampleIntervalMilliseconds = 0 }, null, false);
        session.Complete(Sample(4) with { Observing = false, State = "Stopped" });
        var report = session.Snapshot().Report;
        Assert.Contains("Active sample interval: unavailable", report);
        Assert.DoesNotContain("never detected", report);
        Assert.DoesNotContain("No purple, blue or common/white", report);
    }

    [Fact]
    public void SlowWriterNeverBlocksProducerAndLossIsReported()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new PickpocketDiagnosticSession(root: Root, writerGate: gate.Task);
        try
        {
            for (var i = 1; i <= 300; i++) session.Queue(Sample(i), null, false);
            Assert.True(session.Snapshot().RecordsDropped > 0);
            Assert.Equal(0, session.Snapshot().RecordsSaved);
        }
        finally { gate.SetResult(); }
        session.Complete(Sample(300) with { State = "Stopped", Observing = false, Detail = "Manual stop after queue saturation." });
        var debug = session.Snapshot();
        Assert.Equal("Limited", debug.State);
        Assert.Equal(300, debug.RecordsSaved + debug.RecordsDropped);
        Assert.Contains("Manual stop after queue saturation.", debug.Report);
        Assert.True(File.Exists(Path.Combine(session.DirectoryPath, "summary.json")));
    }

    [Fact]
    public void DiskFailureIsVisibleAndStillProducesAnIndependentSummary()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = new PickpocketDiagnosticSession(root: Root, writerGate: gate.Task);
        try
        {
            Directory.CreateDirectory(Path.Combine(session.DirectoryPath, "trace.jsonl"));
            session.Queue(Sample(1), null, false);
        }
        finally { gate.SetResult(); }
        session.Complete(Sample(1) with { State = "Stopped", Observing = false });
        Assert.Equal("Error", session.Snapshot().State);
        Assert.NotNull(session.Snapshot().Error);
        Assert.Equal(1, session.Snapshot().RecordsDropped);
        Assert.Contains("Evidence error:", File.ReadAllText(Path.Combine(session.DirectoryPath, "REPORT.md")));
    }

    [Fact]
    public async Task SourceStartupFailureStillHasAZeroSampleReport()
    {
        using var observer = new PickpocketObserverEngine(() => throw new IOException("Injected capture startup failure"),
            createDiagnostics: (policy, target) => new(policy, target, Root));
        var done = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.StatusChanged += (_, status) => { if (status.State == "Faulted") done.TrySetResult(status); };
        observer.Start(new() { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 123 });
        var final = await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("No captured samples", final.Debug!.Report);
        Assert.Contains("Injected capture startup failure", final.Debug.Report);
        Assert.True(File.Exists(Path.Combine(final.EvidenceDirectory, "summary.json")));
    }

    [Fact]
    public async Task MissingDiagnosticDirectoryDoesNotClaimToBeRecording()
    {
        using var observer = new PickpocketObserverEngine(createDiagnostics: (_, _) => throw new IOException("Injected unavailable evidence folder"));
        var done = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.StatusChanged += (_, status) => { if (status.State == "Faulted") done.TrySetResult(status); };
        observer.Start(new() { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 123 });
        var final = await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Error", final.Debug!.State);
        Assert.Empty(final.EvidenceDirectory);
    }
}
