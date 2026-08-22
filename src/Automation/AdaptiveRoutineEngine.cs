using System.Diagnostics;

namespace CuePilot;

internal static class RoutineWorker
{
    internal static Task Start(Func<Task> work) => Task.Run(work);
}

internal sealed class AdaptiveRoutineEngine : IDisposable
{
    private const int MaximumPromptPressAttempts = 5;
    private const int CastAccelerationPulseMilliseconds = 40;
    private readonly object sync = new();
    private FishingRoutineSettings settings = new();
    private CancellationTokenSource? cancellation;
    private IFrameSource? frameSource;
    private TargetInputRouter input = new(InputDeliveryMode.Automatic);
    private readonly FishingMeterTracker meterTracker = new();
    private FishingDebugSession? latestDebugSession;
    private volatile RoutineState state = RoutineState.Stopped;

    internal event EventHandler<RoutineStatus>? StatusChanged;
    internal RoutineState State => state;
    internal FishingDebugSnapshot? DebugSnapshot => latestDebugSession?.Snapshot;

    internal void Arm(FishingRoutineSettings requestedSettings)
    {
        lock (sync)
        {
            if (State is not (RoutineState.Stopped or RoutineState.Faulted))
            {
                throw new InvalidOperationException("The fishing routine is already running.");
            }

            if (!requestedSettings.TargetWindow.IsConfigured)
            {
                throw new InvalidOperationException("Capture the game target before arming fishing feedback.");
            }

            settings = requestedSettings.Copy();
            settings.Clamp();
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            frameSource?.Dispose();
            frameSource = FrameSourceFactory.Create();
            meterTracker.Reset();
            input = new TargetInputRouter(settings.InputMode);
            var debugSession = new FishingDebugSession(settings);
            latestDebugSession = debugSession;
            debugSession.SetStage("Preflight", "Resolving the target application.");
            SetState(RoutineState.Casting, "Preflight: resolving the target application.");
            _ = RoutineWorker.Start(() => RunRoutineSafeAsync(cancellation.Token, debugSession));
        }
    }

    internal void Stop(string reason = "Fishing routine stopped.")
    {
        latestDebugSession?.Record("control", "stop_requested", new { reason });
        cancellation?.Cancel();
        ReleaseLeftButton();
        SetState(RoutineState.Stopped, reason);
    }

