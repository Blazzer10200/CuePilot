namespace CuePilot;

/// <summary>Measured or deliberately conservative bounds; replay defaults are not hardware calibration.</summary>
internal sealed record PickpocketTimingBudget(double MinimumInputDelayMs, double MaximumInputDelayMs, double MaximumFrameAgeMs = 40, double MaximumHorizonMs = 50)
{
    internal bool IsValid => double.IsFinite(MinimumInputDelayMs) && double.IsFinite(MaximumInputDelayMs)
        && double.IsFinite(MaximumFrameAgeMs) && double.IsFinite(MaximumHorizonMs)
        && MinimumInputDelayMs >= 0 && MaximumInputDelayMs >= MinimumInputDelayMs
        && MaximumFrameAgeMs is > 0 and <= 100 && MaximumHorizonMs is > 0 and <= 100;
}

internal sealed record PickpocketTimingPrediction(
    bool CanSchedule, double? PressAtMs, double? LatestPressAtMs,
    double SpeedPixelsPerSecond, double UncertaintyPixels, string Reason)
{
    internal static PickpocketTimingPrediction Wait(string reason, double speed = 0, double uncertainty = 0) => new(false, null, null, speed, uncertainty, reason);
}

/// <summary>
/// Pure timing model. All times are milliseconds on one monotonic clock; presentation
/// time is the image time, not analysis completion. This class never sends input.
/// </summary>
internal sealed class PickpocketTimingPredictor
{
    private readonly Queue<(double Time, double X)> samples = new();
    private PickpocketObservation? previous;
    private PickpocketBand? target;
    private PickpocketBandColor? selected;
    private bool attempted;
    internal (double X, double Velocity, double Span, int Count)? MotionFit { get; private set; }

    internal void MarkAttempted() => attempted = true;
    internal void Reset()
    {
        MotionFit = null;
        samples.Clear();
        previous = null;
        target = null;
        selected = null;
        attempted = false;
    }

