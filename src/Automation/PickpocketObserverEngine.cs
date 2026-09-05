using System.Diagnostics;
using System.Drawing;

namespace CuePilot;

internal sealed record PickpocketObserveStatus(
    bool Observing, string State, string Detail, PickpocketObservation Observation,
    int SampleCount = 0, double CaptureMilliseconds = 0, double AnalysisMilliseconds = 0,
    double? FrameAgeMilliseconds = null, double PresentationMilliseconds = 0,
    string CaptureBackend = "None", int? SelectedBandIndex = null, string TargetPolicy = "Widest",
    PickpocketTimingPrediction? Prediction = null, int PredictedPressCount = 0,
    long CooldownUntilUnixMs = 0, string EvidenceDirectory = "", int Attempt = 0,
    double MonotonicMilliseconds = 0, double LoopMilliseconds = 0, double SampleIntervalMilliseconds = 0,
    Rectangle CaptureRegion = default, Rectangle WindowBounds = default, uint AccumulatedFrames = 0,
    bool? ManualSpaceDown = null, int ManualSpacePressCount = 0, double? LastSpaceObservedMilliseconds = null,
    PickpocketDebugStatus? Debug = null, string CaptureDetail = "", int FrameWidth = 0, int FrameHeight = 0, string? Failure = null,
    string InputMode = "Observe", bool InputArmed = false, int AutomatedPressCount = 0, PickpocketInputDelivery? InputDelivery = null,
    int RedAdvanceMs = 8, int YellowAdvanceMs = 20,
    IReadOnlyList<PickpocketRecentAttempt>? RecentAttempts = null, string? SessionStateError = null,
    IReadOnlyList<PickpocketBandColor>? CustomPriority = null, IReadOnlyList<string>? ItemPriority = null)
{
    internal static PickpocketObserveStatus Stopped => new(false, "Stopped", "Ready for a live observation test. Input is off.", PickpocketObservation.Missing);
}

internal sealed class PickpocketObserverEngine : IDisposable
{
    private readonly object sync = new();
    private readonly OwnedRoutineWorker worker = new();
    private readonly PickpocketAttemptTracker attempts = new();
    private readonly Func<IFrameSource> createSource;
    private readonly Func<WindowTargetSettings, WindowTargetService.ResolvedWindowTarget> resolve;
    private readonly Func<WindowTargetSettings, WindowTargetService.ResolvedWindowTarget, bool> validateCapturedWindow;
    private readonly Func<IntPtr, bool> spaceDown;
    private readonly Func<string, WindowTargetSettings, PickpocketDiagnosticSession> createDiagnostics;
    private readonly PickpocketInputController input;
    private readonly Func<Bitmap, PickpocketObservation?, PickpocketObservation> analyze;
    private readonly Func<double> clockNow;
    private readonly Action<double, CancellationToken>? waitUntil;
    private readonly PickpocketSessionState sessionState;
    private string stopReason = "Pickpocket stopped. Input is disarmed.";
    private PickpocketObserveStatus status = PickpocketObserveStatus.Stopped;
    internal event EventHandler<PickpocketObserveStatus>? StatusChanged;
    internal PickpocketObserveStatus Status { get { lock (sync) return status; } }
    internal bool IsObserving => worker.IsRunning;
    internal object History(int page, string outcome) => sessionState.Query(page, outcome);