    private async Task RunRoutineSafeAsync(CancellationToken token, FishingDebugSession debugSession)
    {
        var outcome = "Completed";
        try
        {
            await PreflightAsync(token, debugSession);
            await WaitForPromptAndPressAsync(FishingPromptKind.Cast, token, debugSession);
            FishingLoopDiagnosticLog.Write("cast_prompt_cleared");

            while (!token.IsCancellationRequested)
            {
                SetState(RoutineState.Armed,
                    $"Cast started. Waiting {settings.FishingCastAccelerationDelayMilliseconds / 1_000d:0.0}s for the cast-bar acceleration window.");
                debugSession.SetStage("CastingBar", "Waiting for the guarded one-click cast acceleration window.");
                var meterOutcome = await WaitForMeterAsync(token, debugSession);
                if (meterOutcome == MeterWaitOutcome.CastPromptReady)
                {
                    FishingLoopDiagnosticLog.Write("cast_retry_requested", "Cast prompt remained visible while waiting for the meter; resynchronizing.");
                    await WaitForPromptAndPressAsync(FishingPromptKind.Cast, token, debugSession);
                    continue;
                }

                if (meterOutcome == MeterWaitOutcome.CollectPromptReady)
                {
                    FishingLoopDiagnosticLog.Write("collect_resync_requested", "Collect prompt appeared while waiting for the meter; resynchronizing.");
                    meterTracker.Reset();
                    await WaitForPromptAndPressAsync(FishingPromptKind.Collect, token, debugSession);
                    await WaitForPromptAndPressAsync(FishingPromptKind.Cast, token, debugSession);
                    continue;
                }

                FishingLoopDiagnosticLog.Write("meter_lock");
                await EnsureInputReadyAsync(token);
                SetState(RoutineState.Regulating, "Fishing meter detected. Running bounded feedback.");
                debugSession.SetStage("Regulating", "Meter locked; running bounded tension control.");
                var result = await Task.Run(() => RegulateFishingMeter(token, debugSession), CancellationToken.None);
                token.ThrowIfCancellationRequested();
                meterTracker.Reset();
                debugSession.Record("meter", "tracker_reset", new { reason = "regulation_ended", result.ShouldCollect });
                FishingLoopDiagnosticLog.Write(result.ShouldCollect ? "meter_complete" : "meter_reacquire_failed", result.Detail);

                if (!result.ShouldCollect)
                {
                    SetState(RoutineState.Stowing, $"{result.Detail} Waiting for FiveM to offer the next cast.");
                    await WaitForPromptAndPressAsync(FishingPromptKind.Cast, token, debugSession);
                    continue;
                }

                SetState(RoutineState.Collecting, result.Detail);
                await Task.Delay(settings.CollectDelayMilliseconds, token);
                await WaitForPromptAndPressAsync(FishingPromptKind.Collect, token, debugSession);
                FishingLoopDiagnosticLog.Write("collect_prompt_cleared");
                await WaitForPromptAndPressAsync(FishingPromptKind.Cast, token, debugSession);
                FishingLoopDiagnosticLog.Write("cast_prompt_cleared");
            }
        }
        catch (OperationCanceledException)
        {
            outcome = "Stopped safely";
            ReleaseLeftButton();
            if (State != RoutineState.Stopped)
            {
                SetState(RoutineState.Stopped, "Fishing routine cancelled safely.");
            }
        }
        catch (Exception exception)
        {
            outcome = $"Faulted: {exception.Message}";
            debugSession.Record("fault", "exception", new { type = exception.GetType().Name, exception.Message });
            ReleaseLeftButton();
            SetState(RoutineState.Faulted, exception.Message);
        }
        finally
        {
            debugSession.Complete(outcome);
            Raise(new RoutineStatus(State, outcome));
            debugSession.Dispose();
        }
    }

    private async Task PreflightAsync(CancellationToken token, FishingDebugSession debugSession)
    {
        var source = frameSource ?? throw new InvalidOperationException("No frame source is configured.");
        SetState(RoutineState.Casting, "Preflight: checking target, input, and live desktop capture.");
        var verification = await FishingSetupVerifier.VerifyAsync(settings, source, input, activateTarget: true, token: token);
        debugSession.Record("preflight", "setup_verified", verification);
        if (!verification.Ready)
        {
            throw new InvalidOperationException($"Setup preflight failed: {verification.Detail}");
        }

        SetState(RoutineState.Casting,
            verification.Detail);
        debugSession.Record("preflight", "ready", verification);
        await Task.Delay(150, token);
    }

