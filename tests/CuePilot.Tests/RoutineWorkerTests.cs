namespace CuePilot.Tests;

public sealed class RoutineWorkerTests
{
    [Fact]
    public void InputReleaseContinuesAfterOneWindowsInputFailure()
    {
        var releases = new List<string>();
        var safety = new InputReleaseSafety(
            (key, up) => releases.Add($"{key}:{up}"),
            up =>
            {
                releases.Add($"Left:{up}");
                throw new InvalidOperationException("simulated mouse release failure");
            });

        var released = safety.ReleaseAll();

        Assert.False(released);
        Assert.Equal(["Left:True", "E:True"], releases);
    }

    [Fact]
    public void FishingStopWhileIdleDoesNotInjectInput()
    {
        var releases = new List<string>();
        var safety = new InputReleaseSafety(
            (key, up) => releases.Add($"{key}:{up}"),
            up => releases.Add($"Left:{up}"));
        using var engine = new AdaptiveRoutineEngine(safety);

        engine.Stop("test stop");

        Assert.Empty(releases);
        Assert.Equal(RoutineState.Stopped, engine.State);
    }

    [Fact]
    public void InputGateReleasesOnlyOwnedInputAndRejectsLatePresses()
    {
        var events = new List<string>();
        var safety = new InputReleaseSafety(
            (key, up) => events.Add($"{key}:{up}"),
            up => events.Add($"Left:{up}"));
        var gate = new AutomationInputGate(safety);
        gate.BeginRun();
        gate.SendKeyDown(InputKey.E, CancellationToken.None, () => events.Add("E:False"));

        var released = gate.StopAndReleaseOwnedInput();

        Assert.True(released);
        Assert.Equal(["E:False", "E:True"], events);
        Assert.ThrowsAny<OperationCanceledException>(() =>
            gate.SendLeftButtonDown(CancellationToken.None, () => events.Add("Left:False")));
        Assert.Equal(["E:False", "E:True"], events);
        Assert.True(gate.StopAndReleaseOwnedInput());
        Assert.Equal(["E:False", "E:True"], events);
    }

    [Fact]
    public async Task AutomaticInputWaitsForUserForegroundWithoutActivatingAnything()
    {
        var probes = 0;
        var delays = 0;

        var ready = await ForegroundInputReadiness.WaitAsync(
            InputDeliveryMode.Automatic,
            () => ++probes >= 3,
            CancellationToken.None,
            maximumChecks: 5,
            delay: (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        Assert.True(ready);
        Assert.Equal(3, probes);
        Assert.Equal(2, delays);
    }

    [Fact]
    public async Task ForegroundOnlyInputFailsImmediatelyWithoutWaiting()
    {
        var delays = 0;

        var ready = await ForegroundInputReadiness.WaitAsync(
            InputDeliveryMode.Foreground,
            () => false,
            CancellationToken.None,
            delay: (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        Assert.False(ready);
        Assert.Equal(0, delays);
    }

    [Fact]
    public void OwnedWorkerCancelsAndWaitsForCleanupBeforeReturning()
    {
        using var worker = new OwnedRoutineWorker();
        using var started = new ManualResetEventSlim();
        using var cleanedUp = new ManualResetEventSlim();
        worker.Start(async (_, token) =>
        {
            started.Set();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            finally
            {
                cleanedUp.Set();
            }
        });
        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));

        var stopped = worker.Stop(TimeSpan.FromSeconds(1));

        Assert.True(stopped);
        Assert.True(cleanedUp.IsSet);
        Assert.False(worker.IsRunning);
    }

    [Fact]
    public void OwnedWorkerRejectsRestartUntilAnUncooperativeRunActuallyReturns()
    {
        using var worker = new OwnedRoutineWorker();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        worker.Start((_, _) => Task.Run(() =>
        {
            started.Set();
            release.Wait();
        }));
        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));

        try
        {
            worker.Cancel();
            Assert.False(worker.WaitForCompletion(TimeSpan.FromMilliseconds(25)));
            Assert.Throws<InvalidOperationException>(() => worker.Start((_, _) => Task.CompletedTask));
        }
        finally
        {
            release.Set();
        }

        Assert.True(worker.WaitForCompletion(TimeSpan.FromSeconds(1)));
        worker.Start((_, _) => Task.CompletedTask);
        Assert.True(worker.WaitForCompletion(TimeSpan.FromSeconds(1)));
    }
}