    internal PickpocketObserverEngine(Func<IFrameSource>? createSource = null,
        Func<WindowTargetSettings, WindowTargetService.ResolvedWindowTarget>? resolve = null,
        Func<IntPtr, bool>? spaceDown = null,
        Func<string, WindowTargetSettings, PickpocketDiagnosticSession>? createDiagnostics = null,
        PickpocketInputController? input = null,
        Func<Bitmap, PickpocketObservation?, PickpocketObservation>? analyze = null,
        Func<double>? clockNow = null, Action<double, CancellationToken>? waitUntil = null,
        Func<WindowTargetSettings, WindowTargetService.ResolvedWindowTarget, bool>? validateCapturedWindow = null,
        PickpocketSessionState? sessionState = null)
    {
        this.createSource = createSource ?? FrameSourceFactory.Create;
        this.resolve = resolve ?? (target => WindowTargetService.TryResolve(target, out var result, out var detail)
            ? result : throw new InvalidOperationException(detail));
        this.validateCapturedWindow = validateCapturedWindow ?? (resolve is null
            ? (_, captured) => WindowTargetService.IsUnchangedForeground(captured)
            : (target, captured) =>
            {
                // Preserve explicitly injected window resolution in offline tests.
                var current = resolve(target);
                return current.IsForeground && !current.IsMinimized && current.Handle == captured.Handle
                    && current.ProcessId == captured.ProcessId && current.Bounds == captured.Bounds;
            });
        this.spaceDown = spaceDown ?? (handle => NativeMethods.GetForegroundWindow() == handle && (NativeMethods.GetAsyncKeyState(0x20) & 0x8000) != 0);
        this.createDiagnostics = createDiagnostics ?? ((policy, target) => new(policy, target));
        this.input = input ?? new();
        this.analyze = analyze ?? ((bitmap, prior) => PickpocketDetector.Analyze(bitmap, previous: prior));
        this.clockNow = clockNow ?? (() => PickpocketSampleClock.NowMilliseconds);
        this.waitUntil = waitUntil;
        this.sessionState = sessionState ?? new();
        var remaining = this.sessionState.RemainingMs;
        attempts.RestoreCooldown(remaining, NowMs());
        status = status with { CooldownUntilUnixMs = remaining > 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + remaining : 0,
            RecentAttempts = this.sessionState.Recent, SessionStateError = this.sessionState.Error };
    }

    internal void Configure(string policy, string inputMode = "Observe", int redAdvanceMs = 8, int yellowAdvanceMs = 20,
        IReadOnlyList<PickpocketBandColor>? customPriority = null, IReadOnlyList<string>? itemPriority = null)
    {
        var preferences = new PickpocketPreferences { TargetPolicy = policy, InputMode = inputMode, RedAdvanceMs = redAdvanceMs, YellowAdvanceMs = yellowAdvanceMs };
        if (customPriority is not null) preferences.CustomPriority = customPriority.ToArray();
        if (itemPriority is not null) preferences.ItemPriority = itemPriority.ToArray();
        preferences.Validate();
        if (IsObserving) throw new InvalidOperationException("Stop observation before changing its target policy.");
        Publish(Status with { TargetPolicy = policy, InputMode = inputMode, InputArmed = false, RedAdvanceMs = redAdvanceMs, YellowAdvanceMs = yellowAdvanceMs, CustomPriority = preferences.CustomPriority, ItemPriority = preferences.ItemPriority,
            Detail = inputMode != "Observe" ? "One-tap mode selected. Press F7 in FiveM before starting a new minigame." : "Observation selected. Space remains manual." });
    }

    internal void Start(WindowTargetSettings target)
    {
        if (!WindowTargetService.IsFiveMTarget(target)) throw new InvalidOperationException("Select a FiveM window first.");
        if (IsObserving) throw new InvalidOperationException("Pickpocket observation is already running or still stopping.");
        var copy = target.Copy();
        var policy = Status.TargetPolicy;
        var mode = Status.InputMode;
        var redAdvance = Status.RedAdvanceMs;
        var yellowAdvance = Status.YellowAdvanceMs;
        var customPriority = Status.CustomPriority?.ToArray() ?? new PickpocketPreferences().CustomPriority;
        var itemPriority = Status.ItemPriority?.ToArray() ?? new PickpocketPreferences().ItemPriority;
        input.BeginRun();
        attempts.ResetMotion();
        stopReason = "Pickpocket stopped. Input is disarmed.";
        Publish(PickpocketObserveStatus.Stopped with { Observing = true, State = "Waiting", TargetPolicy = policy, CooldownUntilUnixMs = Status.CooldownUntilUnixMs,
            InputMode = mode, InputArmed = mode != "Observe", RedAdvanceMs = redAdvance, YellowAdvanceMs = yellowAdvance, CustomPriority = customPriority, ItemPriority = itemPriority,
            Detail = mode != "Observe" ? "One-tap mode armed. Return to FiveM and start a new pickpocket." : "Return to FiveM to begin observation. No input will be sent." });
        worker.Start(async (_, token) => await RunAsync(copy, policy, mode, redAdvance, yellowAdvance, customPriority, itemPriority, token));
    }