    private async Task<MeterWaitOutcome> WaitForMeterAsync(CancellationToken token, FishingDebugSession debugSession)
    {
        var meterGate = new FishingMeterStabilityGate();
        var castAccelerationGate = new FishingCastAccelerationGate(settings.FishingCastAccelerationDelayMilliseconds);
        var castPromptGate = new FishingPromptStabilityGate(FishingPromptKind.Cast);
        var collectPromptGate = new FishingPromptStabilityGate(FishingPromptKind.Collect);
        var unavailableTargetSamples = 0;
        var failedCaptureSamples = 0;
        var sampleCount = 0;
        var clock = Stopwatch.StartNew();
        using var diagnostics = new FishingDiagnosticLog();
        while (!token.IsCancellationRequested && clock.Elapsed < TimeSpan.FromSeconds(120))
        {
            using var sample = CaptureMeter(out var captureStatus);
            var observation = sample?.Observation ?? FishingMeterObservation.Missing;
            debugSession.RecordCapture("meter", captureStatus, sample?.Frame.Bitmap.Size);
            if (captureStatus.State is FrameSourceState.TargetUnavailable or FrameSourceState.TargetMinimized)
            {
                unavailableTargetSamples++;
                if (unavailableTargetSamples >= 20)
                {
                    throw new InvalidOperationException(captureStatus.Detail);
                }

                await Task.Delay(250, token);
                continue;
            }

            if (captureStatus.State == FrameSourceState.CaptureFailed)
            {
                failedCaptureSamples++;
                if (failedCaptureSamples >= 3)
                {
                    throw new InvalidOperationException($"Meter capture failed: {captureStatus.Detail}");
                }

                SetState(RoutineState.Armed, $"Meter capture retry {failedCaptureSamples}/3 · {captureStatus.Detail}");
                await Task.Delay(250, token);
                continue;
            }

            unavailableTargetSamples = 0;
            failedCaptureSamples = 0;
            sampleCount++;
            if (sample is not null)
            {
                debugSession.RecordMeter(sample.Analysis, sample.Frame, sampleCount);

                FishingPromptObservation? accelerationPrompt = null;
                if (clock.ElapsedMilliseconds >= settings.FishingCastAccelerationDelayMilliseconds)
                {
                    accelerationPrompt = FishingPromptDetector.Analyze(sample.Frame.Bitmap);
                }

                var accelerationAction = castAccelerationGate.Observe(
                    clock.Elapsed,
                    observation.IsVisible,
                    accelerationPrompt is { Kind: not FishingPromptKind.None });
                if (accelerationAction == FishingCastAccelerationAction.Skip)
                {
                    var reason = observation.IsVisible
                        ? "circular_meter_visible"
                        : $"actionable_{accelerationPrompt?.Kind.ToString().ToLowerInvariant()}_prompt_visible";
                    FishingLoopDiagnosticLog.Write("cast_acceleration_skipped", reason);
                    debugSession.Record("casting_bar", "acceleration_skipped", new
                    {
                        reason,
                        elapsedMilliseconds = clock.ElapsedMilliseconds,
                        meterConfidence = observation.Confidence,
                        prompt = accelerationPrompt?.Kind,
                        promptConfidence = accelerationPrompt?.Confidence,
                    });
                }
                else if (accelerationAction == FishingCastAccelerationAction.Click)
                {
                    await EnsureInputReadyAsync(token);
                    SetState(RoutineState.Armed, "Cast bar ready. Sending one bounded acceleration click.");
                    FishingLoopDiagnosticLog.Write(
                        "cast_acceleration_click_start",
                        $"delay_ms={clock.ElapsedMilliseconds};pulse_ms={CastAccelerationPulseMilliseconds}");
                    await ClickCastAccelerationAsync(token, debugSession);
                    FishingLoopDiagnosticLog.Write(
                        "cast_acceleration_click_end",
                        $"pulse_ms={CastAccelerationPulseMilliseconds}");
                    debugSession.SetStage("Meter", "Cast accelerated; watching for a stable circular tension meter.");
                    SetState(RoutineState.Armed, "Cast accelerated. Watching for the circular fishing meter.");
                }
            }
            if (meterGate.Observe(observation))
            {
                if (sample is not null)
                {
                    var prompt = FishingPromptDetector.Analyze(sample.Frame.Bitmap);
                    if (prompt.Kind == FishingPromptKind.Cast &&
                        prompt.State == FishingHudState.Ready &&
                        prompt.StateConfidence >= 0.90)
                    {
                        debugSession.Record("meter", "ready_cast_overrode_meter_lock", new
                        {
                            sampleCount,
                            prompt.Confidence,
                            prompt.StateConfidence,
                            meterConfidence = observation.Confidence,
                        });
                        meterTracker.Reset();
                        return MeterWaitOutcome.CastPromptReady;
                    }

                    if (prompt.Kind == FishingPromptKind.Collect)
                    {
                        debugSession.Record("meter", "collect_overrode_meter_lock", new
                        {
                            sampleCount,
                            prompt.Confidence,
                            meterConfidence = observation.Confidence,
                        });
                        meterTracker.Reset();
                        return MeterWaitOutcome.CollectPromptReady;
                    }

                    diagnostics.CaptureEvidence("meter-lock", sample.Frame.Bitmap, sample.Analysis);
                    debugSession.Record("meter", "stability_gate_passed", new { sampleCount, observation.Confidence });
                }
                return MeterWaitOutcome.MeterLocked;
            }

            // If the cast press was ignored (or prompt clearing was a visual
            // false-negative), do not wait forever for a meter that cannot start.
            // Re-check the verified cast prompt at a low frequency and return to
            // the bounded prompt/input retry path when it persists.
            if (sampleCount % 10 == 0)
            {
                using var promptSample = CapturePrompt(out var promptStatus);
                var prompt = promptSample?.Observation ?? new FishingPromptObservation(FishingPromptKind.None, 0);
                debugSession.RecordCapture("prompt", promptStatus, promptSample?.Frame.Bitmap.Size);
                if (promptSample is not null)
                {
                    debugSession.RecordPrompt(FishingPromptKind.Cast, prompt, promptSample.Evidence, promptSample.Frame, sampleCount);
                }
                if (promptStatus.State == FrameSourceState.Ready)
                {
                    var castReady = castPromptGate.Observe(prompt);
                    var collectReady = collectPromptGate.Observe(prompt);
                    if (collectReady)
                    {
                        meterTracker.Reset();
                        debugSession.Record("meter", "collect_prompt_resync", new { sampleCount, prompt.Confidence });
                        return MeterWaitOutcome.CollectPromptReady;
                    }

                    if (castReady)
                    {
                        var sameFrameMeter = FishingMeterService.AnalyzeFrameDetailed(
                            promptSample!.Frame.Bitmap,
                            meterTracker);
                        if (FishingPromptArbitration.ShouldSuppress(prompt, sameFrameMeter.Observation))
                        {
                            castPromptGate.Reset();
                            FishingLoopDiagnosticLog.Write(
                                "prompt_suppressed_meter_visible",
                                $"prompt={prompt.Confidence:F3};meter={sameFrameMeter.Observation.Confidence:F3}");
                            debugSession.RecordPromptSuppression(
                                FishingPromptKind.Cast,
                                prompt,
                                promptSample.Evidence,
                                sameFrameMeter,
                                promptSample.Frame,
                                sampleCount);
                            continue;
                        }

                        meterTracker.Reset();
                        return MeterWaitOutcome.CastPromptReady;
                    }
                }
            }

            if (sampleCount % 5 == 0)
            {
                SetState(RoutineState.Armed,
                    $"{captureStatus.Backend} live · meter match {observation.Confidence:P0} · samples {sampleCount}.");
            }

            await Task.Delay(settings.FishingSampleMilliseconds, token);
        }

        token.ThrowIfCancellationRequested();
        throw new TimeoutException(
            "No stable fishing meter or verified fishing action prompt appeared for 120 seconds. Automation stopped instead of leaving the loop stalled.");
    }

