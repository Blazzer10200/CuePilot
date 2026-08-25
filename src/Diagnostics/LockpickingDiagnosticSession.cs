using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;

namespace CuePilot;

/// <summary>
/// Bounded, asynchronous persistence for Lockpicking calibration evidence.
/// Detector timing never waits on image encoding, JSONL appends, or the
/// optional second-pass target trace.
/// </summary>
internal sealed class LockpickingDiagnosticSession : IDisposable
{
    internal const int MaximumSessions = 8;
    internal const long MaximumRetainedBytes = 500L * 1024 * 1024;

    private const int MaximumEvidenceFrames = 72;
    private const int MaximumNumberedEvidenceFrames = 180;
    private const int MaximumSpinEvidenceFrames = 30;
    private const int MaximumTargetTraces = 900;
    private static readonly long SpinEvidenceIntervalTicks = Math.Max(1, Stopwatch.Frequency / 12);
    private static readonly long TargetTraceIntervalTicks = Math.Max(1, Stopwatch.Frequency / 10);
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Channel<DiagnosticWrite> writes;
    private readonly Task writerTask;
    private string lastEvidenceKey = string.Empty;
    private int savedEvidenceCount;
    private int savedNonNumberedEvidenceCount;
    private int savedNumberedEvidenceCount;
    private int savedSpinEvidenceCount;
    private int savedTargetTraceCount;
    private long lastSpinEvidenceTimestamp;
    private long lastTargetTraceTimestamp;
    private bool disposed;

