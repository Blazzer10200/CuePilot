using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;

namespace CuePilot;

internal sealed record FishingDebugDecision(
    string Kind,
    double Confidence,
    bool Accepted,
    string Reason,
    double SecondaryConfidence = 0);

internal sealed record FishingDebugSnapshot(
    string SessionId,
    bool Active,
    string Stage,
    long ElapsedMilliseconds,
    int EventCount,
    int SavedFrameCount,
    string CaptureHealth,
    FishingDebugDecision Prompt,
    FishingDebugDecision Meter,
    string LastEvent,
    string Outcome);

internal sealed class FishingDebugSession : IDisposable
{
    private const int MaximumSessions = 5;
    private const long MaximumSessionBytes = 250L * 1024 * 1024;
    private const int MaximumPromptRollFrames = 120;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object sync = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DateTimeOffset startedAt = DateTimeOffset.Now;
    private readonly FishingRoutineSettings settings;
    private readonly string directory;
    private readonly string eventsPath;
    private readonly string manifestPath;
    private readonly Channel<DebugWrite> writes;
    private readonly Task writerTask;
    private readonly List<DebugFrame> frames = [];
    private readonly Dictionary<string, double> bestFrameScores = new(StringComparer.OrdinalIgnoreCase);
    private int eventCount;
    private bool active = true;
    private bool disposed;
    private string stage = "Starting";
    private string captureHealth = "Not sampled";
    private string lastEvent = "Session created";
    private string outcome = "Running";
    private DateTimeOffset? endedAt;
    private FishingDebugDecision prompt = new("None", 0, false, "No prompt sample yet");
    private FishingDebugDecision meter = new("Missing", 0, false, "No meter sample yet");

