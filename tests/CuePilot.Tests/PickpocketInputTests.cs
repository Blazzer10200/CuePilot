using Xunit;
using System.Drawing;
using System.Text.Json;

namespace CuePilot.Tests;

[Collection("Detector timing")]
public sealed class PickpocketInputTests
{
    [Theory]
    [InlineData("SingleAttempt", true, false, 1)]
    [InlineData("SingleAttempt", false, false, 1)] // Armed late: verified Active frames stand in for the fade-in.
    [InlineData("SingleAttempt", false, true, 0)]  // Header-less Active frames never arm.
    [InlineData("Observe", true, false, 0)]
    [InlineData("PrecisionAttempt", true, false, 1)]
    [InlineData("PrecisionAttempt", false, false, 1)]
    [InlineData("PrecisionAttempt", false, true, 0)]
    public async Task EngineArmsOnPreparationOrVerifiedPanelAndRecordsOneTapThenEnds(string mode, bool preparation, bool headerless, int expectedPresses)
    {
        var sent = new List<bool>();
        var input = new PickpocketInputController(up => sent.Add(up));
        var count = 0;
        var started = 0d;
        using var observer = new PickpocketObserverEngine(() => new EmptyFixtureSource(),
            _ => new(IntPtr.Zero, 3258, "FiveM_b3258_GTAProcess", "Fixture", new Rectangle(0, 0, 1920, 1080), true, false),
            _ => false, (_, target) => new(mode, target, Path.Combine(Path.GetTempPath(), "CuePilotTests", "pickpocket-input")), input,
            (_, _) =>
            {
                count++;
                var time = HighResolutionSampleClock.NowMilliseconds;
                if (started == 0) started = time;
                var elapsed = time - started;
                var state = preparation && count <= 2 ? PickpocketVisualState.Preparing
                    : input.PressCount > 0 || elapsed > 1000 ? PickpocketVisualState.Grabbed : PickpocketVisualState.Active;
                return new(state, new Rectangle(0, 100, 576, 23), 80 + elapsed * .38,
                    [new(PickpocketBandColor.White, 250, 450)], 1, "Synthetic pipeline fixture") { HeaderlessFrames = headerless ? 1 : 0 };
            });
        var completed = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.StatusChanged += (_, status) =>
        {
            if (status.State == "Cooldown") completed.TrySetResult(status);
            if (completed.Task.IsCompleted && !status.Observing && status.State == "Stopped") ended.TrySetResult(status);
        };
        observer.Configure("Widest", mode);
        observer.Start(new() { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 3258 });
        var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (mode != "Observe")
        {
            // A one-tap run ends itself once its attempt is over, so the next key press arms afresh.
            var end = await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("Attempt finished", end.Detail);
            Assert.True(SpinWait.SpinUntil(() => !observer.IsObserving, 2000));
        }
        observer.Stop();
        Assert.Equal(expectedPresses, result.AutomatedPressCount);
        Assert.Equal(expectedPresses * 2, sent.Count);
        Assert.False(observer.Status.InputArmed);
        Assert.Equal(mode, observer.Status.InputMode);
        Assert.True(result.CooldownUntilUnixMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.EvidenceDirectory, "summary.json")));
        Assert.Equal(expectedPresses, summary.RootElement.GetProperty("status").GetProperty("automatedPressCount").GetInt32());
        if (expectedPresses == 1)
        {
            Assert.Equal([false, true], sent);
            // The hold no longer blocks capture: key-up is stamped by the loop, or by
            // Stop when the test stops the run before the hold elapses.
            Assert.NotNull(observer.Status.InputDelivery?.KeyUpMs);
            Assert.Contains("Automated Space taps: 1", observer.Status.Debug!.Report);
        }
    }

    private static WindowTargetService.ResolvedWindowTarget FiveM() =>
        new(IntPtr.Zero, 3258, "FiveM_b3258_GTAProcess", "Fixture", new Rectangle(0, 0, 1920, 1080), true, false);

    [Fact]
    public async Task AutoArmChainsRunsAfterEachAttemptWithoutEverShowingStopped()
    {
        var cycles = 0;
        var started = 0d;
        var stopping = false;
        var stoppedEarly = false;
        var input = new PickpocketInputController(_ => { });
        using var observer = new PickpocketObserverEngine(() => new EmptyFixtureSource(), _ => FiveM(), _ => false,
            (_, target) =>
            {
                Interlocked.Increment(ref cycles);
                started = 0;
                return new("SingleAttempt", target, Path.Combine(Path.GetTempPath(), "CuePilotTests", "pickpocket-input"));
            }, input,
            (_, _) =>
            {
                var time = HighResolutionSampleClock.NowMilliseconds;
                if (started == 0) started = time;
                var elapsed = time - started;
                var state = input.PressCount > 0 || elapsed > 1000 ? PickpocketVisualState.Grabbed : PickpocketVisualState.Active;
                return new(state, new Rectangle(0, 100, 576, 23), 80 + elapsed * .38,
                    [new(PickpocketBandColor.White, 250, 450)], 1, "Synthetic pipeline fixture");
            });
        observer.Configure("Widest", "SingleAttempt", autoArm: true);
        var trail = new System.Collections.Concurrent.ConcurrentQueue<string>();
        observer.StatusChanged += (_, status) =>
        {
            trail.Enqueue($"{(status.Observing ? "obs" : "STOPPED")}/{status.State}/{status.Detail}");
            if (!status.Observing && !Volatile.Read(ref stopping)) stoppedEarly = true;
        };
        observer.Start(new() { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 3258 });
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref cycles) >= 2, 10_000), "auto-arm never started a second run");
        Assert.True(observer.Status.AutoArm);
        Assert.True(observer.IsObserving);
        Volatile.Write(ref stopping, true);
        observer.Stop();
        Assert.False(stoppedEarly, string.Join(" | ", trail.Where(line => line.StartsWith("STOPPED"))));
        Assert.False(observer.IsObserving);
        Assert.False(observer.Status.InputArmed);
    }

    [Fact]
    public void AutoArmWaitsForFiveMInsteadOfFaulting()
    {
        var present = false;
        using var observer = new PickpocketObserverEngine(() => new EmptyFixtureSource(),
            _ => present ? FiveM() : throw new InvalidOperationException("FiveM is not running."), _ => false,
            (_, target) => new("SingleAttempt", target, Path.Combine(Path.GetTempPath(), "CuePilotTests", "pickpocket-input")),
            new PickpocketInputController(_ => { }), (_, _) => PickpocketObservation.Missing);
        observer.Configure("Widest", "SingleAttempt", autoArm: true);
        observer.Start(new() { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 3258 });
        Assert.True(SpinWait.SpinUntil(() => observer.Status.Detail.StartsWith("Auto-arm: waiting for FiveM", StringComparison.Ordinal), 5_000), observer.Status.Detail);
        Assert.True(observer.Status.Observing);
        Assert.Equal("Waiting", observer.Status.State);
        present = true;
        Assert.True(SpinWait.SpinUntil(() => observer.Status.State == "Searching", 10_000), observer.Status.State);
        observer.Stop();
        Assert.False(observer.IsObserving);
    }

    private sealed class EmptyFixtureSource : IFrameSource
    {
        public string Name => "DXGI fixture";
        public bool TryCapture(WindowTargetSettings target, Rectangle region, out FrameLease? frame, out FrameSourceStatus status)
        {
            status = new(FrameSourceState.Ready, Name, "Synthetic pipeline fixture", TimeSpan.Zero, 0);
            frame = new(new Bitmap(2, 2), status);
            return true;
        }
        public void Dispose() { }
    }

    private static PickpocketTimingPrediction Candidate(double planned = 110, double latest = 170) =>
        new(true, planned, latest, 380, 3, "Fixture");

    [Fact]
    public void StopPreservesSelectedModeButLeavesInputDisarmed()
    {
        using var observer = new PickpocketObserverEngine(input: new(_ => Assert.Fail("Unexpected input")));
        observer.Configure("Widest", "SingleAttempt");
        observer.Stop();
        Assert.Equal("SingleAttempt", observer.Status.InputMode);
        Assert.False(observer.Status.InputArmed);
    }

    [Fact]
    public void SpaceUsesPhysicalScanCodeWithMatchingRelease()
    {
        var down = InputSender.CreateScanCodeInput(InputKey.Space, false);
        var up = InputSender.CreateScanCodeInput(InputKey.Space, true);
        Assert.NotEqual(0, down.Data.Keyboard.ScanCode);
        Assert.Equal(down.Data.Keyboard.ScanCode, up.Data.Keyboard.ScanCode);
        Assert.Equal(NativeMethods.KeyeventfScancode, down.Data.Keyboard.Flags);
        Assert.Equal(NativeMethods.KeyeventfScancode | NativeMethods.KeyeventfKeyup, up.Data.Keyboard.Flags);
    }

    [Fact]
    public void OneTapWaitsForDeadlineAndReleasesExactlyOnceWhenTheHoldElapses()
    {
        var time = 100d;
        var events = new List<(bool Up, double Time)>();
        var input = new PickpocketInputController(up => events.Add((up, time)), () => time);
        input.BeginRun();
        input.TryTap(Candidate(), 95, () => true, (deadline, _) => time = deadline, CancellationToken.None);
        // TryTap returns with Space held so the caller keeps capturing during the hold.
        Assert.Equal([(false, 110d)], events);
        Assert.Equal(145, input.ReleaseDueMs);
        time = 140;
        Assert.False(input.ReleaseIfDue());
        input.TryTap(Candidate(150, 210), 135, () => true, (deadline, _) => time = deadline, CancellationToken.None);
        time = 145;
        Assert.True(input.ReleaseIfDue());
        Assert.False(input.ReleaseIfDue());
        Assert.Null(input.ReleaseDueMs);
        Assert.True(input.Stop());
        Assert.Equal([(false, 110d), (true, 145d)], events);
        Assert.Equal(1, input.PressCount);
        Assert.True(input.Consumed);
        Assert.Equal(110, input.Delivery!.KeyDownMs);
        Assert.Equal(145, input.Delivery.KeyUpMs);
    }

    [Theory]
    [InlineData(140, 200, 95)] // Keep capturing when more than 16 ms away.
    [InlineData(110, 130, 95)] // Insufficient calibration margin.
    [InlineData(50, 90, 95)] // Expired delivery.
    [InlineData(110, 170, 50)] // Stale image.
    [InlineData(110, 170, 105)] // Future timestamp.
    public void IneligiblePredictionNeverWaitsOrSends(double planned, double latest, double presentation)
    {
        var input = new PickpocketInputController(_ => Assert.Fail("Unexpected input"), () => 100);
        input.BeginRun();
        input.TryTap(Candidate(planned, latest), presentation, () => true, (_, _) => Assert.Fail("Unexpected wait"), CancellationToken.None);
        Assert.Equal(0, input.PressCount);
        Assert.True(input.Stop());
    }

    [Theory]
    [InlineData(false, 110)] // Focus/geometry/manual Space validation failed.
    [InlineData(true, 180)] // Scheduler overslept the deadline and image age.
    public void FinalValidationFailureDisarmsWithoutSending(bool valid, double wake)
    {
        var time = 100d;
        var input = new PickpocketInputController(_ => Assert.Fail("Unexpected input"), () => time);
        input.BeginRun();
        Assert.Throws<InvalidOperationException>(() => input.TryTap(Candidate(), 95, () => valid,
            (_, _) => time = wake, CancellationToken.None));
        Assert.True(input.Consumed);
        Assert.Equal(0, input.PressCount);
        Assert.True(input.Stop());
    }

    [Fact]
    public void StopBeforeKeyDownPreventsLateInput()
    {
        var time = 100d;
        var input = new PickpocketInputController(_ => Assert.Fail("Unexpected input"), () => time);
        input.BeginRun();
        Assert.Throws<OperationCanceledException>(() => input.TryTap(Candidate(), 95, () => true,
            (deadline, _) => { time = deadline; input.Stop(); }, CancellationToken.None));
        Assert.Equal(0, input.PressCount);
    }

    [Fact]
    public void StopDuringHoldReleasesOwnedSpaceAndStampsKeyUp()
    {
        var time = 100d;
        var events = new List<(bool Up, double Time)>();
        var input = new PickpocketInputController(up => events.Add((up, time)), () => time);
        input.BeginRun();
        input.TryTap(Candidate(), 95, () => true, (deadline, _) => time = deadline, CancellationToken.None);
        time = 120;
        Assert.True(input.Stop());
        Assert.Equal([(false, 110d), (true, 120d)], events);
        Assert.Equal(1, input.PressCount);
        Assert.Equal(120, input.Delivery!.KeyUpMs);
        Assert.Null(input.ReleaseDueMs);
        time = 200;
        Assert.False(input.ReleaseIfDue());
        Assert.True(input.Stop());
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void FailedReleaseRemainsOwnedAndStopRetriesIt()
    {
        var time = 100d;
        var releases = 0;
        var input = new PickpocketInputController(up => { if (up && ++releases == 1) throw new IOException("Injected release failure"); }, () => time);
        input.BeginRun();
        input.TryTap(Candidate(), 95, () => true, (deadline, _) => time = deadline, CancellationToken.None);
        time = 145;
        Assert.Throws<InvalidOperationException>(() => input.ReleaseIfDue());
        Assert.Null(input.Delivery!.KeyUpMs);
        Assert.True(input.Stop());
        Assert.Equal(2, releases);
        Assert.Equal(1, input.PressCount);
        Assert.Equal(145, input.Delivery!.KeyUpMs);
    }

    [Fact]
    public void ManualInterventionDisarmsUntilExplicitNewRun()
    {
        var input = new PickpocketInputController(_ => Assert.Fail("Unexpected input"), () => 100);
        input.BeginRun();
        input.Disarm("Manual Space observed.");
        input.TryTap(Candidate(), 95, () => true, (_, _) => Assert.Fail("Unexpected wait"), CancellationToken.None);
        Assert.Equal("Skipped", input.Delivery!.State);
        Assert.True(input.Stop());
        input.BeginRun();
        Assert.False(input.Consumed);
        Assert.Null(input.Delivery);
    }
}