    internal LockpickingDiagnosticSession(string? sessionsRoot = null)
    {
        var root = sessionsRoot ?? Path.Combine(AppPaths.DiagnosticsDirectory, "lockpicking");
        var sessionId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        DirectoryPath = Path.Combine(root, sessionId);
        Directory.CreateDirectory(DirectoryPath);
        PruneOldSessions(root);
        writes = Channel.CreateBounded<DiagnosticWrite>(new BoundedChannelOptions(24)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });
        writerTask = Task.Run(WriterLoopAsync);
    }

    internal string DirectoryPath { get; }
    internal int SavedSpinEvidenceCount => savedSpinEvidenceCount;

    internal void QueueTargetTrace(Bitmap frame, int sampleCount, long sampleTimestamp)
    {
        if (disposed
            || savedTargetTraceCount >= MaximumTargetTraces
            || (lastTargetTraceTimestamp != 0
                && sampleTimestamp - lastTargetTraceTimestamp < TargetTraceIntervalTicks))
        {
            return;
        }

        // Throttle before cloning so a saturated writer cannot trigger a
        // full-frame allocation on every capture iteration.
        lastTargetTraceTimestamp = sampleTimestamp;
        Bitmap copy;
        try
        {
            copy = frame.Clone(new Rectangle(0, 0, frame.Width, frame.Height), PixelFormat.Format24bppRgb);
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException)
        {
            return;
        }

        if (writes.Writer.TryWrite(new TargetTraceWrite(copy, sampleCount)))
        {
            savedTargetTraceCount++;
        }
        else
        {
            copy.Dispose();
        }
    }

    internal void QueueEvidence(
        Bitmap frame,
        LockpickingObservation observation,
        LockpickingSpinTelemetry? spin,
        FrameSourceStatus capture,
        int sampleCount,
        long sampleTimestamp)
    {
        if (disposed)
        {
            return;
        }

        var evidenceKey = $"{observation.State}:{observation.Target?.Phase}:{observation.PredictedAction}";
        var saveNumberedFrame = observation.State == LockpickingVisualState.Numbered
            && savedNumberedEvidenceCount < MaximumNumberedEvidenceFrames;
        var saveSpinBurst = observation.State == LockpickingVisualState.Spin
            && savedSpinEvidenceCount < MaximumSpinEvidenceFrames
            && (lastSpinEvidenceTimestamp == 0
                || sampleTimestamp - lastSpinEvidenceTimestamp >= SpinEvidenceIntervalTicks);
        if (!saveNumberedFrame
            && !saveSpinBurst
            && (savedNonNumberedEvidenceCount >= MaximumEvidenceFrames || evidenceKey == lastEvidenceKey))
        {
            return;
        }

        var evidenceRegion = saveSpinBurst || saveNumberedFrame
            ? HudEvidenceRegion(frame, observation)
            : new Rectangle(0, 0, frame.Width, frame.Height);
        Bitmap evidence;
        try
        {
            evidence = frame.Clone(evidenceRegion, PixelFormat.Format24bppRgb);
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException)
        {
            return;
        }

        var sequence = savedEvidenceCount + 1;
        var kind = saveSpinBurst ? "spinburst" : observation.State.ToString().ToLowerInvariant();
        var stem = $"{sequence:00}-{kind}-{sampleCount:000000}";
        var extension = saveSpinBurst || saveNumberedFrame ? "png" : "jpg";
        var write = new EvidenceWrite(
            evidence,
            stem,
            extension,
            sampleCount,
            new Size(frame.Width, frame.Height),
            evidenceRegion,
            capture,
            spin,
            observation);
        if (!writes.Writer.TryWrite(write))
        {
            evidence.Dispose();
            return;
        }

        lastEvidenceKey = evidenceKey;
        savedEvidenceCount = sequence;
        if (saveNumberedFrame)
        {
            savedNumberedEvidenceCount++;
        }
        else if (!saveSpinBurst)
        {
            savedNonNumberedEvidenceCount++;
        }
        if (saveSpinBurst)
        {
            savedSpinEvidenceCount++;
            lastSpinEvidenceTimestamp = sampleTimestamp;
        }
    }

    private async Task WriterLoopAsync()
    {
        await foreach (var write in writes.Reader.ReadAllAsync())
        {
            try
            {
                switch (write)
                {
                    case EvidenceWrite evidence:
                        var imageFormat = evidence.Extension == "png" ? ImageFormat.Png : ImageFormat.Jpeg;
                        evidence.Bitmap.Save(
                            Path.Combine(DirectoryPath, $"{evidence.Stem}.{evidence.Extension}"),
                            imageFormat);
                        await File.AppendAllTextAsync(
                            Path.Combine(DirectoryPath, "events.jsonl"),
                            JsonSerializer.Serialize(new
                            {
                                capturedAt = DateTimeOffset.Now,
                                evidence.SampleCount,
                                frame = new { evidence.FrameSize.Width, evidence.FrameSize.Height },
                                evidenceRegion = new
                                {
                                    evidence.EvidenceRegion.Left,
                                    evidence.EvidenceRegion.Top,
                                    evidence.EvidenceRegion.Width,
                                    evidence.EvidenceRegion.Height,
                                },
                                evidenceFormat = evidence.Extension,
                                capture = new
                                {
                                    evidence.Capture.Backend,
                                    frameAgeMilliseconds = evidence.Capture.FrameAge.TotalMilliseconds,
                                    evidence.Capture.CaptureMilliseconds,
                                    evidence.Capture.AccumulatedFrames,
                                },
                                evidence.Spin,
                                evidence.Observation,
                            }, CompactJson) + Environment.NewLine).ConfigureAwait(false);
                        break;
                    case TargetTraceWrite trace:
                        var targetTrace = LockpickingDetector.TraceTargets(trace.Bitmap);
                        await File.AppendAllTextAsync(
                            Path.Combine(DirectoryPath, "candidate-trace.jsonl"),
                            JsonSerializer.Serialize(new
                            {
                                diagnostic = "lockpick-target-trace-v1",
                                capturedAt = DateTimeOffset.Now,
                                trace.SampleCount,
                                trace = targetTrace,
                            }, CompactJson) + Environment.NewLine).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ExternalException
                or ArgumentException)
            {
                // Optional diagnostics must never interrupt observation.
            }
            finally
            {
                write.Bitmap.Dispose();
            }
        }
    }

    private static Rectangle HudEvidenceRegion(Bitmap frame, LockpickingObservation observation)
    {
        var minimum = Math.Min(frame.Width, frame.Height);
        var radius = observation.HudRadius * minimum * 1.18;
        var desired = Math.Max(1, (int)Math.Ceiling(radius * 2));
        var width = Math.Min(frame.Width, desired);
        var height = Math.Min(frame.Height, desired);
        var left = Math.Clamp(
            (int)Math.Round(observation.HudCenterX * frame.Width - width / 2d),
            0,
            frame.Width - width);
        var top = Math.Clamp(
            (int)Math.Round(observation.HudCenterY * frame.Height - height / 2d),
            0,
            frame.Height - height);
        return new Rectangle(left, top, width, height);
    }

    internal static void PruneOldSessions(
        string root,
        int maximumSessions = MaximumSessions,
        long maximumBytes = MaximumRetainedBytes)
    {
        Directory.CreateDirectory(root);
        var sessions = new DirectoryInfo(root)
            .EnumerateDirectories()
            .OrderByDescending(item => item.LastWriteTimeUtc)
            .ToList();
        long retainedBytes = 0;
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            long bytes;
            try
            {
                bytes = session.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            }
            catch
            {
                continue;
            }
            retainedBytes += bytes;
            if (index < maximumSessions && (index == 0 || retainedBytes <= maximumBytes))
            {
                continue;
            }
            try
            {
                session.Delete(true);
            }
            catch
            {
                // Retention cleanup is best-effort and never blocks capture.
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        writes.Writer.TryComplete();
        try
        {
            writerTask.GetAwaiter().GetResult();
        }
        catch
        {
            // Diagnostic writer shutdown remains non-fatal.
        }
    }

    private abstract record DiagnosticWrite(Bitmap Bitmap);

    private sealed record EvidenceWrite(
        Bitmap Bitmap,
        string Stem,
        string Extension,
        int SampleCount,
        Size FrameSize,
        Rectangle EvidenceRegion,
        FrameSourceStatus Capture,
        LockpickingSpinTelemetry? Spin,
        LockpickingObservation Observation) : DiagnosticWrite(Bitmap);

    private sealed record TargetTraceWrite(Bitmap Bitmap, int SampleCount) : DiagnosticWrite(Bitmap);
}