    internal FishingDebugSession(FishingRoutineSettings requestedSettings, string? sessionsDirectory = null)
    {
        settings = requestedSettings.Copy();
        SessionId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24];
        var root = sessionsDirectory ?? AppPaths.DebugSessionsDirectory;
        directory = Path.Combine(root, SessionId);
        eventsPath = Path.Combine(directory, "events.jsonl");
        manifestPath = Path.Combine(directory, "session.json");
        Directory.CreateDirectory(directory);
        PruneOldSessions(root);
        writes = Channel.CreateBounded<DebugWrite>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        writerTask = Task.Run(WriterLoopAsync);
        Record("session", "start", new
        {
            target = new
            {
                settings.TargetWindow.ProcessId,
                settings.TargetWindow.ProcessName,
                settings.TargetWindow.WindowTitle,
            },
            controller = ControllerSettings(),
        });
    }

    internal string SessionId { get; }
    internal string DirectoryPath => directory;

    internal FishingDebugSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return new FishingDebugSnapshot(
                    SessionId,
                    active,
                    stage,
                    clock.ElapsedMilliseconds,
                    eventCount,
                    frames.Count,
                    captureHealth,
                    prompt,
                    meter,
                    lastEvent,
                    outcome);
            }
        }
    }

    internal void SetStage(string value, string detail)
    {
        lock (sync) stage = value;
        Record("state", "stage", new { stage = value, detail });
    }

    internal void RecordCapture(string detector, FrameSourceStatus status, Size? frameSize = null)
    {
        lock (sync)
        {
            captureHealth = status.State == FrameSourceState.Ready
                ? $"{status.Backend} · {status.CaptureMilliseconds:0.0} ms"
                : $"{status.State} · {status.Detail}";
        }

        if (status.State != FrameSourceState.Ready)
        {
            Record(detector, "capture_problem", new
            {
                state = status.State,
                status.Backend,
                status.Detail,
                captureMilliseconds = status.CaptureMilliseconds,
                width = frameSize?.Width,
                height = frameSize?.Height,
            });
        }
        else if (status.Detail.StartsWith("Fallback active", StringComparison.Ordinal))
        {
            Record(detector, "capture_fallback", new
            {
                status.Backend,
                status.Detail,
                captureMilliseconds = status.CaptureMilliseconds,
                width = frameSize?.Width,
                height = frameSize?.Height,
            });
        }
    }

    internal void RecordPrompt(
        FishingPromptKind expected,
        FishingPromptObservation observation,
        FishingPromptEvidence evidence,
        FrameLease? frame,
        int sampleCount)
    {
        var best = evidence.Cast.Score >= evidence.Collect.Score ? evidence.Cast : evidence.Collect;
        var accepted = observation.Kind == expected;
        var reason = accepted
            ? $"Accepted {observation.Kind}"
            : observation.Kind == FishingPromptKind.None
                ? evidence.DecisionReason
                : $"Expected {expected}; strongest match was {observation.Kind}";
        var decision = new FishingDebugDecision(
            observation.Kind.ToString(),
            observation.Confidence,
            accepted,
            reason,
            Math.Min(observation.CastConfidence, observation.CollectConfidence));
        lock (sync) prompt = decision;

        if (sampleCount == 1 || sampleCount % 10 == 0 || observation.Kind != FishingPromptKind.None)
        {
            Record("prompt", "sample", new
            {
                expected,
                observation,
                evidence,
                sampleCount,
            });
        }

        if (frame is not null)
        {
            SavePromptRollFrame(frame.Bitmap, expected, observation, evidence, sampleCount);
            var label = accepted ? $"prompt-{expected.ToString().ToLowerInvariant()}-confirmed" : "prompt-best-near-miss";
            SaveBestFrame(label, best.Score, frame.Bitmap, new { expected, observation, evidence, sampleCount });
            if (sampleCount == 1 || sampleCount % 10 == 0)
            {
                SaveFrame("prompt-latest-sample", best.Score, frame.Bitmap,
                    new { expected, observation, evidence, sampleCount }, onlyIfBetter: false);
            }
        }
    }

    internal void RecordMeter(FishingMeterFrameAnalysis analysis, FrameLease? frame, int sampleCount)
    {
        var observation = analysis.Observation;
        var candidate = analysis.PrimaryCandidate;
        var reason = observation.IsVisible
            ? observation.IsFailed ? "Failure meter accepted" : observation.IsCaught ? "Catch meter accepted" : "Active meter accepted"
            : candidate?.Evidence.DecisionReason ?? "No meter candidate in the calibrated regions";
        var confidence = observation.IsVisible
            ? observation.Confidence
            : MeterAcquisitionProximity(candidate);
        var decision = new FishingDebugDecision(
            observation.IsFailed ? "Failed" : observation.IsCaught ? "Caught" : observation.IsVisible ? "Active" : "Missing",
            confidence,
            observation.IsVisible,
            reason);
        lock (sync) meter = decision;

        if (sampleCount == 1 || sampleCount % 5 == 0 || observation.IsVisible)
        {
            Record("meter", "sample", new { observation, candidate, analysis.CandidateCount, sampleCount });
        }

        if (frame is not null)
        {
            var label = observation.IsVisible ? "meter-confirmed" : "meter-best-near-miss";
            SaveBestFrame(label, confidence, frame.Bitmap, new { observation, candidate, analysis.CandidateCount, sampleCount });
        }
    }

    internal void RecordPromptSuppression(
        FishingPromptKind expected,
        FishingPromptObservation observation,
        FishingPromptEvidence evidence,
        FishingMeterFrameAnalysis meterAnalysis,
        FrameLease frame,
        int sampleCount)
    {
        var detail = new
        {
            expected,
            observation,
            evidence,
            meter = meterAnalysis.Observation,
            meterCandidate = meterAnalysis.PrimaryCandidate,
            meterAnalysis.CandidateCount,
            meterAnalysis.UsedTrackedRegion,
            sampleCount,
        };
        Record("prompt", "suppressed_by_meter", detail);
        SaveBestFrame(
            "prompt-suppressed-by-meter",
            Math.Max(observation.Confidence, meterAnalysis.Observation.Confidence),
            frame.Bitmap,
            detail);
    }

    internal void Record(string category, string eventName, object? detail = null)
    {
        if (disposed) return;
        var sequence = Interlocked.Increment(ref eventCount);
        var payload = JsonSerializer.Serialize(new
        {
            sequence,
            elapsedMilliseconds = clock.ElapsedMilliseconds,
            capturedAt = DateTimeOffset.Now,
            category,
            eventName,
            detail,
        }, CompactJson);
        lock (sync) lastEvent = $"{category}: {eventName}";
        Queue(new DebugWrite(DebugWriteKind.AppendEvent, eventsPath, payload + Environment.NewLine));
        QueueManifest();
    }

    internal void Complete(string result)
    {
        lock (sync)
        {
            if (!active) return;
            active = false;
            outcome = result;
            endedAt = DateTimeOffset.Now;
        }
        Record("session", "complete", new { outcome = result });
        QueueManifest();
    }

    private void SaveBestFrame(string label, double score, Bitmap exactFrame, object metadata)
        => SaveFrame(label, score, exactFrame, metadata, onlyIfBetter: true);

    private static double MeterAcquisitionProximity(FishingMeterCandidateEvidence? candidate)
    {
        if (candidate is null) return 0;
        var evidence = candidate.Value.Evidence;
        // CandidateConfidence can reach 100% for ordinary dark scenery before the
        // active-meter identity gate rejects it. Weight the actual independent
        // identity signals so the saved near-miss is useful for future diagnosis.
        return Math.Clamp(
            (evidence.LmbPromptStrength * 0.45) +
            (evidence.RingStrength * 0.25) +
            (evidence.DarkDisk * 0.15) +
            (evidence.DiskContrast * 0.15),
            0,
            1);
    }

    private void SavePromptRollFrame(
        Bitmap exactFrame,
        FishingPromptKind expected,
        FishingPromptObservation observation,
        FishingPromptEvidence evidence,
        int sampleCount)
    {
        // Keep the part of every captured frame where FiveM renders its action
        // prompts. A bounded rolling sequence makes brief or capture-only HUD
        // failures replayable without retaining full-resolution gameplay video.
        var cropHeight = Math.Max(1, exactFrame.Height * 35 / 100);
        var crop = new Rectangle(0, exactFrame.Height - cropHeight, exactFrame.Width, cropHeight);
        var clone = exactFrame.Clone(crop, PixelFormat.Format24bppRgb);
        var slot = (sampleCount - 1) % MaximumPromptRollFrames;
        var stem = $"prompt-roll-{slot:D3}";
        var rollDirectory = Path.Combine(directory, "prompt-roll");
        var metadataJson = JsonSerializer.Serialize(new
        {
            sampleCount,
            elapsedMilliseconds = clock.ElapsedMilliseconds,
            capturedAt = DateTimeOffset.Now,
            sourceWidth = exactFrame.Width,
            sourceHeight = exactFrame.Height,
            crop,
            expected,
            observation,
            evidence,
        }, Json);
        if (!Queue(new DebugWrite(DebugWriteKind.SaveJpeg, Path.Combine(rollDirectory, $"{stem}.jpg"), Bitmap: clone)))
        {
            clone.Dispose();
            return;
        }
        Queue(new DebugWrite(DebugWriteKind.WriteText, Path.Combine(rollDirectory, $"{stem}.json"), metadataJson));
    }

    private void SaveFrame(
        string label,
        double score,
        Bitmap exactFrame,
        object metadata,
        bool onlyIfBetter)
    {
        lock (sync)
        {
            if (onlyIfBetter && bestFrameScores.TryGetValue(label, out var previous) && score <= previous) return;
            bestFrameScores[label] = score;
            frames.RemoveAll(frame => frame.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            frames.Add(new DebugFrame(label, $"{label}.png", $"{label}.json", score, clock.ElapsedMilliseconds));
        }

        var clone = new Bitmap(exactFrame);
        var metadataJson = JsonSerializer.Serialize(new
        {
            label,
            score,
            elapsedMilliseconds = clock.ElapsedMilliseconds,
            capturedAt = DateTimeOffset.Now,
            width = exactFrame.Width,
            height = exactFrame.Height,
            detail = metadata,
        }, Json);
        if (!Queue(new DebugWrite(DebugWriteKind.SaveBitmap, Path.Combine(directory, $"{label}.png"), Bitmap: clone)))
        {
            clone.Dispose();
            return;
        }
        Queue(new DebugWrite(DebugWriteKind.WriteText, Path.Combine(directory, $"{label}.json"), metadataJson));
        QueueManifest();
    }

    private void QueueManifest()
    {
        object manifest;
        lock (sync)
        {
            manifest = new
            {
                sessionId = SessionId,
                active,
                startedAt,
                endedAt,
                elapsedMilliseconds = clock.ElapsedMilliseconds,
                stage,
                captureHealth,
                eventCount,
                outcome,
                lastEvent,
                prompt,
                meter,
                target = new
                {
                    settings.TargetWindow.ProcessId,
                    settings.TargetWindow.ProcessName,
                    settings.TargetWindow.WindowTitle,
                },
                controller = ControllerSettings(),
                promptRoll = new
                {
                    directory = "prompt-roll",
                    maximumFrames = MaximumPromptRollFrames,
                    crop = "bottom 35%",
                },
                frames = frames.ToArray(),
            };
        }
        Queue(new DebugWrite(DebugWriteKind.WriteText, manifestPath, JsonSerializer.Serialize(manifest, Json)));
    }

    private object ControllerSettings() => new
    {
        settings.FishingLowerTensionPercent,
        settings.FishingUpperTensionPercent,
        settings.FishingSampleMilliseconds,
        settings.FishingMinimumPulseMilliseconds,
        settings.FishingMaximumPulseMilliseconds,
        settings.FishingMinimumRestMilliseconds,
        settings.MaximumDurationSeconds,
        settings.CollectDelayMilliseconds,
        settings.CollectOnTimeout,
        settings.InputMode,
    };

    private bool Queue(DebugWrite write) => writes.Writer.TryWrite(write);

    private async Task WriterLoopAsync()
    {
        await foreach (var write in writes.Reader.ReadAllAsync())
        {
            try
            {
                switch (write.Kind)
                {
                    case DebugWriteKind.AppendEvent:
                        Directory.CreateDirectory(Path.GetDirectoryName(write.Path)!);
                        await File.AppendAllTextAsync(write.Path, write.Text ?? string.Empty);
                        break;
                    case DebugWriteKind.WriteText:
                        Directory.CreateDirectory(Path.GetDirectoryName(write.Path)!);
                        var temporary = write.Path + ".tmp";
                        await File.WriteAllTextAsync(temporary, write.Text ?? string.Empty);
                        File.Move(temporary, write.Path, true);
                        break;
                    case DebugWriteKind.SaveBitmap:
                        Directory.CreateDirectory(Path.GetDirectoryName(write.Path)!);
                        write.Bitmap!.Save(write.Path, ImageFormat.Png);
                        break;
                    case DebugWriteKind.SaveJpeg:
                        Directory.CreateDirectory(Path.GetDirectoryName(write.Path)!);
                        write.Bitmap!.Save(write.Path, ImageFormat.Jpeg);
                        break;
                }
            }
            catch
            {
                // Debugging must never break capture or input safety.
            }
            finally
            {
                write.Bitmap?.Dispose();
            }
        }
    }

    private static void PruneOldSessions(string root)
    {
        Directory.CreateDirectory(root);
        var sessions = new DirectoryInfo(root)
            .EnumerateDirectories()
            .OrderByDescending(item => item.CreationTimeUtc)
            .ToList();
        long retainedBytes = 0;
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            var bytes = session.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            retainedBytes += bytes;
            if (index < MaximumSessions && (index == 0 || retainedBytes <= MaximumSessionBytes)) continue;
            try
            {
                session.Delete(true);
            }
            catch
            {
                // Retention cleanup is best-effort and never blocks automation.
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        Complete(active ? "Disposed" : outcome);
        disposed = true;
        writes.Writer.TryComplete();
        try
        {
            writerTask.GetAwaiter().GetResult();
        }
        catch
        {
            // Debug writer shutdown remains non-fatal.
        }
    }

    private enum DebugWriteKind { AppendEvent, WriteText, SaveBitmap, SaveJpeg }
    private sealed record DebugWrite(DebugWriteKind Kind, string Path, string? Text = null, Bitmap? Bitmap = null);
    private sealed record DebugFrame(string Label, string ImageName, string MetadataName, double Score, long ElapsedMilliseconds);
}