    private FishingMeterFrameSample? CaptureMeter(out FrameSourceStatus status)
    {
        var source = frameSource ?? throw new InvalidOperationException("No frame source is configured.");
        return FishingMeterService.CaptureAndAnalyze(source, settings.TargetWindow, meterTracker, out status);
    }

    private async Task WaitForPromptAndPressAsync(FishingPromptKind expected, CancellationToken token, FishingDebugSession debugSession)
    {
        var label = expected == FishingPromptKind.Cast ? "Cast Fishing Line" : "Keep Fish";
        var promptState = expected == FishingPromptKind.Cast ? RoutineState.Casting : RoutineState.Collecting;
        var stabilityGate = new FishingPromptStabilityGate(expected);
        var alternate = expected == FishingPromptKind.Cast ? FishingPromptKind.Collect : FishingPromptKind.Cast;
        var alternateGate = new FishingPromptStabilityGate(alternate);
        var unavailableTargetSamples = 0;
        var failedCaptureSamples = 0;
        var sampleCount = 0;
        var clock = Stopwatch.StartNew();
        debugSession.SetStage(expected == FishingPromptKind.Cast ? "Cast" : "Collect", $"Waiting for the verified {label} prompt.");
        SetState(promptState, $"Waiting for the verified “{label}” prompt.");

        while (!token.IsCancellationRequested && clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            using var promptSample = CapturePrompt(out var captureStatus);
            var observation = promptSample?.Observation ?? new FishingPromptObservation(FishingPromptKind.None, 0);
            debugSession.RecordCapture("prompt", captureStatus, promptSample?.Frame.Bitmap.Size);
            if (captureStatus.State is FrameSourceState.TargetUnavailable or FrameSourceState.TargetMinimized)
            {
                unavailableTargetSamples++;
                if (unavailableTargetSamples >= 20) throw new InvalidOperationException(captureStatus.Detail);
                await Task.Delay(250, token);
                continue;
            }

            if (captureStatus.State == FrameSourceState.CaptureFailed)
            {
                failedCaptureSamples++;
                if (failedCaptureSamples >= 3) throw new InvalidOperationException($"Prompt capture failed: {captureStatus.Detail}");
                await Task.Delay(250, token);
                continue;
            }

            unavailableTargetSamples = 0;
            failedCaptureSamples = 0;
            sampleCount++;
            if (promptSample is not null)
            {
                debugSession.RecordPrompt(expected, observation, promptSample.Evidence, promptSample.Frame, sampleCount);
            }
            var expectedStable = stabilityGate.Observe(observation);
            var alternateStable = alternateGate.Observe(observation);
            if (expectedStable)
            {
                debugSession.Record("prompt", "stability_gate_passed", new { expected, sampleCount, observation.Confidence });
                var sameFrameMeter = FishingMeterService.AnalyzeFrameDetailed(
                    promptSample!.Frame.Bitmap,
                    meterTracker);
                if (FishingPromptArbitration.ShouldSuppress(observation, sameFrameMeter.Observation))
                {
                    stabilityGate.Reset();
                    FishingLoopDiagnosticLog.Write(
                        "prompt_suppressed_meter_visible",
                        $"kind={expected};prompt={observation.Confidence:F3};meter={sameFrameMeter.Observation.Confidence:F3}");
                    debugSession.RecordPromptSuppression(
                        expected,
                        observation,
                        promptSample.Evidence,
                        sameFrameMeter,
                        promptSample.Frame,
                        sampleCount);
                    SetState(promptState, "Meter remains visible; holding input while the detector reacquires its state.");
                    await Task.Delay(Math.Max(120, settings.FishingSampleMilliseconds), token);
                    continue;
                }

                var maximumAttempts = MaximumPromptPressAttempts;
                for (var attempt = 1; attempt <= maximumAttempts; attempt++)
                {
                    await EnsureInputReadyAsync(token);
                    SetState(promptState, $"Verified “{label}” ({observation.Confidence:P0}). Pressing E.");
                    var eventPrefix = expected.ToString().ToLowerInvariant();
                    FishingLoopDiagnosticLog.Write($"{eventPrefix}_e_start", $"confidence={observation.Confidence:F3};attempt={attempt}");
                    debugSession.Record("input", "key_press_start", new { key = InputKey.E, expected, attempt, observation.Confidence });
                    await PressKeyAsync(InputKey.E, token, debugSession);
                    FishingLoopDiagnosticLog.Write($"{eventPrefix}_e_end", $"attempt={attempt}");
                    if (await WaitForPromptToClearAsync(expected, token, debugSession))
                    {
                        meterTracker.Reset();
                        debugSession.Record("meter", "tracker_reset", new { reason = $"{expected}_prompt_cleared" });
                        return;
                    }
                }

                throw new InvalidOperationException(
                    $"The verified “{label}” prompt remained after {maximumAttempts} E press{(maximumAttempts == 1 ? string.Empty : "es")}; automation stopped to prevent input spam.");
            }

            if (alternateStable)
            {
                debugSession.Record("prompt", "stage_resynchronized", new
                {
                    expected,
                    observed = alternate,
                    sampleCount,
                    observation.Confidence,
                    observation.State,
                    observation.StateConfidence,
                });
                FishingLoopDiagnosticLog.Write(
                    "prompt_stage_resynchronized",
                    $"expected={expected};observed={alternate};confidence={observation.Confidence:F3}");
                meterTracker.Reset();

                if (expected == FishingPromptKind.Collect)
                {
                    // A stable Cast prompt proves that Keep Fish was already handled (for
                    // example by a manual E press). Let the caller continue at Cast instead
                    // of spending the remainder of the timeout waiting for an obsolete state.
                    return;
                }

                // If the routine was waiting for Cast but a result prompt is already on
                // screen, collect it through the same verified bounded input path and then
                // resume this Cast wait with fresh stability gates and timeout budget.
                await WaitForPromptAndPressAsync(FishingPromptKind.Collect, token, debugSession);
                stabilityGate.Reset();
                alternateGate.Reset();
                clock.Restart();
                debugSession.SetStage("Cast", "Fishing stage recovered; waiting for the verified Cast Fishing Line prompt.");
                SetState(RoutineState.Casting, "Fishing stage recovered. Waiting for the verified “Cast Fishing Line” prompt.");
                continue;
            }

            if (sampleCount % 10 == 0)
            {
                var detail = expected == FishingPromptKind.Cast && observation.Confidence < 0.05
                    ? "No fishing action prompt is visible in the captured FiveM HUD. Enter fishing mode, then use the Start / Stop shortcut from FiveM."
                    : $"Scanning for “{label}” · best match {observation.Confidence:P0}.";
                SetState(promptState, detail);
            }
            await Task.Delay(Math.Max(120, settings.FishingSampleMilliseconds), token);
        }

        throw new TimeoutException(expected == FishingPromptKind.Cast
            ? "No verified Cast Fishing Line prompt appeared in the captured FiveM HUD. Enter fishing mode and use the Start / Stop shortcut from inside FiveM. No E input was sent."
            : $"Timed out waiting for the verified “{label}” prompt. No E input was sent.");
    }

