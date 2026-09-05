namespace CuePilot;

/// <summary>Session cooldown survives observer stops/restarts. No input path.</summary>
internal sealed class PickpocketAttemptTracker
{
    internal const double CooldownMilliseconds = 180_000;
    private bool activeSeen;
    private bool completed;
    private int hiddenFrames;
    internal double CooldownUntilMs { get; private set; }
    internal int Attempt { get; private set; }

    internal void RestoreCooldown(double remainingMs, double nowMs)
    {
        if (!double.IsFinite(remainingMs) || !double.IsFinite(nowMs) || remainingMs <= 0) return;
        CooldownUntilMs = nowMs + Math.Min(CooldownMilliseconds, remainingMs);
        completed = true;
        activeSeen = false;
        hiddenFrames = 0;
    }

    internal bool Observe(PickpocketVisualState state, double nowMs)
    {
        if (!double.IsFinite(nowMs)) return false;
        if (nowMs < CooldownUntilMs) return false;
        if (completed)
        {
            if (state is not (PickpocketVisualState.Preparing or PickpocketVisualState.Hidden)) return false;
            completed = false;
            activeSeen = false;
        }
        if (state == PickpocketVisualState.Active && !activeSeen) { activeSeen = true; Attempt++; }
        hiddenFrames = state == PickpocketVisualState.Hidden ? hiddenFrames + 1 : 0;
        if (state is PickpocketVisualState.Grabbed or PickpocketVisualState.Missed || (activeSeen && hiddenFrames >= 3))
        {
            CooldownUntilMs = nowMs + CooldownMilliseconds;
            completed = true;
            activeSeen = false;
            return true;
        }
        return false;
    }

    internal void ResetMotion() { activeSeen = false; hiddenFrames = 0; }
    internal double RemainingMs(double nowMs) => Math.Max(0, CooldownUntilMs - nowMs);
}