    internal void Stop(string detail = "Pickpocket stopped. Input is disarmed.")
    {
        Volatile.Write(ref stopReason, detail);
        if (!input.Stop()) Volatile.Write(ref stopReason, "Input cleanup failed; stopping retries owned Space release.");
        var complete = worker.Stop();
        if (complete && !Status.Observing)
        {
            if (Status.InputArmed) Publish(Status with { InputArmed = false });
            return;
        }
        Publish(Status with { Observing = false, InputArmed = false, State = complete ? "Stopped" : "Faulted", Detail = complete ? detail : "Observation is still stopping; restart is blocked until cleanup finishes.", Prediction = null, Observation = PickpocketObservation.Missing });
    }

    private async Task RunAsync(WindowTargetSettings target, string policy, string mode, int redAdvance, int yellowAdvance, PickpocketBandColor[] customPriority, string[] itemPriority, CancellationToken token)
    {
        var automatic = mode != "Observe";
        var precision = mode == "PrecisionAttempt";
        PickpocketDiagnosticSession? diagnostics = null;
        var latest = Status;
        try
        {
            diagnostics = createDiagnostics(policy, target);
            diagnostics.SetInputMode(mode);
            latest = latest with { EvidenceDirectory = diagnostics.DirectoryPath };
            diagnostics.Queue(latest, null, false);
            latest = latest with { Debug = diagnostics.Snapshot() };
            Publish(latest);
            using var source = createSource();
            using var sampleClock = new PickpocketSampleClock();
            Action<double, CancellationToken> wait = waitUntil ?? sampleClock.WaitUntil;
            Action<double, CancellationToken> inputWait = waitUntil ?? sampleClock.WaitForInputDeadline;
            var predictor = new PickpocketTimingPredictor();
            PickpocketObservation? previous = null;
            var targetTracker = new PickpocketTargetTracker();
            var itemReader = new PickpocketItemReader();
            var sweepTracker = new PickpocketSweepTracker();
            int? selected = null;
            PickpocketBand? lastTarget = null;
            int? shotAttempt = null;
            var samples = 0;
            var predictions = 0;
            var lastPublish = 0d;
            var lastImage = 0d;
            var lastTrace = 0d;
            var lastLoop = 0d;
            var spaceWasDown = false;
            var manualPresses = 0;
            double? lastSpace = null;
            var start = NowMs();
            var foregroundSeen = false;
            var preparationSeen = false;
            while (!token.IsCancellationRequested && NowMs() - start < 600_000)
            {
                var window = resolve(target);
                if (window.IsMinimized) throw new InvalidOperationException("FiveM was minimized. Observation stopped.");
                if (!window.IsForeground)
                {
                    if (foregroundSeen) throw new InvalidOperationException("FiveM lost focus. Observation stopped; predictions cleared.");
                    if (diagnostics.Error != latest.Debug?.Error)
                    {
                        latest = latest with { Debug = diagnostics.Snapshot() };
                        Publish(latest);
                    }
                    await Task.Delay(100, token);
                    continue;
                }
                foregroundSeen = true;
                var loopStart = NowMs();
                var sampleInterval = lastLoop == 0 ? 0 : loopStart - lastLoop;
                lastLoop = loopStart;
                var region = new Rectangle((int)(window.Bounds.Width * .15), (int)(window.Bounds.Height * .62),
                    (int)(window.Bounds.Width * .70), (int)(window.Bounds.Height * .36));
                if (!source.TryCapture(target, region, out var frame, out var capture) || frame is null)
                    throw new InvalidOperationException(capture.Detail);
                using (frame)
                {
                    var capturedAt = NowMs();
                    // Cleanup and fallback health checks happen after FrameAge is
                    // measured. Keep DXGI's original QPC image time so that work
                    // cannot shift the motion fit or make a stale image look fresh.
                    var presentation = capture.PresentationMilliseconds
                        ?? (capture.FrameAge == TimeSpan.MaxValue ? double.NaN : capturedAt - capture.FrameAge.TotalMilliseconds);
                    var observation = analyze(frame.Bitmap, previous);
                    observation = itemReader.Annotate(frame.Bitmap, observation, presentation);
                    var analyzedAt = NowMs();
                    token.ThrowIfCancellationRequested();
                    sweepTracker.Observe(observation, presentation);
                    bool? manualDown = observation.State == PickpocketVisualState.Active ? spaceDown(window.Handle) : null;
                    var spaceEdge = manualDown == true && !spaceWasDown;
                    if (spaceEdge) { manualPresses++; lastSpace = NowMs(); }
                    if (automatic && spaceEdge) input.Disarm("Manual Space observed; automatic input cancelled for this run.");
                    if (manualDown.HasValue) spaceWasDown = manualDown.Value;
                    samples++;
                    var completion = attempts.Observe(observation.State, analyzedAt);
                    if (automatic && completion) input.Disarm("Attempt ended; arm a new test explicitly.");
                    if (observation.State == PickpocketVisualState.Preparing && previous?.State != PickpocketVisualState.Preparing)
                    { predictor.Reset(); selected = null; lastTarget = null; targetTracker.Reset(); preparationSeen = true; spaceWasDown = false; }
                    if (observation.State == PickpocketVisualState.Hidden)
                    {
                        selected = null;
                        targetTracker.Reset();
                        predictor.Observe(observation, PickpocketBandColor.White, presentation, analyzedAt, new(0, 16));
                    }
                    var selectionRevision = targetTracker.Revision;
                    selected = targetTracker.Observe(observation, policy, presentation, customPriority, itemPriority);
                    if (observation.State == PickpocketVisualState.Active && selected is int targetIndex && targetIndex < observation.Bands.Count)
                        lastTarget = observation.Bands[targetIndex];
                    // A newly confirmed/reacquired target needs its own fresh motion fit.
                    // Never clear the one-tap latch when changing target geometry.
                    if (selectionRevision != targetTracker.Revision && input.PressCount == 0 && predictions == 0) predictor.Reset();
                    var cooldown = attempts.RemainingMs(analyzedAt);
                    var prediction = PickpocketTimingPrediction.Wait(policy != "Widest" && observation.State == PickpocketVisualState.Active
                        && ResolveTargetColor(observation, policy, customPriority) is null
                        ? IsPriorityPolicy(policy) ? "No color from the selected priority order is visible. Waiting for an eligible target."
                            : $"No {policy} region in this attempt. Choose Automatic or a visible color for the next test."
                        : targetTracker.Reason);
                    if (cooldown > 0) prediction = PickpocketTimingPrediction.Wait("Three-minute cooldown is active.");
                    else if (selected is int index && index < observation.Bands.Count)
                    {
                        // 0–16 ms is a test envelope only. GDI has no presentation
                        // timestamp, so do not issue a timing candidate from it.
                        prediction = capture.Backend.Contains("DXGI", StringComparison.OrdinalIgnoreCase)
                            ? predictor.Observe(observation, observation.Bands[index].Color, presentation, analyzedAt, new(0, 16), index, precision,
                                observation.Bands[index].Color == PickpocketBandColor.Yellow ? yellowAdvance : observation.Bands[index].Color == PickpocketBandColor.Red ? redAdvance : 8)
                            : PickpocketTimingPrediction.Wait("Capture backend lacks a verified presentation timestamp.");
                        if (precision && PickpocketSweepTracker.IsTiny(observation, index) && !sweepTracker.FirstPassComplete)
                            prediction = PickpocketTimingPrediction.Wait("Small target: observing the first sweep and confirming its turnaround before one timed shot.",
                                prediction.SpeedPixelsPerSecond, prediction.UncertaintyPixels);
                    }
                    var countBefore = input.PressCount;
                    if (policy != "Widest" && !itemReader.ReadyToChoose && observation.Bands.GroupBy(b => b.Color).Any(g => g.Count() > 1))
                        prediction = PickpocketTimingPrediction.Wait("Reading item cards to resolve the same-color priority.");
                    if (automatic)
                    {
                        if (diagnostics.Error is not null) input.Disarm("Automatic input cancelled because debug evidence could not be saved.");
                        if (!preparationSeen) prediction = PickpocketTimingPrediction.Wait("Waiting for a new preparation state before automatic input.");
                        else if (!precision && prediction.CanSchedule && prediction.LatestPressAtMs - prediction.PressAtMs < 35)
                            prediction = PickpocketTimingPrediction.Wait("One-tap test skips this narrow window: less than 35 ms of delivery margin.", prediction.SpeedPixelsPerSecond, prediction.UncertaintyPixels);
                        else if (!input.Consumed && cooldown == 0 && observation.State == PickpocketVisualState.Active && selected is not null)
                        {
                            input.TryTap(prediction, presentation, () =>
                            {
                                return validateCapturedWindow(target, window)
                                    && attempts.RemainingMs(NowMs()) == 0 && !spaceDown(window.Handle);
                            }, inputWait, token, precision);
                        }
                        if (input.PressCount > countBefore) { predictions++; predictor.MarkAttempted(); shotAttempt = attempts.Attempt; }
                    }
                    else if (prediction.CanSchedule) { predictions++; predictor.MarkAttempted(); }
                    var next = new PickpocketObserveStatus(true, cooldown > 0 ? "Cooldown" : observation.State == PickpocketVisualState.Hidden ? "Searching" : "Tracking",
                        diagnostics.Error is { } evidenceError ? $"Evidence saving failed: {evidenceError}"
                            : cooldown > 0 ? "Three-minute cooldown is active. Input is disarmed." : input.Delivery?.Detail ?? prediction.Reason,
                        observation, samples, capture.CaptureMilliseconds, analyzedAt - capturedAt,
                        double.IsFinite(presentation) ? analyzedAt - presentation : null, double.IsFinite(presentation) ? presentation : 0,
                        capture.Backend, selected, policy, prediction, predictions,
                        cooldown > 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)cooldown : 0, diagnostics.DirectoryPath, attempts.Attempt,
                        analyzedAt, NowMs() - loopStart, sampleInterval, region, window.Bounds, capture.AccumulatedFrames,
                        manualDown, manualPresses, lastSpace, CaptureDetail: capture.Detail, FrameWidth: frame.Bitmap.Width, FrameHeight: frame.Bitmap.Height,
                        InputMode: mode, InputArmed: automatic && !input.Consumed, AutomatedPressCount: input.PressCount, InputDelivery: input.Delivery,
                        RedAdvanceMs: redAdvance, YellowAdvanceMs: yellowAdvance, CustomPriority: customPriority, ItemPriority: itemPriority);
                    // Null previous means acquisition, not a new Hidden transition on every frame.
                    var changed = observation.State != latest.Observation.State;
                    var critical = changed || prediction.CanSchedule || completion || spaceEdge || input.PressCount > countBefore;
                    if (critical || observation.State == PickpocketVisualState.Active || analyzedAt - lastTrace >= 500)
                    {
                        var saveImage = critical || analyzedAt - lastImage >= (observation.State == PickpocketVisualState.Active ? 50 : 5000);
                        diagnostics.Queue(next, frame.Bitmap, saveImage, critical);
                        if (completion)
                        {
                            var hasShot = shotAttempt == attempts.Attempt;
                            sessionState.Complete(next with { AutomatedPressCount = hasShot ? 1 : 0 },
                                hasShot ? diagnostics.Snapshot().Result : null, lastTarget);
                        }
                        if (saveImage) lastImage = analyzedAt;
                        lastTrace = analyzedAt;
                    }
                    next = next with { Detail = PickpocketProgress.Describe(next, preparationSeen, sweepTracker.FirstPassComplete) };
                    latest = next;
                    if (critical || analyzedAt - lastPublish >= 100)
                    {
                        Publish(next with { Debug = diagnostics.Snapshot() });
                        lastPublish = analyzedAt;
                    }
                    previous = observation.State == PickpocketVisualState.Hidden ? null : observation;
                }
                var interval = previous?.State == PickpocketVisualState.Active && attempts.RemainingMs(NowMs()) == 0 ? 16 : 67;
                if (interval == 16 || waitUntil is not null) wait(loopStart + interval, token);
                else await Task.Delay(Math.Max(2, (int)Math.Ceiling(interval - (NowMs() - loopStart))), token);
            }
            token.ThrowIfCancellationRequested();
            latest = latest with { Observing = false, State = "Stopped", Detail = "Ten-minute observation limit reached.", Prediction = null };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { latest = latest with { Observing = false, State = "Stopped", Detail = Volatile.Read(ref stopReason), Prediction = null }; }
        catch (Exception exception)
        {
            var failure = exception.ToString();
            latest = latest with { Observing = false, State = "Faulted", Detail = exception.Message, Prediction = null, Failure = failure[..Math.Min(failure.Length, 8000)] };
        }
        finally
        {
            var released = input.Stop();
            latest = latest with { InputArmed = false, AutomatedPressCount = input.PressCount, InputDelivery = input.Delivery };
            if (!released) latest = latest with { State = "Faulted", Detail = "Space release failed. Input is disarmed; use Stop to retry cleanup." };
            // Preserve the last observation in the report even after focus/capture failure.
            if (diagnostics is not null)
            {
                diagnostics.Complete(latest);
                latest = latest with { Debug = diagnostics.Snapshot() };
            }
            else latest = latest with { Debug = new("Error", 0, 0, 0, 0, latest.Detail, $"Recorder could not start: {latest.Detail}") };
            Publish(latest with { Observing = false, Observation = PickpocketObservation.Missing });
        }
    }