    private async Task<bool> WaitForPromptToClearAsync(FishingPromptKind pressed, CancellationToken token, FishingDebugSession debugSession)
    {
        var clearGate = new FishingPromptClearGate(pressed);
        var clock = Stopwatch.StartNew();
        while (!token.IsCancellationRequested && clock.Elapsed < TimeSpan.FromSeconds(3))
        {
            using var promptSample = CapturePrompt(out var status);
            var observation = promptSample?.Observation ?? new FishingPromptObservation(FishingPromptKind.None, 0);
            debugSession.RecordCapture("prompt_clear", status, promptSample?.Frame.Bitmap.Size);
            if (promptSample is not null)
            {
                debugSession.RecordPrompt(pressed, observation, promptSample.Evidence, promptSample.Frame, 1);
            }
            if (status.State is FrameSourceState.TargetUnavailable or FrameSourceState.TargetMinimized or FrameSourceState.CaptureFailed)
                throw new InvalidOperationException(status.Detail);
            if (clearGate.Observe(observation)) return true;
            await Task.Delay(150, token);
        }

        return false;
    }

    private FishingPromptFrameSample? CapturePrompt(out FrameSourceStatus status)
    {
        var source = frameSource ?? throw new InvalidOperationException("No frame source is configured.");
        return FishingPromptDetector.CaptureAndAnalyze(source, settings.TargetWindow, out status);
    }

