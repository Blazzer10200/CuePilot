namespace CuePilot;

internal enum FishingCastAccelerationAction
{
    Wait,
    Click,
    Skip,
}

internal sealed class FishingCastAccelerationGate(int delayMilliseconds)
{
    private readonly TimeSpan delay = TimeSpan.FromMilliseconds(Math.Clamp(delayMilliseconds, 3_000, 10_000));
    private bool completed;

    internal FishingCastAccelerationAction Observe(
        TimeSpan elapsed,
        bool meterVisible,
        bool actionablePromptVisible)
    {
        if (completed)
        {
            return FishingCastAccelerationAction.Wait;
        }

        if (meterVisible || actionablePromptVisible)
        {
            completed = true;
            return FishingCastAccelerationAction.Skip;
        }

        if (elapsed < delay)
        {
            return FishingCastAccelerationAction.Wait;
        }

        completed = true;
        return FishingCastAccelerationAction.Click;
    }
}
