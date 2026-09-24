namespace CuePilot;

/// <summary>Session cooldown survives observer stops/restarts. No input path.</summary>
internal sealed class PickpocketAttemptTracker
{
    internal const double CooldownMilliseconds = 180_000;
    /// <summary>
    /// How long the panel must stay missing before an attempt without a result
    /// counts as ended while its tap is still available. Scenery such as grass
    /// can hide a live panel from the detector for over two seconds; a false end
    /// starts a cooldown that blocks the rest of that minigame.
    /// </summary>
    internal const double DisappearanceMilliseconds = 3_000;
    private bool activeSeen;
    private bool completed;
    private int hiddenFrames;
    private double hiddenSinceMs = double.NaN;
    internal double CooldownUntilMs { get; private set; }
    internal int Attempt { get; private set; }

    internal void RestoreCooldown(double remainingMs, double nowMs)
    {
        if (!double.IsFinite(remainingMs) || !double.IsFinite(nowMs) || remainingMs <= 0) return;
        CooldownUntilMs = nowMs + Math.Min(CooldownMilliseconds, remainingMs);
        completed = true;
        activeSeen = false;
        hiddenFrames = 0;
        hiddenSinceMs = double.NaN;
    }

    /// <param name="inputSpent">This run's one tap is sent or cancelled, so a false end blocks nothing.</param>
    internal bool Observe(PickpocketVisualState state, double nowMs, bool inputSpent = false)
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
        if (state != PickpocketVisualState.Hidden) hiddenSinceMs = double.NaN;
        else if (double.IsNaN(hiddenSinceMs)) hiddenSinceMs = nowMs;
        var disappeared = inputSpent ? hiddenFrames >= 3
            : !double.IsNaN(hiddenSinceMs) && nowMs - hiddenSinceMs >= DisappearanceMilliseconds;
        // Only an attempt seen in play can end. A result-like frame with no Active
        // before it is scenery, and its cooldown would block the next real attempt.
        if (activeSeen && (state is PickpocketVisualState.Grabbed or PickpocketVisualState.Missed || disappeared))
        {
            CooldownUntilMs = nowMs + CooldownMilliseconds;
            completed = true;
            activeSeen = false;
            return true;
        }
        return false;
    }

    internal void ResetMotion() { activeSeen = false; hiddenFrames = 0; hiddenSinceMs = double.NaN; }
    internal double RemainingMs(double nowMs) => Math.Max(0, CooldownUntilMs - nowMs);
}
