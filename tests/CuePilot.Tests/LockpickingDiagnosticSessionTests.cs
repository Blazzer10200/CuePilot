using System.Diagnostics;
using System.Drawing;

namespace CuePilot.Tests;

public sealed class LockpickingDiagnosticSessionTests
{
    [Fact]
    public void EvidenceWriterFlushesQueuedImageAndMetadataOnDispose()
    {
        var root = TemporaryRoot();
        try
        {
            string directory;
            using (var session = new LockpickingDiagnosticSession(root))
            {
                directory = session.DirectoryPath;
                using var frame = new Bitmap(80, 60);
                session.QueueEvidence(
                    frame,
                    LockpickingObservation.Hidden("test"),
                    null,
                    new FrameSourceStatus(FrameSourceState.Ready, "Test", "Ready", TimeSpan.Zero, 1.5),
                    1,
                    Stopwatch.GetTimestamp());
            }

            Assert.Single(Directory.GetFiles(directory, "*.jpg"));
            var eventsPath = Path.Combine(directory, "events.jsonl");
            Assert.True(File.Exists(eventsPath));
            Assert.Contains("\"sampleCount\":1", File.ReadAllText(eventsPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RetentionPrunesOldestSessionsByCount()
    {
        var root = TemporaryRoot();
        try
        {
            for (var index = 0; index < 5; index++)
            {
                var directory = Directory.CreateDirectory(Path.Combine(root, $"session-{index}"));
                directory.LastWriteTimeUtc = DateTime.UtcNow.AddMinutes(-index);
            }

            LockpickingDiagnosticSession.PruneOldSessions(root, maximumSessions: 3, maximumBytes: long.MaxValue);

            var remaining = Directory.GetDirectories(root).Select(path => Path.GetFileName(path)!).Order().ToArray();
            Assert.Equal(["session-0", "session-1", "session-2"], remaining);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RetentionKeepsNewestSessionButPrunesWhenByteBudgetIsExceeded()
    {
        var root = TemporaryRoot();
        try
        {
            for (var index = 0; index < 3; index++)
            {
                var directory = Directory.CreateDirectory(Path.Combine(root, $"session-{index}"));
                File.WriteAllBytes(Path.Combine(directory.FullName, "evidence.bin"), new byte[32]);
                directory.LastWriteTimeUtc = DateTime.UtcNow.AddMinutes(-index);
            }

            LockpickingDiagnosticSession.PruneOldSessions(root, maximumSessions: 10, maximumBytes: 40);

            Assert.Equal("session-0", Path.GetFileName(Assert.Single(Directory.GetDirectories(root))));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string TemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "CuePilot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
