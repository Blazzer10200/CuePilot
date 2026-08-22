using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CuePilot;

internal sealed record LockpickingObserveStatus(
    bool Observing,
    string State,
    string Detail,
    int SampleCount,
    double Confidence,
    string CaptureBackend,
    double CaptureMilliseconds,
    string EvidenceDirectory,
    LockpickingObservation Observation,
    uint AccumulatedFrames = 1,
    LockpickingSpinTelemetry? Spin = null,
    bool InputEnabled = false,
    string VehicleClass = "",
    int ActionCount = 0,
    bool SpinInputActive = false)
{
    internal static LockpickingObserveStatus Stopped(string detail = "Lockpicking observation is stopped.") => new(
        false, "Stopped", detail, 0, 0, "None", 0, string.Empty, LockpickingObservation.Hidden());
}

internal sealed class LockpickingObserverEngine : IDisposable
{
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private IFrameSource? frameSource;
    private volatile bool observing;
    private LockpickingObserveStatus status = LockpickingObserveStatus.Stopped();
    private string evidenceDirectory = string.Empty;
    private string lastEvidenceKey = string.Empty;
    private int savedEvidenceCount;
    private int savedNonNumberedEvidenceCount;
    private int savedNumberedEvidenceCount;
    private int savedSpinEvidenceCount;
    private long lastSpinEvidenceTimestamp;
    private LockpickingClassController? automation;
    private bool inputEnabled;
    private string vehicleClass = string.Empty;

    private const int MaximumEvidenceFrames = 72;
    // Numbered bubbles are the calibration-critical phase. Keep a dense, lossless
    // HUD replay so one Observe run can be analysed frame-by-frame.
    private const int MaximumNumberedEvidenceFrames = 180;
    private const int MaximumSpinEvidenceFrames = 30;
    private static readonly long SpinEvidenceIntervalTicks = Math.Max(1, Stopwatch.Frequency / 12);

    internal event EventHandler<LockpickingObserveStatus>? StatusChanged;
    internal bool IsObserving => observing;
    internal LockpickingObserveStatus Status
    {
        get { lock (sync) return status; }
    }

