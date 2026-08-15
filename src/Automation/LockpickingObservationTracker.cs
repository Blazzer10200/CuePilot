using System.Diagnostics;

namespace CuePilot;

/// <summary>
/// Adds sequence and motion evidence to the detector's deliberately conservative
/// single-frame observations. This class never executes input.
/// </summary>
internal sealed class LockpickingObservationTracker
{
    private const double CommittedTargetTolerance = 0.035;
    private const double PendingTargetTolerance = 0.025;
    private const double OuterRingRatio = 1.30;
    private const double ReadyRatio = 1.26;
    private const double MinimumShrink = 0.06;
    private const double MaximumFrameGapSeconds = 0.25;
    private const double MaximumFrameAgeMilliseconds = 100;
    private const double MaximumProcessingMilliseconds = 120;

    private readonly List<RingSample> ringSamples = [];
    private LockpickingTargetObservation? committedTarget;
    private LockpickingTargetObservation? pendingTarget;
    private RingSample? pendingRingSample;
    private int pendingTargetCount;
    private int inferredTargetNumber;
    private int hiddenCount;
    private long lastTimestamp;
    private bool outerRingSeen;
    private bool readyReported;

    internal LockpickingObservation Track(
        LockpickingObservation observation,
        long timestamp,
        TimeSpan frameAge,
        double processingMilliseconds,
        uint accumulatedFrames = 1)
    {
        var gapSeconds = lastTimestamp == 0
            ? 0
            : (timestamp - lastTimestamp) / (double)Stopwatch.Frequency;
        lastTimestamp = timestamp;

        if (gapSeconds > MaximumFrameGapSeconds || gapSeconds < 0)
        {
            ResetMotion();
        }

        if (observation.State == LockpickingVisualState.Hidden)
        {
            hiddenCount++;
            if (hiddenCount >= 5)
            {
                Reset();
                lastTimestamp = timestamp;
            }
            return observation;
        }

        hiddenCount = 0;
        if (observation.State is LockpickingVisualState.Spin or LockpickingVisualState.Open)
        {
            ResetTarget();
            return observation;
        }

        if (observation.State != LockpickingVisualState.Numbered || observation.Target is null)
        {
            return observation;
        }

        var current = observation.Target;
        var sample = new RingSample(timestamp, current.ApproachRatio);
        if (committedTarget is not null && TargetDistance(committedTarget, current) <= CommittedTargetTolerance)
        {
            pendingTarget = null;
            pendingRingSample = null;
            pendingTargetCount = 0;
            current = current with { Number = inferredTargetNumber };
            AddRingSample(sample);
        }
        else
        {
            var matchesPending = pendingTarget is not null
                && TargetDistance(pendingTarget, current) <= PendingTargetTolerance;
            pendingTargetCount = matchesPending ? pendingTargetCount + 1 : 1;
            pendingTarget = current;
            pendingRingSample = matchesPending ? pendingRingSample : sample;
            var candidateNumber = inferredTargetNumber + 1;
            current = current with { Number = candidateNumber };

            if (pendingTargetCount < 2)
            {
                return observation with
                {
                    Target = current,
                    PredictedAction = "VERIFY",
                    Reason = "Target candidate requires one more matching frame before it is trusted.",
                };
            }

            inferredTargetNumber = candidateNumber;
            committedTarget = current;
            ResetMotion();
            if (pendingRingSample is not null)
            {
                AddRingSample(pendingRingSample);
            }
            AddRingSample(sample);
            pendingTarget = null;
            pendingRingSample = null;
            pendingTargetCount = 0;
        }

        var staleReason = GetStaleReason(frameAge, processingMilliseconds, gapSeconds, accumulatedFrames);
        var velocity = CalculateVelocity();
        var timeToReady = CalculateTimeToReady(current.ApproachRatio, velocity);
        var maximumRatio = ringSamples.Count == 0 ? current.ApproachRatio : ringSamples.Max(item => item.Ratio);
        var shrinking = maximumRatio - current.ApproachRatio >= MinimumShrink;
        var motionReady = outerRingSeen
            && current.ApproachRatio <= ReadyRatio
            && ringSamples.Count >= 2
            && (shrinking || velocity <= -0.25);
        var brightReady = current.FillDensity >= 0.27;
        var ready = staleReason is null
            && (brightReady || motionReady);
        var newlyReady = ready && !readyReported;
        if (newlyReady)
        {
            readyReported = true;
        }

        current = current with
        {
            Number = inferredTargetNumber,
            Phase = ready ? LockpickingTargetPhase.Ready : LockpickingTargetPhase.Approaching,
            RadialVelocity = velocity,
            TimeToReadyMilliseconds = timeToReady,
        };

        if (staleReason is not null)
        {
            return observation with
            {
                Target = current,
                PredictedAction = "WAIT",
                Reason = staleReason,
            };
        }

        if (newlyReady)
        {
            return observation with
            {
                Target = current,
                PredictedAction = "CLICK (OBSERVE ONLY)",
                Reason = brightReady
                    ? "Stable target entered the bright-green READY state. Input remains disabled."
                    : "Stable target and inward ring motion reached the calibrated READY boundary. Input remains disabled.",
            };
        }

        if (readyReported && ready)
        {
            return observation with
            {
                Target = current,
                PredictedAction = "WAIT",
                Reason = "READY was already reported for this target; waiting for the next verified target.",
            };
        }

        var reason = !outerRingSeen
            ? "Stable target acquired; waiting for a distinct outer approach ring."
            : timeToReady is > 0 and < 1000
                ? $"Approach ring is shrinking; estimated READY in {timeToReady.Value:0} ms."
                : "Approach ring is tracked outside the READY boundary.";
        return observation with { Target = current, PredictedAction = "WAIT", Reason = reason };
    }

