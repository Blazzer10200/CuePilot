using Xunit;
using System.Drawing;
using System.Text.Json;

namespace CuePilot.Tests;

public sealed class PickpocketInputTests
{
    [Theory]
    [InlineData("SingleAttempt", true, 1)]
    [InlineData("SingleAttempt", false, 0)]
    [InlineData("Observe", true, 0)]
    [InlineData("PrecisionAttempt", true, 1)]
    [InlineData("PrecisionAttempt", false, 0)]
    public async Task EngineRequiresNewPreparationAndRecordsOneTapThenDisarms(string mode, bool preparation, int expectedPresses)
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
                var time = PickpocketSampleClock.NowMilliseconds;
                if (started == 0) started = time;
                var elapsed = time - started;
                var state = preparation && count <= 2 ? PickpocketVisualState.Preparing
                    : input.PressCount > 0 || elapsed > 1000 ? PickpocketVisualState.Grabbed : PickpocketVisualState.Active;
                return new(state, new Rectangle(0, 100, 576, 23), 80 + elapsed * .38,
                    [new(PickpocketBandColor.White, 250, 450)], 1, "Synthetic pipeline fixture");
            });
        var completed = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.StatusChanged += (_, status) => { if (status.State == "Cooldown") completed.TrySetResult(status); };
        observer.Configure("Widest", mode);
        observer.Start(new() { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 3258 });
        var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
            Assert.NotNull(result.InputDelivery?.KeyUpMs);
            Assert.Contains("Automated Space taps: 1", observer.Status.Debug!.Report);
        }
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
    public void OneTapWaitsForDeadlineAndReleasesExactlyOnce()
    {
        var time = 100d;
        var events = new List<(bool Up, double Time)>();
        var input = new PickpocketInputController(up => events.Add((up, time)), () => time);
        input.BeginRun();
        input.TryTap(Candidate(), 95, () => true, (deadline, _) => time = deadline, CancellationToken.None);
        input.TryTap(Candidate(150, 210), 145, () => true, (deadline, _) => time = deadline, CancellationToken.None);
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
    public void CancellationDuringHoldReleasesOwnedSpace()
    {
        var time = 100d;
        var events = new List<bool>();
        using var cancellation = new CancellationTokenSource();
        var input = new PickpocketInputController(up => events.Add(up), () => time);
        input.BeginRun();
        Assert.Throws<OperationCanceledException>(() => input.TryTap(Candidate(), 95, () => true, (deadline, token) =>
        {
            time = deadline;
            if (events.Count == 1) { cancellation.Cancel(); input.Stop(); }
            token.ThrowIfCancellationRequested();
        }, cancellation.Token));
        Assert.Equal([false, true], events);
        Assert.Equal(1, input.PressCount);
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
        Assert.Throws<InvalidOperationException>(() => input.TryTap(Candidate(), 95, () => true,
            (deadline, _) => time = deadline, CancellationToken.None));
        Assert.True(input.Stop());
        Assert.Equal(2, releases);
        Assert.Equal(1, input.PressCount);
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