    internal void Start(WindowTargetSettings requestedTarget, LockpickingClassProfile? classProfile = null)
    {
        lock (sync)
        {
            if (observing)
            {
                throw new InvalidOperationException("Lockpicking observation is already running.");
            }
            if (!requestedTarget.IsConfigured)
            {
                throw new InvalidOperationException("Select a FiveM target before observing lockpicking.");
            }

            if (classProfile is not null && !WindowTargetService.IsFiveMTarget(requestedTarget))
            {
                throw new InvalidOperationException($"Class {classProfile.VehicleClass} input requires a verified FiveM target.");
            }

            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            frameSource?.Dispose();
            frameSource = FrameSourceFactory.Create();
            automation?.Dispose();
            automation = classProfile is not null ? new LockpickingClassController(requestedTarget, classProfile) : null;
            inputEnabled = classProfile is not null;
            vehicleClass = classProfile?.VehicleClass ?? string.Empty;
            evidenceDirectory = Path.Combine(
                AppPaths.DiagnosticsDirectory,
                "lockpicking",
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(evidenceDirectory);
            lastEvidenceKey = string.Empty;
            savedEvidenceCount = 0;
            savedNonNumberedEvidenceCount = 0;
            savedNumberedEvidenceCount = 0;
            savedSpinEvidenceCount = 0;
            lastSpinEvidenceTimestamp = 0;
            observing = true;
            Publish(new LockpickingObserveStatus(
                true,
                "Searching",
                classProfile is not null
                    ? $"Class {classProfile.VehicleClass} input is armed. Return to FiveM; Pause / Break stops input immediately."
                    : "Watching the selected FiveM window for the lockpicking HUD.",
                0,
                0,
                frameSource.Name,
                0,
                evidenceDirectory,
                LockpickingObservation.Hidden(),
                InputEnabled: classProfile is not null,
                VehicleClass: vehicleClass));
            var target = requestedTarget.Copy();
            _ = RoutineWorker.Start(() => RunSafeAsync(target, cancellation.Token));
        }
    }

    internal void Stop(string detail = "Lockpicking observation stopped safely.")
    {
        cancellation?.Cancel();
        automation?.Stop();
        observing = false;
        inputEnabled = false;
        Publish(Status with
        {
            Observing = false,
            State = "Stopped",
            Detail = detail,
            InputEnabled = false,
            SpinInputActive = false,
        });
    }

    private async Task RunSafeAsync(WindowTargetSettings target, CancellationToken token)
    {
        var sampleCount = 0;
        var tracker = new LockpickingObservationTracker();
        var spinTracker = new LockpickingSpinTracker();
        LockpickingObservation? previousRawObservation = null;
        var hiddenAfterSpinCount = 0;
        var hiddenAfterActionCount = 0;
        var openCount = 0;
        var unexpectedCount = 0;
        var inputSessionActivated = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (inputEnabled && (NativeMethods.GetAsyncKeyState(NativeMethods.VkPause) & 0x8000) != 0)
                {
                    Stop("Emergency stop: Pause / Break released all Class C lockpicking input.");
                    return;
                }
                if (!WindowTargetService.TryResolve(target, out var resolved, out var resolveDetail))
                {
                    throw new InvalidOperationException(resolveDetail);
                }
                if (resolved.IsMinimized)
                {
                    throw new InvalidOperationException("FiveM is minimized. Observation stopped without input.");
                }
                if (!resolved.IsForeground)
                {
                    if (inputEnabled && inputSessionActivated)
                    {
                        throw new InvalidOperationException($"FiveM lost foreground focus. Class {vehicleClass} input stopped without guessing.");
                    }
                    Publish(Status with
                    {
                        Observing = true,
                        State = "Waiting",
                        Detail = inputEnabled
                            ? $"Class {vehicleClass} is armed without input. Return to FiveM to begin."
                            : "Observation is armed. Return to FiveM to begin capture.",
                        Observation = LockpickingObservation.Hidden("Waiting for FiveM to become foreground."),
                        SpinInputActive = false,
                    });
                    previousRawObservation = null;
                    tracker.Reset();
                    spinTracker.Reset();
                    await Task.Delay(100, token);
                    continue;
                }
                if (inputEnabled)
                {
                    inputSessionActivated = true;
                }

                var source = frameSource ?? throw new InvalidOperationException("No lockpicking frame source is configured.");
                var region = new Rectangle(Point.Empty, resolved.Bounds.Size);
                var clock = Stopwatch.StartNew();
                if (!source.TryCapture(target, region, out var frame, out var capture) || frame is null)
                {
                    throw new InvalidOperationException($"Lockpicking capture failed: {capture.Detail}");
                }

                using (frame)
                {
                    var rawObservation = LockpickingDetector.Analyze(frame.Bitmap, previousRawObservation);
                    previousRawObservation = rawObservation;
                    var observation = rawObservation;
                    sampleCount++;
                    var sampleTimestamp = Stopwatch.GetTimestamp();
                    if (!inputEnabled)
                    {
                        TrySaveTargetTrace(frame.Bitmap, sampleCount);
                    }
                    observation = tracker.Track(
                        observation,
                        sampleTimestamp,
                        capture.FrameAge,
                        Math.Max(capture.CaptureMilliseconds, clock.Elapsed.TotalMilliseconds),
                        capture.AccumulatedFrames);

                    var cursorVisible = NativeMethods.GetCursorPos(out var cursor);
                    var spin = spinTracker.Track(observation, cursorVisible, cursor, resolved.Bounds, sampleTimestamp);

                    if (automation is not null)
                    {
                        var update = await automation.HandleAsync(observation, resolved.Bounds, token);
                        observation = observation with
                        {
                            PredictedAction = update.PredictedAction,
                            Reason = update.Detail,
                        };
                    }

                    // Stop owns the terminal status. Never let an in-flight capture
                    // publish a stale live update after cancellation has released input.
                    token.ThrowIfCancellationRequested();

                    if (observation.State == LockpickingVisualState.Hidden && automation?.SpinStarted == true)
                    {
                        hiddenAfterSpinCount++;
                    }
                    else
                    {
                        hiddenAfterSpinCount = 0;
                    }
                    if (observation.State == LockpickingVisualState.Hidden && (automation?.ActionCount ?? 0) > 0)
                    {
                        hiddenAfterActionCount++;
                    }
                    else
                    {
                        hiddenAfterActionCount = 0;
                    }
                    openCount = observation.State == LockpickingVisualState.Open ? openCount + 1 : 0;
                    unexpectedCount = observation.State == LockpickingVisualState.Unexpected ? unexpectedCount + 1 : 0;

                    var state = observation.State == LockpickingVisualState.Hidden ? "Searching" : "Tracking";
                    TrySaveEvidence(frame.Bitmap, observation, spin, capture, sampleCount, sampleTimestamp);
                    if (spin is not null)
                    {
                        spin = spin with { CapturedFrames = savedSpinEvidenceCount };
                    }
                    var detail = $"{observation.State} · {observation.PredictedAction} · {observation.Reason}";
                    Publish(new LockpickingObserveStatus(
                        true,
                        state,
                        detail,
                        sampleCount,
                        observation.Confidence,
                        capture.Backend,
                        Math.Max(capture.CaptureMilliseconds, clock.Elapsed.TotalMilliseconds),
                        evidenceDirectory,
                        observation,
                        capture.AccumulatedFrames,
                        spin,
                        inputEnabled,
                        vehicleClass,
                        automation?.ActionCount ?? 0,
                        automation?.SpinActive ?? false));

                    if (openCount >= 3)
                    {
                        StopCompleted("Class C run completed: OPEN was visually confirmed.", observation);
                        return;
                    }
                    if (hiddenAfterSpinCount >= 3)
                    {
                        StopCompleted("Class C run ended safely after the lockpicking HUD disappeared.", observation);
                        return;
                    }
                    if (hiddenAfterActionCount >= 5)
                    {
                        StopCompleted("Class C input stopped because the HUD disappeared before completion was confirmed.", observation);
                        return;
                    }
                    if (automation is not null && unexpectedCount >= 3)
                    {
                        StopCompleted("Class C input stopped after three uncertain HUD frames.", observation);
                        return;
                    }
                }

                var remainingMilliseconds = 16 - clock.Elapsed.TotalMilliseconds;
                if (remainingMilliseconds >= 1)
                {
                    await Task.Delay((int)Math.Ceiling(remainingMilliseconds), token);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A normal stop is already published by Stop.
        }
        catch (Exception exception)
        {
            automation?.Stop();
            observing = false;
            inputEnabled = false;
            Publish(Status with
            {
                Observing = false,
                State = "Faulted",
                Detail = exception.Message,
                Observation = LockpickingObservation.Hidden(exception.Message),
                InputEnabled = false,
                SpinInputActive = false,
            });
        }
    }

    private void StopCompleted(string detail, LockpickingObservation observation)
    {
        automation?.Stop();
        observing = false;
        inputEnabled = false;
        Publish(Status with
        {
            Observing = false,
            State = "Stopped",
            Detail = detail,
            Observation = observation,
            InputEnabled = false,
            SpinInputActive = false,
        });
    }

    private void Publish(LockpickingObserveStatus value)
    {
        lock (sync) status = value;
        StatusChanged?.Invoke(this, value);
    }

    private void TrySaveEvidence(
        Bitmap frame,
        LockpickingObservation observation,
        LockpickingSpinTelemetry? spin,
        FrameSourceStatus capture,
        int sampleCount,
        long sampleTimestamp)
    {
        var evidenceKey = $"{observation.State}:{observation.Target?.Phase}:{observation.PredictedAction}";
        var saveNumberedFrame = observation.State == LockpickingVisualState.Numbered
            && savedNumberedEvidenceCount < MaximumNumberedEvidenceFrames;
        var saveSpinBurst = observation.State == LockpickingVisualState.Spin
            && savedSpinEvidenceCount < MaximumSpinEvidenceFrames
            && (lastSpinEvidenceTimestamp == 0 || sampleTimestamp - lastSpinEvidenceTimestamp >= SpinEvidenceIntervalTicks);
        if (!saveNumberedFrame
            && !saveSpinBurst
            && (savedNonNumberedEvidenceCount >= MaximumEvidenceFrames || evidenceKey == lastEvidenceKey))
        {
            return;
        }

        try
        {
            var sequence = savedEvidenceCount + 1;
            var kind = saveSpinBurst ? "spinburst" : observation.State.ToString().ToLowerInvariant();
            var stem = $"{sequence:00}-{kind}-{sampleCount:000000}";
            var croppedHudEvidence = saveSpinBurst || saveNumberedFrame;
            var evidenceRegion = croppedHudEvidence ? HudEvidenceRegion(frame, observation) : new Rectangle(0, 0, frame.Width, frame.Height);
            using var evidence = new Bitmap(evidenceRegion.Width, evidenceRegion.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(evidence))
            {
                graphics.DrawImage(
                    frame,
                    new Rectangle(0, 0, evidence.Width, evidence.Height),
                    evidenceRegion,
                    GraphicsUnit.Pixel);
            }
            var imageFormat = croppedHudEvidence ? ImageFormat.Png : ImageFormat.Jpeg;
            var extension = croppedHudEvidence ? "png" : "jpg";
            evidence.Save(Path.Combine(evidenceDirectory, $"{stem}.{extension}"), imageFormat);
            File.AppendAllText(
                Path.Combine(evidenceDirectory, "events.jsonl"),
                JsonSerializer.Serialize(new
                {
                    capturedAt = DateTimeOffset.Now,
                    sampleCount,
                    frame = new { frame.Width, frame.Height },
                    evidenceRegion = new { evidenceRegion.Left, evidenceRegion.Top, evidenceRegion.Width, evidenceRegion.Height },
                    evidenceFormat = extension,
                    capture = new
                    {
                        capture.Backend,
                        frameAgeMilliseconds = capture.FrameAge.TotalMilliseconds,
                        capture.CaptureMilliseconds,
                        capture.AccumulatedFrames,
                    },
                    spin,
                    observation,
                }) + Environment.NewLine);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException)
        {
            // Observation remains available even if optional local evidence cannot be persisted.
        }
    }

    private static Rectangle HudEvidenceRegion(Bitmap frame, LockpickingObservation observation)
    {
        var minimum = Math.Min(frame.Width, frame.Height);
        var radius = observation.HudRadius * minimum * 1.18;
        var desired = Math.Max(1, (int)Math.Ceiling(radius * 2));
        var width = Math.Min(frame.Width, desired);
        var height = Math.Min(frame.Height, desired);
        var left = Math.Clamp((int)Math.Round(observation.HudCenterX * frame.Width - width / 2d), 0, frame.Width - width);
        var top = Math.Clamp((int)Math.Round(observation.HudCenterY * frame.Height - height / 2d), 0, frame.Height - height);
        return new Rectangle(left, top, width, height);
    }

    private void TrySaveTargetTrace(Bitmap frame, int sampleCount)
    {
        try
        {
            var trace = LockpickingDetector.TraceTargets(frame);
            File.AppendAllText(
                Path.Combine(evidenceDirectory, "candidate-trace.jsonl"),
                JsonSerializer.Serialize(new
                {
                    diagnostic = "lockpick-target-trace-v1",
                    capturedAt = DateTimeOffset.Now,
                    sampleCount,
                    trace,
                }) + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException)
        {
            // Optional local diagnostics must not interrupt observation.
        }
    }

    public void Dispose()
    {
        Stop();
        automation?.Dispose();
        cancellation?.Dispose();
        frameSource?.Dispose();
    }
}