    private static readonly PickpocketBandColor[] RarityOrder = [PickpocketBandColor.Yellow, PickpocketBandColor.Red,
        PickpocketBandColor.Purple, PickpocketBandColor.Blue, PickpocketBandColor.White];
    private static readonly PickpocketBandColor[] PurpleOrder = [PickpocketBandColor.Purple, PickpocketBandColor.Blue, PickpocketBandColor.White];
    internal static bool IsPriorityPolicy(string policy) => policy is "RarestFirst" or "PurpleBlueWhite" or "Custom";

    internal static PickpocketBandColor? ResolveTargetColor(PickpocketObservation observation, string policy, IReadOnlyList<PickpocketBandColor>? customPriority = null)
    {
        if (IsPriorityPolicy(policy))
        {
            foreach (var color in policy == "Custom" ? customPriority ?? RarityOrder : policy == "RarestFirst" ? RarityOrder : PurpleOrder)
                if (observation.Bands.Any(b => b.Color == color)) return color;
            return null;
        }
        return observation.Bands.Where(b => b.Color.ToString() == policy).Select(b => (PickpocketBandColor?)b.Color).FirstOrDefault();
    }

    internal static int? SelectBand(PickpocketObservation observation, string policy, int direction = 1, IReadOnlyList<PickpocketBandColor>? customPriority = null, IReadOnlyList<string>? itemPriority = null)
    {
        var color = ResolveTargetColor(observation, policy, customPriority);
        // Priority is based on presence across the entire bar. Confirm a passed target
        // too, so the motion predictor can wait for its return instead of downgrading.
        return observation.Bands.Select((band, index) => (band, index))
            .Where(entry => (IsPriorityPolicy(policy) || (policy != "Widest" && itemPriority is { Count: > 0 }) || (direction > 0
                    ? entry.band.Right > observation.MarkerX + 3 : entry.band.Left < observation.MarkerX - 3))
                && (policy == "Widest" || entry.band.Color == color))
            .OrderBy(entry => policy == "Widest" ? 0 : PickpocketItemReader.Rank(entry.band.ItemName, itemPriority))
            .ThenByDescending(entry => entry.band.Width).Select(entry => (int?)entry.index).FirstOrDefault();
    }

    internal static int? MatchBand(PickpocketObservation observation, PickpocketBand chosen)
    {
        var tolerance = 6 * observation.Bar.Width / 576d;
        var matches = observation.Bands.Select((band, index) => (band, index)).Where(entry => entry.band.Color == chosen.Color
            && Math.Abs(entry.band.Left - chosen.Left) <= tolerance && Math.Abs(entry.band.Right - chosen.Right) <= tolerance).ToArray();
        return matches.Length == 1 ? matches[0].index : null;
    }

    private double NowMs() => clockNow();
    private void Publish(PickpocketObserveStatus next)
    {
        next = next with { RecentAttempts = sessionState.Recent, SessionStateError = sessionState.Error };
        lock (sync) status = next;
        StatusChanged?.Invoke(this, next);
    }
    public void Dispose() { Stop(); worker.Dispose(); }
}
