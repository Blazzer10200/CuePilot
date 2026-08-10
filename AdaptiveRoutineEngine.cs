using System.Diagnostics;

namespace WorkflowLooper;

internal sealed class AdaptiveRoutineEngine : IDisposable
{
    private readonly PhysicalMouseMonitor monitor = new();
    private readonly object sync = new();
    private TriggeredRoutineSettings settings = new();
    private CancellationTokenSource? cancellation;
    private long physicalDownTimestamp;
    private volatile bool finishRequested;

    internal event EventHandler<RoutineStatus>? StatusChanged;
    internal RoutineState State { get; private set; } = RoutineState.Stopped;

    internal AdaptiveRoutineEngine() => monitor.LeftButtonChanged += HandlePhysicalMouse;

    internal void Arm(TriggeredRoutineSettings requestedSettings)
    {
        lock (sync)
        {
            if (State is RoutineState.Tapping or RoutineState.Collecting or RoutineState.Cooldown)
            {
                throw new InvalidOperationException("The triggered routine is already running.");
            }

            settings = requestedSettings.Copy();
            settings.Clamp();
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            finishRequested = false;
            monitor.Start();
            SetState(RoutineState.Armed, "Hold and release physical left-click when the minigame appears.");
        }
    }

    internal void Stop(string reason = "Routine stopped.")
    {
        cancellation?.Cancel();
        finishRequested = true;
        monitor.Stop();
        try
        {
            InputSender.SendLeftButton(true);
        }
        catch
        {
            // The normal playback cleanup path will report injection failures.
        }

        SetState(RoutineState.Stopped, reason);
    }

    internal void FinishCurrentCycle()
    {
        if (State == RoutineState.Tapping)
        {
            finishRequested = true;
        }
    }

    private void HandlePhysicalMouse(object? sender, PhysicalMouseEventArgs e)
    {
        if (e.IsDown)
        {
            if (State == RoutineState.Armed)
            {
                physicalDownTimestamp = e.Timestamp;
            }
            else if (State == RoutineState.Tapping && settings.PhysicalClickFinishes)
            {
                finishRequested = true;
            }

            return;
        }

        if (State != RoutineState.Armed || physicalDownTimestamp == 0)
        {
            return;
        }

        var heldMilliseconds = (e.Timestamp - physicalDownTimestamp) * 1_000d / Stopwatch.Frequency;
        physicalDownTimestamp = 0;
        if (heldMilliseconds < settings.TriggerHoldMilliseconds)
        {
            Raise(new RoutineStatus(RoutineState.Armed, $"Hold was {heldMilliseconds:F0} ms; need {settings.TriggerHoldMilliseconds} ms to trigger."));
            return;
        }

        if (!WindowTargetService.IsForegroundMatch(settings.TargetWindow, out var detail))
        {
            Raise(new RoutineStatus(RoutineState.Armed, detail));
            return;
        }

        _ = RunCycleSafeAsync(cancellation?.Token ?? CancellationToken.None);
    }

    private async Task RunCycleSafeAsync(CancellationToken token)
    {
        try
        {
            await RunCycleAsync(token);
        }
        catch (OperationCanceledException)
        {
            if (State != RoutineState.Stopped)
            {
                SetState(RoutineState.Stopped, "Routine cancelled safely.");
            }
        }
        catch (Exception exception)
        {
            monitor.Stop();
            SetState(RoutineState.Faulted, exception.Message);
        }
    }

