namespace CuePilot.Tests;

public sealed class FishingCastAccelerationTests
{
    [Fact]
    public void GateClicksOnceAfterConfiguredDelay()
    {
        var gate = new FishingCastAccelerationGate(5_000);

        Assert.Equal(FishingCastAccelerationAction.Wait, gate.Observe(TimeSpan.FromMilliseconds(4_999), false, false));
        Assert.Equal(FishingCastAccelerationAction.Click, gate.Observe(TimeSpan.FromMilliseconds(5_000), false, false));
        Assert.Equal(FishingCastAccelerationAction.Wait, gate.Observe(TimeSpan.FromSeconds(8), false, false));
    }

    [Fact]
    public void GateSkipsPermanentlyWhenCircularMeterAppearsFirst()
    {
        var gate = new FishingCastAccelerationGate(5_000);

        Assert.Equal(FishingCastAccelerationAction.Skip, gate.Observe(TimeSpan.FromSeconds(4), true, false));
        Assert.Equal(FishingCastAccelerationAction.Wait, gate.Observe(TimeSpan.FromSeconds(6), false, false));
    }

    [Fact]
    public void GateSkipsPermanentlyWhenActionablePromptAppearsFirst()
    {
        var gate = new FishingCastAccelerationGate(5_000);

        Assert.Equal(FishingCastAccelerationAction.Skip, gate.Observe(TimeSpan.FromSeconds(5), false, true));
        Assert.Equal(FishingCastAccelerationAction.Wait, gate.Observe(TimeSpan.FromSeconds(6), false, false));
    }
}