    internal PickpocketTimingPrediction Observe(PickpocketObservation observation, PickpocketBandColor color,
        double presentationMs, double nowMs, PickpocketTimingBudget budget, int? bandIndex = null, bool precision = false, double tinyAdvanceMs = 8)
    {
        MotionFit = null;
        if (observation.State == PickpocketVisualState.Preparing && previous?.State != PickpocketVisualState.Preparing)
            attempted = false;
        if (observation.State is PickpocketVisualState.Grabbed or PickpocketVisualState.Missed) attempted = true;
        var prior = previous;
        previous = observation;
        if (observation.State != PickpocketVisualState.Active || attempted)
        {
            samples.Clear();
            target = null;
            return PickpocketTimingPrediction.Wait(attempted ? "Attempt complete; wait for a new preparation state." : "Waiting for active play.");
        }
        if (!budget.IsValid || !double.IsFinite(tinyAdvanceMs) || tinyAdvanceMs is < 0 or > 20 || !double.IsFinite(presentationMs) || !double.IsFinite(nowMs)
            || nowMs < presentationMs || nowMs - presentationMs > budget.MaximumFrameAgeMs
            || !double.IsFinite(observation.MarkerX) || observation.Confidence < 0.80 || observation.Bar.Width <= 0)
        {
            samples.Clear();
            return PickpocketTimingPrediction.Wait("Invalid timing, stale image, or uncertain detection.");
        }

        var candidates = bandIndex is int index
            ? index >= 0 && index < observation.Bands.Count && observation.Bands[index].Color == color ? [observation.Bands[index]] : Array.Empty<PickpocketBand>()
            : observation.Bands.Where(b => b.Color == color).ToArray();
        if (candidates.Length != 1 || !double.IsFinite(candidates[0].Left) || !double.IsFinite(candidates[0].Right)
            || candidates[0].Width <= 0 || candidates[0].Left < observation.Bar.Left || candidates[0].Right > observation.Bar.Right)
        {
            samples.Clear();
            return PickpocketTimingPrediction.Wait("Selected target is missing or ambiguous.");
        }
        var nextTarget = candidates[0];
        var scale = observation.Bar.Width / 576d;
        if (selected != color || target is null || Math.Abs(target.Left - nextTarget.Left) > 2 * scale
            || Math.Abs(target.Right - nextTarget.Right) > 2 * scale || prior is null
            || Math.Abs(prior.Bar.Left - observation.Bar.Left) > 3 * scale
            || Math.Abs(prior.Bar.Top - observation.Bar.Top) > 3 * scale
            || Math.Abs(prior.Bar.Width - observation.Bar.Width) > 3 * scale)
            samples.Clear();
        selected = color;
        target = nextTarget;

        if (samples.Count > 0)
        {
            var last = samples.Last();
            if (presentationMs <= last.Time)
            {
                samples.Clear();
                return PickpocketTimingPrediction.Wait("Repeated or out-of-order image timestamp.");
            }
            if (presentationMs - last.Time > 60 || observation.MarkerX == last.X
                || (samples.Count >= 2 && Math.Sign(observation.MarkerX - last.X) != Math.Sign(last.X - samples.ElementAt(samples.Count - 2).X)))
            {
                samples.Clear();
                return PickpocketTimingPrediction.Wait("Motion stopped, reversed, or capture skipped too far ahead.");
            }
        }
        samples.Enqueue((presentationMs, observation.MarkerX));
        // Small targets are sensitive to pixel quantization and uneven DXGI
        // presentation intervals. Fit more of the current sweep; all existing
        // reversal/gap/geometry resets and residual rejection still apply.
        var maximumSamples = precision ? 16 : 8;
        var maximumHistoryMs = precision ? 280 : 140;
        while (samples.Count > maximumSamples || (samples.Count > 1 && presentationMs - samples.Peek().Time > maximumHistoryMs)) samples.Dequeue();
        if (samples.Count < 4 || presentationMs - samples.Peek().Time < 35)
            return PickpocketTimingPrediction.Wait("Gathering fresh motion samples.");

        // Center time around the latest presentation to avoid large-clock cancellation.
        var meanTime = samples.Average(s => s.Time - presentationMs);
        var meanX = samples.Average(s => s.X);
        var variance = samples.Sum(s => Math.Pow(s.Time - presentationMs - meanTime, 2));
        var velocity = samples.Sum(s => (s.Time - presentationMs - meanTime) * (s.X - meanX)) / variance;
        if (!double.IsFinite(velocity) || velocity == 0)
            return PickpocketTimingPrediction.Wait("No stable motion.");
        var fittedX = meanX - velocity * meanTime;
        var residual = samples.Max(s => Math.Abs(s.X - (fittedX + velocity * (s.Time - presentationMs))));
        var span = presentationMs - samples.Peek().Time;
        MotionFit = (fittedX, velocity, span, samples.Count);
        var centerTime = presentationMs + (target.Center - fittedX) / velocity;
        // Include localization error and growth of measured model error while predicting.
        var extrapolation = Math.Max(0, centerTime - presentationMs);
        var uncertainty = 1.5 * scale + residual * (1 + 2 * extrapolation / span);
        var speed = velocity * 1000;
        if (!double.IsFinite(centerTime) || centerTime < nowMs)
            return PickpocketTimingPrediction.Wait("Target center has already passed.", speed, uncertainty);
        if (residual > 2 * scale)
            return PickpocketTimingPrediction.Wait("Motion changed; prediction is not stable enough.", speed, uncertainty);
        PickpocketTimingPrediction PrecisionCenter()
        {
            if (samples.Count < 6 || span < 75)
                return PickpocketTimingPrediction.Wait("Precision: observing more motion before a narrow shot.", speed, uncertainty);
            var nominalDelay = (budget.MinimumInputDelayMs + budget.MaximumInputDelayMs) / 2;
            // First live red return shot stopped 4 px beyond center at 385 px/s.
            // Trial an 8 ms advance for slivers only; this is not a calibrated
            // game-delay estimate and does not change successful wider targets.
            var advanceMs = target.Width <= 6 * scale ? tinyAdvanceMs : 0;
            var planned = centerTime - nominalDelay - advanceMs;
            var windowMs = target.Width / Math.Abs(velocity);
            if (planned < nowMs)
                return PickpocketTimingPrediction.Wait("Precision center deadline passed; tracking the next sweep.", speed, uncertainty);
            if (planned > nowMs + budget.MaximumHorizonMs)
                return PickpocketTimingPrediction.Wait($"Precision: tracking a {windowMs:F1} ms target window.", speed, uncertainty);
            // A best-effort center shot, NOT a claim that the full delay/error
            // envelope fits. Preserve a tight host deadline and all input gates.
            return new(true, planned, planned + Math.Min(4, windowMs / 2), speed, uncertainty,
                $"Experimental center shot: {windowMs:F1} ms target window; {advanceMs:F0} ms early correction. Timing remains uncalibrated; a miss is possible.");
        }
        if (target.Width <= 2 * uncertainty)
            return precision ? PrecisionCenter() : PickpocketTimingPrediction.Wait("Target window is smaller than the timing uncertainty.", speed, uncertainty);
        var entry = velocity > 0 ? target.Left + uncertainty : target.Right - uncertainty;
        var exit = velocity > 0 ? target.Right - uncertainty : target.Left + uncertainty;
        var earliest = presentationMs + (entry - fittedX) / velocity - budget.MinimumInputDelayMs;
        var latest = presentationMs + (exit - fittedX) / velocity - budget.MaximumInputDelayMs;
        if (earliest > latest || latest < nowMs)
            return precision ? PrecisionCenter() : PickpocketTimingPrediction.Wait("Target window is smaller than the timing uncertainty.", speed, uncertainty);
        var pressAt = Math.Max(nowMs, (earliest + latest) / 2);
        if (pressAt > nowMs + budget.MaximumHorizonMs)
            return PickpocketTimingPrediction.Wait("Tracking toward the selected target.", speed, uncertainty);
        return new PickpocketTimingPrediction(true, pressAt, latest, speed, uncertainty,
            "Replay candidate only; live execution must revalidate foreground, stop gate, target, and deadline.");
    }
}
