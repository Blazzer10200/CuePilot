using System.Drawing;

namespace CuePilot.Tests;

public sealed class PickpocketCaptureTimingTests
{
    [Theory]
    [InlineData(100d, 100d, 25d)]
    [InlineData(null, 120d, 5d)]
    public async Task CaptureCleanupCannotShiftAnExplicitPresentationTime(double? imageTime, double expectedTime, double expectedAge)
    {
        var now = 125d;
        var finished = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observer = new PickpocketObserverEngine(() => new DelayedCapture(imageTime),
            _ => new(IntPtr.Zero, 3258, "FiveM_b3258_GTAProcess", "Timestamp fixture", new Rectangle(0, 0, 1920, 1080), true, false),
            _ => false, (policy, target) => new(policy, target, Path.Combine(Path.GetTempPath(), "CuePilotTests", "pickpocket-capture-clock")),
            new(_ => Assert.Fail("Observation must not send input")),
            (_, _) => new(PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), 80,
                [new(PickpocketBandColor.Purple, 200, 215)], 1, "Timestamp fixture"),
            () => now, (deadline, token) => { token.ThrowIfCancellationRequested(); now = Math.Max(now, deadline); });
        observer.StatusChanged += (_, status) => { if (!status.Observing && status.SampleCount > 0) finished.TrySetResult(status); };
        observer.Start(new() { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 3258 });
        var final = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expectedTime, final.PresentationMilliseconds);
        Assert.Equal(expectedAge, final.FrameAgeMilliseconds);
    }

    private sealed class DelayedCapture(double? imageTime) : IFrameSource
    {
        private bool captured;
        public string Name => "DXGI timestamp fixture";
        public bool TryCapture(WindowTargetSettings target, Rectangle region, out FrameLease? frame, out FrameSourceStatus status)
        {
            if (captured)
            {
                frame = null;
                status = new(FrameSourceState.CaptureFailed, Name, "Timestamp fixture complete", TimeSpan.MaxValue, 0);
                return false;
            }
            captured = true;
            // Age was measured at 105 ms; cleanup/health checks return at 125 ms.
            status = new(FrameSourceState.Ready, Name, "Delayed capture return", TimeSpan.FromMilliseconds(5), 5,
                PresentationMilliseconds: imageTime);
            frame = new(new Bitmap(2, 2), status);
            return true;
        }
        public void Dispose() { }
    }
}
