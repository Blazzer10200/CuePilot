using System.Drawing;

namespace CuePilot.Tests;

public sealed class FishingDiagnosticLogTests
{
    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CuePilot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void WriteFailure_DoesNotThrowAndDisablesLog()
    {
        var directory = NewDirectory();
        try
        {
            // A directory where the log file belongs makes every open fail like a locked file.
            Directory.CreateDirectory(Path.Combine(directory, "fishing-loop.csv"));
            Directory.CreateDirectory(Path.Combine(directory, "last-fishing.csv"));

            var loop = new FishingLoopLogFile(Path.Combine(directory, "fishing-loop.csv"));
            loop.Append("first");
            loop.Append("second");
            Assert.True(loop.IsDisabled);
            Assert.Equal(2, loop.DroppedWrites);

            FishingLoopDiagnosticLog.Write("meter_lock", "detail", directory);

            using var diagnostics = new FishingDiagnosticLog(directory);
            diagnostics.Write(FishingMeterObservation.Missing, holding: false);
            diagnostics.Write(FishingMeterObservation.Missing, holding: true, "pulse_start", 40);
            Assert.True(diagnostics.IsCsvDisabled);
            Assert.Equal(2, diagnostics.DroppedWrites);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoopLog_StopsAtTheSizeCapAndNotesItOnce()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "fishing-loop.csv");
            var loop = new FishingLoopLogFile(path, maximumBytes: 512);
            for (var index = 0; index < 100; index++)
            {
                loop.Append($"2026-01-01T00:00:00.0000000+00:00,event_{index},detail");
            }

            var lines = File.ReadAllLines(path);
            Assert.Single(lines, line => line.Contains("log_capped", StringComparison.Ordinal));
            Assert.Contains("log_capped", lines[^1], StringComparison.Ordinal);
            Assert.True(new FileInfo(path).Length < 512 + 200);
            Assert.True(loop.DroppedWrites > 0);
            Assert.False(loop.IsDisabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EvidenceOnlyLog_DoesNotTruncateLastFishingCsv()
    {
        var directory = NewDirectory();
        try
        {
            var csvPath = Path.Combine(directory, "last-fishing.csv");
            File.WriteAllText(csvPath, "previous regulation rows");
            using var frame = new Bitmap(320, 180);
            var analysis = FishingMeterService.AnalyzeFrameDetailed(frame);

            using (var diagnostics = new FishingDiagnosticLog(directory))
            {
                diagnostics.CaptureEvidence("meter-lock", frame, analysis);
            }

            Assert.Equal("previous regulation rows", File.ReadAllText(csvPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DebugSession_EventsFile_IsCapped()
    {
        var root = NewDirectory();
        try
        {
            const long cap = 4096;
            string eventsPath;
            long dropped;
            using (var session = new FishingDebugSession(AppSettings.Defaults().Routine, root, cap))
            {
                eventsPath = Path.Combine(session.DirectoryPath, "events.jsonl");
                for (var index = 0; index < 300; index++)
                {
                    session.Record("test", "event", new { index });
                }

                session.Complete("Test complete");
                dropped = session.DroppedWrites;
            }

            var lines = File.ReadAllLines(eventsPath);
            Assert.Single(lines, line => line.Contains("events_capped", StringComparison.Ordinal));
            Assert.True(new FileInfo(eventsPath).Length < cap + 512);
            Assert.True(dropped > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