    private async Task EnsureInputReadyAsync(CancellationToken token)
    {
        var capability = await input.PrepareAsync(settings.TargetWindow, token);
        if (!capability.Ready)
        {
            throw new InvalidOperationException($"Input is not ready: {capability.Detail}");
        }
    }

    private async Task PressKeyAsync(InputKey key, CancellationToken token, FishingDebugSession debugSession)
    {
        var keyIsDown = false;
        try
        {
            input.SendKey(settings.TargetWindow, key, false);
            keyIsDown = true;
            debugSession.Record("input", "key_down", new { key });
            await Task.Delay(120, token);
        }
        finally
        {
            if (keyIsDown)
            {
                input.SendKey(settings.TargetWindow, key, true);
                debugSession.Record("input", "key_up", new { key });
            }
        }
    }

    private async Task ClickCastAccelerationAsync(CancellationToken token, FishingDebugSession debugSession)
    {
        var leftIsDown = false;
        try
        {
            input.SendLeftButton(settings.TargetWindow, false);
            leftIsDown = true;
            debugSession.Record("input", "cast_acceleration_left_down", new
            {
                pulseMilliseconds = CastAccelerationPulseMilliseconds,
            });
            await Task.Delay(CastAccelerationPulseMilliseconds, token);
        }
        finally
        {
            if (leftIsDown)
            {
                input.SendLeftButton(settings.TargetWindow, true);
                debugSession.Record("input", "cast_acceleration_left_up", new
                {
                    pulseMilliseconds = CastAccelerationPulseMilliseconds,
                });
            }
        }
    }

