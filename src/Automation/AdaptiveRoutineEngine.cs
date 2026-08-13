using System.Diagnostics;

namespace WorkflowLooper;

internal static class RoutineWorker
{
    internal static Task Start(Func<Task> work) => Task.Run(work);
}

internal sealed class AdaptiveRoutineEngine : IDisposable
{
    private const int MaximumPromptPressAttempts = 5;
    private readonly object sync = new();
    private FishingRoutineSettings settings = new();
    private CancellationTokenSource? cancellation;
    private IFrameSource? frameSource;
    private GdiFrameSource? promptFrameSource;
    private TargetInputRouter input = new(InputDeliveryMode.Automatic);
    private volatile RoutineState state = RoutineState.Stopped;

    internal event EventHandler<RoutineStatus>? StatusChanged;
    internal RoutineState State => state;

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
            promptFrameSource?.Dispose();
            promptFrameSource = new GdiFrameSource();
            input = new TargetInputRouter(settings.InputMode);
            SetState(RoutineState.Casting, "Preflight: resolving the target application.");
            _ = RoutineWorker.Start(() => RunRoutineSafeAsync(cancellation.Token));
        }
    }

    internal void Stop(string reason = "Fishing routine stopped.")
    {
        cancellation?.Cancel();
        ReleaseLeftButton();
        SetState(RoutineState.Stopped, reason);
    }

    private async Task RunRoutineSafeAsync(CancellationToken token)
    {
        try
        {
            await PreflightAsync(token);
            await WaitForPromptAndPressAsync(FishingPromptKind.Cast, token);
            FishingLoopDiagnosticLog.Write("cast_prompt_cleared");

            while (!token.IsCancellationRequested)
            {
                SetState(RoutineState.Armed, "Line cast. Watching the target application for the fishing meter.");
                var detected = await WaitForMeterAsync(token);
                if (!detected)
                {
                    FishingLoopDiagnosticLog.Write("cast_retry_requested", "Cast prompt remained visible while waiting for the meter.");
                    await WaitForPromptAndPressAsync(FishingPromptKind.Cast, token);
                    continue;
                }

                FishingLoopDiagnosticLog.Write("meter_lock");
                await EnsureInputReadyAsync(token);
                SetState(RoutineState.Regulating, "Fishing meter detected. Running bounded feedback.");
                var result = await Task.Run(() => RegulateFishingMeter(token), CancellationToken.None);
                token.ThrowIfCancellationRequested();
                FishingLoopDiagnosticLog.Write(result.ShouldCollect ? "meter_complete" : "meter_reacquire_failed", result.Detail);

                if (!result.ShouldCollect)
                {
                    SetState(RoutineState.Stowing, $"{result.Detail} Waiting for FiveM to offer the next cast.");
                    await WaitForPromptAndPressAsync(FishingPromptKind.Cast, token);
                    continue;
                }

                SetState(RoutineState.Collecting, result.Detail);
                await Task.Delay(settings.CollectDelayMilliseconds, token);
                await WaitForPromptAndPressAsync(FishingPromptKind.Collect, token);
                FishingLoopDiagnosticLog.Write("collect_prompt_cleared");
                await WaitForPromptAndPressAsync(FishingPromptKind.Cast, token);
                FishingLoopDiagnosticLog.Write("cast_prompt_cleared");
            }
        }
        catch (OperationCanceledException)
        {
            ReleaseLeftButton();
            if (State != RoutineState.Stopped)
            {
                SetState(RoutineState.Stopped, "Fishing routine cancelled safely.");
            }
        }
        catch (Exception exception)
        {
            ReleaseLeftButton();
            SetState(RoutineState.Faulted, exception.Message);
        }
    }

    private async Task PreflightAsync(CancellationToken token)
    {
        if (!WindowTargetService.TryResolve(settings.TargetWindow, out var target, out var targetDetail))
        {
            throw new InvalidOperationException($"Target preflight failed: {targetDetail}");
        }

        if (target.IsMinimized)
        {
            throw new InvalidOperationException("Target preflight failed: restore FiveM before arming. It may be covered, but not minimized.");
        }

        SetState(RoutineState.Casting, $"Preflight: {targetDetail} Verifying input delivery.");
        var capability = await input.PrepareAsync(settings.TargetWindow, token);
        if (!capability.Ready)
        {
            throw new InvalidOperationException($"Input preflight failed: {capability.Detail}");
        }

        SetState(RoutineState.Casting, "Preflight: input verified. Testing live desktop capture.");
        var source = frameSource ?? throw new InvalidOperationException("No frame source is configured.");
        _ = FishingMeterService.Observe(source, settings.TargetWindow, out var captureStatus);
        if (captureStatus.State != FrameSourceState.Ready)
        {
            throw new InvalidOperationException($"Capture preflight failed: {captureStatus.Detail}");
        }

        SetState(RoutineState.Casting,
            $"Preflight ready · {captureStatus.Backend} {captureStatus.CaptureMilliseconds:0.0} ms · {capability.Backend}.");
        await Task.Delay(150, token);
    }

    private async Task<bool> WaitForMeterAsync(CancellationToken token)
    {
        var meterGate = new FishingMeterStabilityGate();
        var castPromptGate = new FishingPromptStabilityGate(FishingPromptKind.Cast);
        var unavailableTargetSamples = 0;
        var failedCaptureSamples = 0;
        var sampleCount = 0;
        while (!token.IsCancellationRequested)
        {
            var observation = Observe(out var captureStatus);
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
            if (meterGate.Observe(observation))
            {
                return true;
            }

            // If the cast press was ignored (or prompt clearing was a visual
            // false-negative), do not wait forever for a meter that cannot start.
            // Re-check the verified cast prompt at a low frequency and return to
            // the bounded prompt/input retry path when it persists.
            if (sampleCount % 20 == 0)
            {
                var prompt = ObservePrompt(out var promptStatus);
                if (promptStatus.State == FrameSourceState.Ready && castPromptGate.Observe(prompt))
                {
                    return false;
                }
            }

            if (sampleCount % 5 == 0)
            {
                SetState(RoutineState.Armed,
                    $"{captureStatus.Backend} live · meter match {observation.Confidence:P0} · samples {sampleCount}.");
            }

            await Task.Delay(settings.FishingSampleMilliseconds, token);
        }

        return false;
    }

    private FishingMeterObservation Observe(out FrameSourceStatus status)
    {
        var source = frameSource ?? throw new InvalidOperationException("No frame source is configured.");
        return FishingMeterService.Observe(source, settings.TargetWindow, out status);
    }

    private async Task WaitForPromptAndPressAsync(FishingPromptKind expected, CancellationToken token)
    {
        var label = expected == FishingPromptKind.Cast ? "Cast Fishing Line" : "Keep Fish";
        var promptState = expected == FishingPromptKind.Cast ? RoutineState.Casting : RoutineState.Collecting;
        var stabilityGate = new FishingPromptStabilityGate(expected);
        var unavailableTargetSamples = 0;
        var failedCaptureSamples = 0;
        var sampleCount = 0;
        var clock = Stopwatch.StartNew();
        SetState(promptState, $"Waiting for the verified “{label}” prompt.");

        while (!token.IsCancellationRequested && clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            var observation = ObservePrompt(out var captureStatus);
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
            if (stabilityGate.Observe(observation))
            {
                for (var attempt = 1; attempt <= MaximumPromptPressAttempts; attempt++)
                {
                    await EnsureInputReadyAsync(token);
                    SetState(promptState, $"Verified “{label}” ({observation.Confidence:P0}). Pressing E.");
                    var eventPrefix = expected.ToString().ToLowerInvariant();
                    FishingLoopDiagnosticLog.Write($"{eventPrefix}_e_start", $"confidence={observation.Confidence:F3};attempt={attempt}");
                    await PressKeyAsync(Keys.E, token);
                    FishingLoopDiagnosticLog.Write($"{eventPrefix}_e_end", $"attempt={attempt}");
                    if (await WaitForPromptToClearAsync(expected, token)) return;
                }

                throw new InvalidOperationException(
                    $"The verified “{label}” prompt remained after {MaximumPromptPressAttempts} E presses; automation stopped to prevent input spam.");
            }

            if (sampleCount % 10 == 0)
                SetState(promptState, $"Scanning for “{label}” · best match {observation.Confidence:P0}.");
            await Task.Delay(Math.Max(120, settings.FishingSampleMilliseconds), token);
        }

        throw new TimeoutException($"Timed out waiting for the verified “{label}” prompt. No E input was sent.");
    }

    private async Task<bool> WaitForPromptToClearAsync(FishingPromptKind pressed, CancellationToken token)
    {
        var clearGate = new FishingPromptClearGate(pressed);
        var clock = Stopwatch.StartNew();
        while (!token.IsCancellationRequested && clock.Elapsed < TimeSpan.FromSeconds(3))
        {
            var observation = ObservePrompt(out var status);
            if (status.State is FrameSourceState.TargetUnavailable or FrameSourceState.TargetMinimized or FrameSourceState.CaptureFailed)
                throw new InvalidOperationException(status.Detail);
            if (clearGate.Observe(observation)) return true;
            await Task.Delay(150, token);
        }

        return false;
    }

    private FishingPromptObservation ObservePrompt(out FrameSourceStatus status)
    {
        var source = promptFrameSource ?? throw new InvalidOperationException("No prompt frame source is configured.");
        return FishingPromptDetector.Observe(source, settings.TargetWindow, out status);
    }

    private async Task EnsureInputReadyAsync(CancellationToken token)
    {
        var capability = await input.PrepareAsync(settings.TargetWindow, token);
        if (!capability.Ready)
        {
            throw new InvalidOperationException($"Input is not ready: {capability.Detail}");
        }
    }

    private async Task PressKeyAsync(Keys key, CancellationToken token)
    {
        var keyIsDown = false;
        try
        {
            input.SendKey(settings.TargetWindow, key, false);
            keyIsDown = true;
            await Task.Delay(120, token);
        }
        finally
        {
            if (keyIsDown)
            {
                input.SendKey(settings.TargetWindow, key, true);
            }
        }
    }

    private CycleResult RegulateFishingMeter(CancellationToken token)
    {
        var leftIsDown = false;
        var missingSamples = 0;
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
                var observation = Observe(out var captureStatus);
                token.ThrowIfCancellationRequested();
                if (captureStatus.State is FrameSourceState.TargetUnavailable or FrameSourceState.TargetMinimized or FrameSourceState.CaptureFailed)
                {
                    throw new InvalidOperationException($"Meter capture failed: {captureStatus.Detail}");
                }

                diagnostics.Write(observation, leftIsDown);
                highestProgress = Math.Max(highestProgress, observation.ProgressRatio);
                missingSamples = observation.IsVisible ? 0 : missingSamples + 1;
                caughtSamples = observation.IsCaught ? caughtSamples + 1 : 0;

                if (failureGate.Observe(observation))
                {
                    return new CycleResult(false, "Fishing got away confirmed. Waiting for FiveM to offer the next cast.");
                }

                if (!observation.IsVisible)
                {
                    if (!capturedLossEvidence)
                    {
                        capturedLossEvidence = true;
                        diagnostics.CaptureFirstLoss(
                            frameSource ?? throw new InvalidOperationException("No frame source is configured."),
                            settings.TargetWindow);
                    }

                    // Clear the controller's previous-tension velocity before a
                    // recovered frame arrives, so a visual transition can never
                    // turn into a stale pulse decision.
                    _ = controller.Observe(observation, Stopwatch.GetTimestamp());
                    diagnostics.Write(observation, leftIsDown, "reacquire");
                    if (FishingMeterReacquisition.HasEnded(missingSamples, highestProgress))
                    {
                        return new CycleResult(false,
                            $"Fishing meter stayed unavailable for {missingSamples} samples at {highestProgress:P0} progress; collection skipped.");
                    }

                    if (FishingMeterReacquisition.ShouldCollectAfterMeterLoss(missingSamples, highestProgress))
                    {
                        return new CycleResult(true,
                            $"Fishing meter completed after a {missingSamples}-sample transition at {highestProgress:P0} progress.");
                    }

                    if (missingSamples % 8 == 0)
                    {
                        Raise(new RoutineStatus(RoutineState.Regulating,
                            $"Meter transition detected · reacquiring {missingSamples}/{FishingMeterReacquisition.ConsecutiveMissingSamplesBeforeComplete}.",
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
                    diagnostics.Write(observation, true, "pulse_start", decision.PulseMilliseconds);
                    if (token.WaitHandle.WaitOne(decision.PulseMilliseconds))
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    input.SendLeftButton(settings.TargetWindow, true);
                    leftIsDown = false;
                    diagnostics.Write(observation, false, "pulse_end", decision.PulseMilliseconds);
                }

                if (caughtSamples >= 2 || decision.Action == FishingControlAction.Complete)
                {
                    return new CycleResult(true, $"Catch confirmed at {observation.ProgressRatio:P0} progress.");
                }

                sampleCount++;
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
        Raise(new RoutineStatus(value, detail));
    }

    private void Raise(RoutineStatus status) => StatusChanged?.Invoke(this, status);

    public void Dispose()
    {
        Stop();
        cancellation?.Dispose();
        frameSource?.Dispose();
        promptFrameSource?.Dispose();
    }

    private sealed record CycleResult(bool ShouldCollect, string Detail);
}