    internal void Reset()
    {
        ResetTarget();
        inferredTargetNumber = 0;
        hiddenCount = 0;
        lastTimestamp = 0;
    }

    private void AddRingSample(RingSample sample)
    {
        if (sample.Ratio is < 1.15 or > 1.85)
        {
            return;
        }
        ringSamples.Add(sample);
        if (sample.Ratio >= OuterRingRatio)
        {
            outerRingSeen = true;
        }
        var cutoff = sample.Timestamp - (long)(Stopwatch.Frequency * 0.45);
        ringSamples.RemoveAll(item => item.Timestamp < cutoff);
        if (ringSamples.Count > 10)
        {
            ringSamples.RemoveRange(0, ringSamples.Count - 10);
        }
    }

    private double CalculateVelocity()
    {
        if (ringSamples.Count < 2)
        {
            return 0;
        }
        var origin = ringSamples[0].Timestamp;
        var meanTime = ringSamples.Average(item => (item.Timestamp - origin) / (double)Stopwatch.Frequency);
        var meanRatio = ringSamples.Average(item => item.Ratio);
        var numerator = 0d;
        var denominator = 0d;
        foreach (var item in ringSamples)
        {
            var time = (item.Timestamp - origin) / (double)Stopwatch.Frequency;
            numerator += (time - meanTime) * (item.Ratio - meanRatio);
            denominator += Math.Pow(time - meanTime, 2);
        }
        return denominator <= double.Epsilon ? 0 : numerator / denominator;
    }

    private static double? CalculateTimeToReady(double ratio, double velocity)
    {
        if (ratio <= ReadyRatio)
        {
            return 0;
        }
        if (velocity >= -0.01)
        {
            return null;
        }
        return Math.Clamp((ratio - ReadyRatio) / -velocity * 1000, 0, 2000);
    }

    private static string? GetStaleReason(
        TimeSpan frameAge,
        double processingMilliseconds,
        double gapSeconds,
        uint accumulatedFrames)
    {
        if (accumulatedFrames > 30)
        {
            return $"Capture skipped ahead by {accumulatedFrames} frames; motion history is not trusted.";
        }
        if (frameAge == TimeSpan.MaxValue)
        {
            return "DXGI did not report a new desktop image; motion prediction withheld.";
        }
        if (frameAge.TotalMilliseconds > MaximumFrameAgeMilliseconds)
        {
            return $"Frame is {frameAge.TotalMilliseconds:0} ms old; action prediction withheld.";
        }
        if (processingMilliseconds > MaximumProcessingMilliseconds)
        {
            return $"Capture and processing took {processingMilliseconds:0} ms; action prediction withheld.";
        }
        if (gapSeconds > MaximumFrameGapSeconds)
        {
            return $"Frame gap was {gapSeconds * 1000:0} ms; motion history was reset.";
        }
        return null;
    }

    private void ResetTarget()
    {
        committedTarget = null;
        pendingTarget = null;
        pendingRingSample = null;
        pendingTargetCount = 0;
        ResetMotion();
    }

    private void ResetMotion()
    {
        ringSamples.Clear();
        outerRingSeen = false;
        readyReported = false;
    }

    private static double TargetDistance(LockpickingTargetObservation first, LockpickingTargetObservation second) =>
        Math.Sqrt(Math.Pow(first.CenterX - second.CenterX, 2) + Math.Pow(first.CenterY - second.CenterY, 2));

    private sealed record RingSample(long Timestamp, double Ratio);
}