    private CycleResult RegulateFishingMeter(CancellationToken token, FishingDebugSession debugSession)
    {
        var leftIsDown = false;
        var missingSamples = 0;
        TimeSpan? missingSince = null;
        var caughtSamples = 0;
        var failureGate = new FishingFailureGate();
        var sampleCount = 0;
        var highestProgress = 0d;
        var capturedLossEvidence = false;
        var controller = new FishingTensionController(
            settings.FishingLowerTensionPercent,
            settings.FishingUpperTensionPercent,
            settings.FishingMinimumPulseMilliseconds,
            settings.FishingMaximumPulseMilliseconds,
            settings.FishingMinimumRestMilliseconds);
        using var diagnostics = new FishingDiagnosticLog();
        var clock = Stopwatch.StartNew();

        try
        {
            while (!token.IsCancellationRequested && clock.Elapsed < TimeSpan.FromSeconds(settings.MaximumDurationSeconds))
            {
                var sampleStartedAt = clock.Elapsed;
                using var sample = CaptureMeter(out var captureStatus);
                var observation = sample?.Observation ?? FishingMeterObservation.Missing;
                debugSession.RecordCapture("regulation", captureStatus, sample?.Frame.Bitmap.Size);
                token.ThrowIfCancellationRequested();
                if (captureStatus.State is FrameSourceState.TargetUnavailable or FrameSourceState.TargetMinimized or FrameSourceState.CaptureFailed)
                {
                    throw new InvalidOperationException($"Meter capture failed: {captureStatus.Detail}");
                }

                diagnostics.Write(observation, leftIsDown);
                sampleCount++;
                if (sample is not null)
                {
                    debugSession.RecordMeter(sample.Analysis, sample.Frame, sampleCount);
                }
                highestProgress = Math.Max(highestProgress, observation.ProgressRatio);
                missingSamples = observation.IsVisible ? 0 : missingSamples + 1;
                if (observation.IsVisible)
                {
                    missingSince = null;
                }
                else
                {
                    missingSince ??= sampleStartedAt;
                }

                var missingDuration = missingSince is null
                    ? TimeSpan.Zero
                    : clock.Elapsed - missingSince.Value;
                caughtSamples = observation.IsCaught ? caughtSamples + 1 : 0;

                if (failureGate.Observe(observation))
                {
                    debugSession.Record("meter", "failure_confirmed", new { sampleCount, observation.Confidence });
                    return new CycleResult(false, "Fishing got away confirmed. Waiting for FiveM to offer the next cast.");
                }

                if (!observation.IsVisible)
                {
                    if (!capturedLossEvidence)
                    {
                        capturedLossEvidence = true;
                        if (sample is not null)
                        {
                            diagnostics.CaptureEvidence("meter-loss", sample.Frame.Bitmap, sample.Analysis);
                        }
                    }

                    // Clear the controller's previous-tension velocity before a
                    // recovered frame arrives, so a visual transition can never
                    // turn into a stale pulse decision.
                    _ = controller.Observe(observation, Stopwatch.GetTimestamp());
                    diagnostics.Write(observation, leftIsDown, "reacquire");
                    if (FishingMeterReacquisition.HasEnded(missingDuration, highestProgress))
                    {
                        return new CycleResult(false,
                            $"Fishing meter stayed unavailable for {missingDuration.TotalSeconds:F1}s at {highestProgress:P0} progress; collection skipped.");
                    }

                    if (FishingMeterReacquisition.ShouldCollectAfterMeterLoss(missingDuration, highestProgress))
                    {
                        return new CycleResult(true,
                            $"Fishing meter completed after a {missingDuration.TotalSeconds:F1}s transition at {highestProgress:P0} progress.");
                    }

                    if (missingSamples % 8 == 0)
                    {
                        Raise(new RoutineStatus(RoutineState.Regulating,
                            $"Meter transition detected · reacquiring {missingDuration.TotalSeconds:F1}/{FishingMeterReacquisition.MissingDurationBeforeComplete.TotalSeconds:F1}s.",
                            sampleCount, observation.Confidence));
                    }

                    if (token.WaitHandle.WaitOne(settings.FishingSampleMilliseconds))
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    continue;
                }

                var decision = controller.Observe(observation, Stopwatch.GetTimestamp());
                if (decision.Action == FishingControlAction.Pulse)
                {
                    input.SendLeftButton(settings.TargetWindow, false);
                    leftIsDown = true;
                    debugSession.Record("input", "left_down", new { decision.PulseMilliseconds, observation.TensionRatio, observation.ProgressRatio });
                    diagnostics.Write(observation, true, "pulse_start", decision.PulseMilliseconds);
                    if (token.WaitHandle.WaitOne(decision.PulseMilliseconds))
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    input.SendLeftButton(settings.TargetWindow, true);
                    leftIsDown = false;
                    debugSession.Record("input", "left_up", new { decision.PulseMilliseconds });
                    diagnostics.Write(observation, false, "pulse_end", decision.PulseMilliseconds);
                }

                if (caughtSamples >= 2 || decision.Action == FishingControlAction.Complete)
                {
                    debugSession.Record("meter", "catch_confirmed", new { sampleCount, observation.ProgressRatio, observation.Confidence });
                    return new CycleResult(true, $"Catch confirmed at {observation.ProgressRatio:P0} progress.");
                }

                if (sampleCount % 10 == 0)
                {
                    Raise(new RoutineStatus(RoutineState.Regulating,
                        $"{captureStatus.Backend} · tension {observation.TensionRatio:P0} · progress {observation.ProgressRatio:P0}",
                        sampleCount, observation.Confidence));
                }

                if (token.WaitHandle.WaitOne(settings.FishingSampleMilliseconds))
                {
                    token.ThrowIfCancellationRequested();
                }
            }

            return new CycleResult(settings.CollectOnTimeout, settings.CollectOnTimeout
                ? "Fishing safety duration reached; collecting."
                : "Fishing safety duration reached; collection skipped.");
        }
        finally
        {
            if (leftIsDown)
            {
                ReleaseLeftButton();
            }
        }
    }

    private void ReleaseLeftButton()
    {
        try
        {
            input.SendLeftButton(settings.TargetWindow, true);
        }
        catch
        {
            try
            {
                InputSender.SendLeftButton(true);
            }
            catch
            {
                // Best-effort release on cancellation/fault cleanup.
            }
        }
    }

    private void SetState(RoutineState value, string detail)
    {
        state = value;
        latestDebugSession?.Record("state", "status", new { state = value, detail });
        Raise(new RoutineStatus(value, detail));
    }

    private void Raise(RoutineStatus status) => StatusChanged?.Invoke(this, status);

    public void Dispose()
    {
        Stop();
        cancellation?.Dispose();
        frameSource?.Dispose();
    }

    private enum MeterWaitOutcome
    {
        MeterLocked,
        CastPromptReady,
        CollectPromptReady,
    }

    private sealed record CycleResult(bool ShouldCollect, string Detail);
}
