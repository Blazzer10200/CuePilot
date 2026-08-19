namespace CuePilot;

// Read-only setup validation shared by the Fishing UI and the routine's
// preflight.  It never emits an input event; the routine may optionally ask
// Windows to activate FiveM before performing the same checks.
internal sealed record FishingSetupCheck(bool Passed, string Detail);

internal sealed record FishingSetupVerification(
    FishingSetupCheck Target,
    FishingSetupCheck Input,
    FishingSetupCheck Capture,
    string CaptureBackend,
    double CaptureMilliseconds,
    int WindowWidth,
    int WindowHeight)
{
    public bool Ready => Target.Passed && Input.Passed && Capture.Passed;

    public string Detail => !Target.Passed ? Target.Detail
        : !Input.Passed ? Input.Detail
        : !Capture.Passed ? Capture.Detail
        : $"Setup verified · {CaptureBackend} {CaptureMilliseconds:0.0} ms · {WindowWidth}×{WindowHeight}.";
}

internal static class FishingSetupVerifier
{
    internal static async Task<FishingSetupVerification> VerifyAsync(
        FishingRoutineSettings settings,
        IFrameSource frameSource,
        TargetInputRouter input,
        bool activateTarget,
        CancellationToken token)
    {
        if (!WindowTargetService.TryResolve(settings.TargetWindow, out var target, out var targetDetail))
        {
            return Unavailable(targetDetail);
        }

        if (target.IsMinimized)
        {
            return Unavailable("FiveM is minimized. Restore it, then verify setup again.", target.Bounds.Width, target.Bounds.Height);
        }

        var capability = activateTarget
            ? await input.PrepareAsync(settings.TargetWindow, token)
            : input.Probe(settings.TargetWindow);
        var inputCheck = new FishingSetupCheck(capability.Ready, capability.Detail);

        var captured = FishingMeterService.Observe(frameSource, settings.TargetWindow, out var captureStatus);
        var captureCheck = new FishingSetupCheck(
            captureStatus.State == FrameSourceState.Ready,
            captureStatus.State == FrameSourceState.Ready
                ? $"{captureStatus.Backend} capture is ready in {captureStatus.CaptureMilliseconds:0.0} ms."
                : captureStatus.Detail);

        return new FishingSetupVerification(
            new FishingSetupCheck(true, targetDetail),
            inputCheck,
            captureCheck,
            captureStatus.Backend,
            captureStatus.CaptureMilliseconds,
            target.Bounds.Width,
            target.Bounds.Height);
    }

    private static FishingSetupVerification Unavailable(string detail, int width = 0, int height = 0) => new(
        new FishingSetupCheck(false, detail),
        new FishingSetupCheck(false, "Skipped until FiveM is available."),
        new FishingSetupCheck(false, "Skipped until FiveM is available."),
        "Unavailable",
        0,
        width,
        height);
}
