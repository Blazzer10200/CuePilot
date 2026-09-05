using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace CuePilot;

internal sealed record PickpocketShotResult(string State, PickpocketBandColor Color, double WidthPixels, double? OffsetPixels);
internal sealed record PickpocketDebugStatus(string State, int RecordsSaved, int RecordsDropped,
    int ImagesSaved, int ImagesSkipped, string? Error, string Report, PickpocketShotResult? Result = null);

/// <summary>Bounded local recorder. Capture never waits on encoding or disk.</summary>
internal sealed class PickpocketDiagnosticSession : IDisposable
{
    internal const int MaximumRecords = 12000;
    internal const int MaximumImages = 128;
    private const long MaximumTraceBytes = 32 * 1024 * 1024;
    private const long MaximumImageBytes = 64 * 1024 * 1024;
    private const long CriticalImageReserveBytes = 16 * 1024 * 1024;
    private sealed record Entry(PickpocketObserveStatus Status, Bitmap? Image, Rectangle ImageRegion, bool Critical);
    private Rectangle lastImageRegion;
    private Size lastFrameSize;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    private readonly Channel<Entry> queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(128)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Dictionary<string, int> reasons = new();
    private readonly Dictionary<string, int> states = new();
    private readonly Task writer;
    private readonly DateTime started = DateTime.UtcNow;
    private readonly string policy;
    private PickpocketObserveStatus latest = PickpocketObserveStatus.Stopped;
    private int queued, saved, dropped, imagesQueued, imagesSaved, imagesSkipped, pendingImages, completed;
    private int stale, slow, spacePresses, measuredSamples;
    private double captureTotal, analysisTotal, captureMax, analysisMax, ageMax;
    private int activeSamples, activeIntervals, activeGaps, preferredVisibleSamples;
    private double activeIntervalTotal, activeIntervalMax, activeCaptureTotal, activeAnalysisTotal;
    private bool previousActive;
    private int samples;
    private string? error;
    private sealed record ShotGeometry(PickpocketBand Band, Rectangle Bar, double Speed, int Sample);
    private ShotGeometry? shot;
    private string? shotResult;
    private PickpocketShotResult? shotOutcome;
    internal string DirectoryPath { get; }
    internal string? Error => Volatile.Read(ref error);

    internal void SetInputMode(string mode)
    {
        var path = Path.Combine(DirectoryPath, "session.json");
        var metadata = JsonNode.Parse(File.ReadAllText(path))!;
        metadata["inputMode"] = mode;
        metadata["inputEnabled"] = mode is "SingleAttempt" or "PrecisionAttempt";
        File.WriteAllText(path, metadata.ToJsonString(Json));
    }