    private async Task RunCycleAsync(CancellationToken token)
    {
        if (State != RoutineState.Armed)
        {
            return;
        }

        finishRequested = false;
        SetState(RoutineState.Tapping, "Precision tapping active.");
        var result = await Task.Run(() => TapUntilComplete(token), CancellationToken.None);
        if (token.IsCancellationRequested || State == RoutineState.Stopped)
        {
            return;
        }

        if (!result.ShouldCollect)
        {
            SetState(RoutineState.Armed, result.Detail);
            return;
        }

        SetState(RoutineState.Collecting, result.Detail);
        await Task.Delay(settings.CollectDelayMilliseconds, token);
        if (!WindowTargetService.IsForegroundMatch(settings.TargetWindow, out var targetDetail))
        {
            SetState(RoutineState.Armed, $"Collection skipped: {targetDetail}");
            return;
        }

        InputSender.SendVirtualKey(Keys.E, false);
        await Task.Delay(35, token);
        InputSender.SendVirtualKey(Keys.E, true);
        SetState(RoutineState.Cooldown, $"Collected. Re-arming in {settings.CooldownSeconds} seconds.");
        await Task.Delay(TimeSpan.FromSeconds(settings.CooldownSeconds), token);
        if (!token.IsCancellationRequested)
        {
            SetState(RoutineState.Armed, "Ready for the next physical left-click hold.");
        }
    }

    private CycleResult TapUntilComplete(CancellationToken token)
    {
        var originalPriority = Thread.CurrentThread.Priority;
        var leftIsDown = false;
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            using var waiter = new PrecisionWaiter();
            var clock = Stopwatch.StartNew();
            var intervalMicroseconds = settings.TapIntervalMilliseconds * 1_000d;
            var holdMicroseconds = settings.HoldMilliseconds * 1_000d;
            var maximumMicroseconds = settings.MaximumDurationSeconds * 1_000_000d;
            var click = 0;
            var missingCueSamples = 0;
            var lastCueCheck = -100_000d;
            double similarity = 1;

            while (!token.IsCancellationRequested && clock.ElapsedTicks * 1_000_000d / Stopwatch.Frequency < maximumMicroseconds)
            {
                if (finishRequested)
                {
                    return new CycleResult(true, "Physical finish signal received.");
                }

                if (!WindowTargetService.IsForegroundMatch(settings.TargetWindow, out var targetDetail))
                {
                    return new CycleResult(false, $"Stopped safely: {targetDetail}");
                }

                var downTarget = click * intervalMicroseconds;
                PlaybackEngine.WaitUntil(clock, downTarget, waiter, token);
                InputSender.SendLeftButton(false);
                leftIsDown = true;
                PlaybackEngine.WaitUntil(clock, downTarget + holdMicroseconds, waiter, token);
                InputSender.SendLeftButton(true);
                leftIsDown = false;
                click++;

                var elapsed = clock.ElapsedTicks * 1_000_000d / Stopwatch.Frequency;
                if (settings.VisualCue.IsConfigured && elapsed > 1_000_000 && elapsed - lastCueCheck >= 100_000)
                {
                    lastCueCheck = elapsed;
                    similarity = VisualCueService.Similarity(settings.VisualCue, settings.TargetWindow);
                    missingCueSamples = similarity * 100 < settings.VisualCue.SimilarityPercent ? missingCueSamples + 1 : 0;
                    if (missingCueSamples >= 5)
                    {
                        return new CycleResult(true, $"Visual cue ended at {similarity:P0} similarity.");
                    }
                }

                if (click % 8 == 0)
                {
                    Raise(new RoutineStatus(RoutineState.Tapping, $"Tap {click:N0} · {settings.TapIntervalMilliseconds} ms rhythm", click, similarity));
                }
            }

            return new CycleResult(settings.CollectOnTimeout, settings.CollectOnTimeout
                ? "Safety duration reached; collecting."
                : "Safety duration reached; collection skipped.");
        }
        finally
        {
            if (leftIsDown)
            {
                InputSender.SendLeftButton(true);
            }

            Thread.CurrentThread.Priority = originalPriority;
        }
    }

    private void SetState(RoutineState state, string detail)
    {
        State = state;
        Raise(new RoutineStatus(state, detail));
    }

    private void Raise(RoutineStatus status) => StatusChanged?.Invoke(this, status);

    public void Dispose()
    {
        Stop();
        monitor.Dispose();
        cancellation?.Dispose();
    }

    private sealed record CycleResult(bool ShouldCollect, string Detail);
}