    internal PickpocketDiagnosticSession(string policy = "Widest", WindowTargetSettings? target = null, string? root = null, Task? writerGate = null)
    {
        this.policy = policy;
        DirectoryPath = Path.Combine(root ?? Path.Combine(AppPaths.DiagnosticsDirectory, "pickpocket"), $"{started:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, "session.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 4, startedUtc = started, engineVersion = typeof(PickpocketDiagnosticSession).Assembly.GetName().Version?.ToString(),
            os = Environment.OSVersion.VersionString, target = target?.ProcessName, targetPid = target?.ProcessId, policy,
            inputEnabled = false, simulatedInputDelayMs = new[] { 0, 16 }, maximumRecords = MaximumRecords,
            maximumImages = MaximumImages, maximumTraceBytes = MaximumTraceBytes, maximumImageBytes = MaximumImageBytes, criticalImageReserveBytes = CriticalImageReserveBytes,
            note = "No summary.json means the session did not finish cleanly. Space is polled only during active foreground play; its timestamp is an observation, not exact key delivery."
        }, Json));
        writer = Task.Run(async () => { if (writerGate is not null) await writerGate; await WriteAsync(); });
    }

    internal void Queue(PickpocketObserveStatus status, Bitmap? frame, bool image, bool critical = false)
    {
        latest = status with { Debug = null };
        if (shot is null && status.AutomatedPressCount == 1 && status.InputDelivery?.KeyDownMs is not null
            && status.Observation.State == PickpocketVisualState.Active && status.SelectedBandIndex is int selected
            && selected >= 0 && selected < status.Observation.Bands.Count && status.Prediction is { } prediction)
            shot = new(status.Observation.Bands[selected], status.Observation.Bar, prediction.SpeedPixelsPerSecond, status.SampleCount);
        if (shotResult is null && shot is { } fired && status.SampleCount > fired.Sample
            && status.Observation.State is PickpocketVisualState.Grabbed or PickpocketVisualState.Missed)
        {
            // A grabbed sliver can disappear under the marker. Retain its geometry
            // from the actual shot instead of trusting the result-frame band list.
            var result = status.Observation;
            shotOutcome = new(result.State.ToString(), fired.Band.Color, fired.Band.Width,
                result.Bar == fired.Bar && double.IsFinite(result.MarkerX) && double.IsFinite(fired.Speed) && fired.Speed != 0
                    ? (result.MarkerX - fired.Band.Center) * Math.Sign(fired.Speed) : null);
            shotResult = result.Bar == fired.Bar && double.IsFinite(result.MarkerX) && double.IsFinite(fired.Speed) && fired.Speed != 0
                ? $"Shot result: {result.State} | {fired.Band.Color} width {fired.Band.Width:F2} px | stopped marker {(result.MarkerX - fired.Band.Center) * Math.Sign(fired.Speed):F2} px past center (negative = before center). Visual offset, not measured input latency."
                : $"Shot result: {result.State} | visual offset unavailable because geometry or motion changed.";
        }
        if (status.SampleCount > samples)
        {
            var active = status.Observation.State == PickpocketVisualState.Active;
            if (active)
            {
                activeSamples++;
                activeCaptureTotal += status.CaptureMilliseconds;
                activeAnalysisTotal += status.AnalysisMilliseconds;
                if (PickpocketObserverEngine.ResolveTargetColor(status.Observation, policy, status.CustomPriority) is not null) preferredVisibleSamples++;
                // Only adjacent Active samples measure the fast loop. Exclude acquisition,
                // idle/cooldown intervals, duplicate statuses, and gaps in submitted evidence.
                if (previousActive && status.SampleCount == samples + 1
                    && double.IsFinite(status.SampleIntervalMilliseconds) && status.SampleIntervalMilliseconds > 0)
                {
                    activeIntervals++;
                    activeIntervalTotal += status.SampleIntervalMilliseconds;
                    activeIntervalMax = Math.Max(activeIntervalMax, status.SampleIntervalMilliseconds);
                    if (status.SampleIntervalMilliseconds > 24) activeGaps++;
                }
            }
            previousActive = active;
            samples = status.SampleCount;
            measuredSamples++;
            captureTotal += status.CaptureMilliseconds;
            analysisTotal += status.AnalysisMilliseconds;
            captureMax = Math.Max(captureMax, status.CaptureMilliseconds);
            analysisMax = Math.Max(analysisMax, status.AnalysisMilliseconds);
            ageMax = Math.Max(ageMax, status.FrameAgeMilliseconds ?? 0);
            if (status.FrameAgeMilliseconds is > 40) stale++;
            if (status.Observation.State == PickpocketVisualState.Active && status.LoopMilliseconds > 16) slow++;
            var reason = status.Prediction?.Reason ?? status.Detail;
            reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            var state = status.Observation.State.ToString();
            states[state] = states.GetValueOrDefault(state) + 1;
            spacePresses = status.ManualSpacePressCount;
        }
        if (Volatile.Read(ref completed) != 0) return;
        if (queued >= MaximumRecords || writer.IsCompleted) { Interlocked.Increment(ref dropped); return; }
        Bitmap? bitmap = null;
        var imageRegion = Rectangle.Empty;
        if (image && frame is not null)
        {
            // Reserve sixteen image slots for transitions, candidates and Space edges.
            if (imagesQueued < (critical ? MaximumImages : MaximumImages - 16) && Volatile.Read(ref pendingImages) < 4)
            {
                try
                {
                    imageRegion = EvidenceRegion(frame.Size, status.Observation);
                    if (status.Observation.Bar.Width > 0) { lastImageRegion = imageRegion; lastFrameSize = frame.Size; }
                    else if (lastFrameSize == frame.Size && !lastImageRegion.IsEmpty) imageRegion = lastImageRegion;
                    bitmap = frame.Clone(imageRegion, PixelFormat.Format32bppArgb);
                    Interlocked.Increment(ref pendingImages);
                }
                catch (Exception failure) { Volatile.Write(ref error, failure.Message); }
            }
            if (bitmap is null) Interlocked.Increment(ref imagesSkipped);
        }
        if (queue.Writer.TryWrite(new(latest, bitmap, imageRegion, critical))) { queued++; if (bitmap is not null) imagesQueued++; }
        else
        {
            Interlocked.Increment(ref dropped);
            if (bitmap is not null) { bitmap.Dispose(); Interlocked.Decrement(ref pendingImages); Interlocked.Increment(ref imagesSkipped); }
        }
    }

    internal static Rectangle EvidenceRegion(Size frame, PickpocketObservation observation)
    {
        var bounds = new Rectangle(Point.Empty, frame);
        var bar = observation.Bar;
        if (bar.Width <= 0 || bar.Height <= 0 || !bounds.Contains(bar)) return bounds;
        var scale = bar.Width / 576d;
        return Rectangle.Intersect(bounds, Rectangle.FromLTRB(
            bar.Left - (int)Math.Ceiling(16 * scale), bar.Top - (int)Math.Ceiling(225 * scale),
            bar.Right + (int)Math.Ceiling(16 * scale), bar.Bottom + (int)Math.Ceiling(65 * scale)));
    }

    internal PickpocketDebugStatus Snapshot()
    {
        var state = Error is not null ? "Error" : Volatile.Read(ref dropped) > 0 || Volatile.Read(ref imagesSkipped) > 0 ? "Limited"
            : Volatile.Read(ref completed) != 0 ? "Saved" : "Recording";
        return new(state, Volatile.Read(ref saved), Volatile.Read(ref dropped), Volatile.Read(ref imagesSaved), Volatile.Read(ref imagesSkipped), Error, Report(state), shotOutcome);
    }

    private string Report(string state)
    {
        var report = new StringBuilder();
        report.AppendLine("# Pickpocket debug report");
        report.AppendLine($"Session: {Path.GetFileName(DirectoryPath)} | UTC start: {started:O}");
        report.AppendLine($"Recorder: {state} | Observer: {latest.State} | Policy: {policy}");
        if (policy == "Custom" && latest.CustomPriority is { } order)
            report.AppendLine($"Custom priority: {string.Join(" -> ", order)}");
        if (latest.ItemPriority is { } items) report.AppendLine($"Within-color item priority: {string.Join(" -> ", items)}");
        if (shotResult is not null) report.AppendLine(shotResult);
        report.AppendLine($"Early adjustments: Red {latest.RedAdvanceMs} ms | Yellow {latest.YellowAdvanceMs} ms (thin Precision targets only).");
        report.AppendLine($"Last status: {latest.Detail}");
        report.AppendLine($"Samples: {samples} | Attempts: {latest.Attempt} | Timing candidates: {latest.PredictedPressCount} | Observed Space presses: {spacePresses}");
        report.AppendLine($"Input mode: {latest.InputMode} | Automated Space taps: {latest.AutomatedPressCount} | Armed: {latest.InputArmed}");
        if (latest.InputDelivery is { } delivery)
        {
            report.AppendLine($"Input delivery: {delivery.State} — {delivery.Detail} | planned/down/up monotonic ms: {delivery.PlannedMs:F3}/{delivery.KeyDownMs:F3}/{delivery.KeyUpMs:F3}");
            if (delivery.PlannedMs is double planned && delivery.KeyDownMs is double down && delivery.KeyUpMs is double up)
                report.AppendLine($"Host key-down lateness: {down - planned:F3} ms | key hold: {up - down:F3} ms (host calls, not game receipt latency)");
            if (delivery.PlannedMs is double deadline && delivery.TimerWakeMs is double wake)
                report.AppendLine($"Timer wake lateness: {wake - deadline:F3} ms | final validation: {delivery.ValidationMilliseconds:F3} ms");
        }
        report.AppendLine($"Capture: {latest.CaptureBackend} | mean/max capture: {captureTotal / Math.Max(1, measuredSamples):F2}/{captureMax:F2} ms | mean/max analysis: {analysisTotal / Math.Max(1, measuredSamples):F2}/{analysisMax:F2} ms");
        report.AppendLine($"Last capture detail: {latest.CaptureDetail} | raw frame: {latest.FrameWidth}×{latest.FrameHeight}");
        if (activeSamples > 0)
            report.AppendLine($"Active-only mean capture/analysis: {activeCaptureTotal / activeSamples:F2}/{activeAnalysisTotal / activeSamples:F2} ms ({activeSamples} samples)");
        report.AppendLine(activeIntervals > 0
            ? $"Active sample interval mean/max: {activeIntervalTotal / activeIntervals:F2}/{activeIntervalMax:F2} ms | intervals: {activeIntervals} | over 24 ms: {activeGaps} (16 ms target)"
            : "Active sample interval: unavailable; need adjacent Active samples.");
        report.AppendLine($"Maximum image age: {ageMax:F2} ms | stale recorded samples (>40 ms): {stale} | active capture/analysis work over 16 ms: {slow}");
        report.AppendLine($"Trace records saved/dropped: {Volatile.Read(ref saved)}/{Volatile.Read(ref dropped)} | images saved/skipped: {Volatile.Read(ref imagesSaved)}/{Volatile.Read(ref imagesSkipped)}");
        report.AppendLine($"Recorded states: {string.Join(", ", states.Select(pair => $"{pair.Key}={pair.Value}"))}");
        report.AppendLine("\nMost frequent timing decisions:");
        foreach (var pair in reasons.OrderByDescending(p => p.Value).Take(6)) report.AppendLine($"- {pair.Value} samples: {pair.Key}");
        report.AppendLine("\nWhat to inspect:");
        if (samples == 0) report.AppendLine("- No captured samples. Check the stop reason, selected window and foreground state.");
        else if (!states.ContainsKey("Active")) report.AppendLine("- No active minigame was recognized. Inspect the raw frames and capture bounds in trace.jsonl.");
        if (latest.CaptureBackend != "None" && !latest.CaptureBackend.Contains("DXGI", StringComparison.OrdinalIgnoreCase)) report.AppendLine("- This backend has no verified presentation timestamp; timing candidates are withheld.");
        if (stale > 0) report.AppendLine("- Stale captures were observed. Compare frame age, accumulated frames and loop interval near the attempt.");
        if (activeGaps > 0) report.AppendLine("- Active sampling exceeded 24 ms. Compare sampleIntervalMilliseconds with loopMilliseconds and frame age to separate wait/capture delays from detector work.");
        if (policy != "Widest" && activeSamples > 0 && preferredVisibleSamples == 0)
            report.AppendLine(policy == "Custom"
                ? "- No color from the custom priority order was detected during Active play. Inspect saved regions."
                : policy == "RarestFirst"
                ? "- No yellow, red, purple, blue or common/white region was detected during Active play. Inspect saved regions."
                : policy == "PurpleBlueWhite"
                ? "- No purple, blue or common/white region was detected during Active play. Inspect saved regions."
                : $"- Preferred color {policy} was never detected during Active play. Inspect saved regions; choose Automatic or an available color for the next attempt.");
        if (Volatile.Read(ref dropped) > 0 || Volatile.Read(ref imagesSkipped) > 0) report.AppendLine("- Evidence is incomplete: the queue or storage budget skipped entries. Counters above show the loss.");
        if (Error is not null) report.AppendLine($"- Evidence error: {Error}");
        if (latest.Failure is not null) report.AppendLine($"\nException context:\n{latest.Failure}\n");
        report.AppendLine("- trace.jsonl contains sampled observations, region geometry, prediction deadlines, capture bounds, and Space state. frames.jsonl maps lossless panel PNGs to samples; imageRegion is the crop within the original captured frame. Subtract its X/Y from observation coordinates when inspecting a PNG.");
        report.AppendLine(latest.InputMode == "PrecisionAttempt"
            ? "- Precision mode allows one Space tap per explicit start. Narrow targets use an experimental center shot with nominal 8 ms game delay; slivers add the configured early correction after a measured first sweep. The full delay/error envelope may not fit. No guaranteed hit or automatic rearming. Final focus, geometry, physical Space, stop and 40 ms frame-age checks remain enforced."
            : latest.InputMode == "SingleAttempt"
            ? "- One-tap calibration mode allows at most one Space tap per explicitly armed run, after a new Preparing state. The 0–16 ms input-delay envelope is uncalibrated; correlate sent timestamps with the saved game result. No automatic rearming."
            : "- Automatic input is OFF. Candidates use a simulated 0–16 ms delay, not measured input latency. Space timestamps are sampled while Active; brief presses can be missed. A candidate does not prove a successful grab.");
        report.AppendLine($"\nLocal evidence: {DirectoryPath}");
        return report.ToString();
    }

    private async Task WriteAsync()
    {
        var outstanding = false;
        try
        {
            using var trace = new StreamWriter(Path.Combine(DirectoryPath, "trace.jsonl")) { AutoFlush = true };
            using var frames = new StreamWriter(Path.Combine(DirectoryPath, "frames.jsonl")) { AutoFlush = true };
            long traceBytes = 0, imageBytes = 0;
            await foreach (var entry in queue.Reader.ReadAllAsync())
            {
                outstanding = true;
                try
                {
                    string? imageName = null;
                    if (entry.Image is not null)
                    {
                        using var buffer = new MemoryStream();
                        entry.Image.Save(buffer, ImageFormat.Png);
                        var budget = entry.Critical ? MaximumImageBytes : MaximumImageBytes - CriticalImageReserveBytes;
                        if (imageBytes + buffer.Length <= budget)
                        {
                            imageName = $"frame-{entry.Status.SampleCount:00000}.png";
                            await File.WriteAllBytesAsync(Path.Combine(DirectoryPath, imageName), buffer.ToArray());
                            imageBytes += buffer.Length;
                            Interlocked.Increment(ref imagesSaved);
                            await frames.WriteLineAsync(JsonSerializer.Serialize(new { file = imageName, entry.ImageRegion, entry.Status.SampleCount, entry.Status.PresentationMilliseconds, entry.Status.MonotonicMilliseconds }, Json));
                        }
                        else Interlocked.Increment(ref imagesSkipped);
                    }
                    var line = JsonSerializer.Serialize(new { imageName, imageRegion = imageName is null ? (Rectangle?)null : entry.ImageRegion, status = entry.Status }, Json);
                    traceBytes += Encoding.UTF8.GetByteCount(line) + 1;
                    if (traceBytes <= MaximumTraceBytes) { await trace.WriteLineAsync(line); Interlocked.Increment(ref saved); }
                    else Interlocked.Increment(ref dropped);
                    outstanding = false;
                }
                finally { if (entry.Image is not null) { entry.Image.Dispose(); Interlocked.Decrement(ref pendingImages); } }
            }
        }
        catch (Exception failure) { if (outstanding) Interlocked.Increment(ref dropped); Volatile.Write(ref error, failure.Message); }
        finally
        {
            queue.Writer.TryComplete();
            while (queue.Reader.TryRead(out var entry))
            {
                Interlocked.Increment(ref dropped);
                if (entry.Image is not null) { entry.Image.Dispose(); Interlocked.Decrement(ref pendingImages); Interlocked.Increment(ref imagesSkipped); }
            }
        }
    }

    internal void Complete(PickpocketObserveStatus final)
    {
        if (Volatile.Read(ref completed) != 0) return;
        latest = final with { Debug = null };
        queue.Writer.TryComplete();
        writer.GetAwaiter().GetResult();
        Interlocked.Exchange(ref completed, 1);
        try
        {
            File.WriteAllText(Path.Combine(DirectoryPath, "summary.json"), JsonSerializer.Serialize(new { schemaVersion = 4, endedUtc = DateTime.UtcNow, status = latest, debug = Snapshot() }, Json));
            File.WriteAllText(Path.Combine(DirectoryPath, "REPORT.md"), Snapshot().Report);
        }
        catch (Exception failure) { Volatile.Write(ref error, failure.Message); }
    }

    public void Dispose() => Complete(latest);
}
