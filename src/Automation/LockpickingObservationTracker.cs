using System.Diagnostics;

namespace CuePilot;

/// <summary>
/// Adds sequence and motion evidence to the detector's deliberately conservative
/// single-frame observations. This class never executes input.
/// </summary>
internal sealed class LockpickingObservationTracker
{
    private const double TrackTargetTolerance = 0.035;
    private const double ReadyRatio = 1.26;
    private const double MaximumFrameGapSeconds = 0.25;
    private const double MaximumFrameAgeMilliseconds = 100;
    private const double MaximumProcessingMilliseconds = 120;

    private readonly List<TargetTrack> tracks = [];
    private int hiddenCount;
    private long lastTimestamp;

    private const int RequiredBrightFrames = 2;
    private static readonly long MinimumBrightDwellTicks = Math.Max(1, Stopwatch.Frequency * 80 / 1000);

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
            ResetTrackMotion();
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

        if (observation.State != LockpickingVisualState.Numbered)
        {
            return observation;
        }

        var staleReason = GetStaleReason(frameAge, processingMilliseconds, gapSeconds, accumulatedFrames);
        var candidates = observation.Targets?.Count > 0
            ? observation.Targets
            : observation.Target is null ? [] : [observation.Target];
        var matchedTracks = new HashSet<TargetTrack>();
        var trackedTargets = new List<(TargetTrack Track, LockpickingTargetObservation Target, bool NewlyReady)>();
        foreach (var candidate in candidates.OrderByDescending(item => item.Confidence))
        {
            var track = tracks
                .Where(item => !matchedTracks.Contains(item))
                .OrderBy(item => TargetDistance(item.Last, candidate))
                .FirstOrDefault(item => TargetDistance(item.Last, candidate) <= TrackTargetTolerance);
            if (track is null)
            {
                track = new TargetTrack(candidate);
                tracks.Add(track);
            }
            matchedTracks.Add(track);
            trackedTargets.Add((track, UpdateTrack(track, candidate, timestamp, staleReason), track.NewlyReady));
        }
        foreach (var track in tracks.Where(item => !matchedTracks.Contains(item)).ToArray())
        {
            if (++track.MissingFrames > 3)
            {
                tracks.Remove(track);
            }
        }

        var stableTargets = trackedTargets.Where(item => item.Track.ConsecutiveFrames >= 2).ToArray();
        var readyTargets = stableTargets.Where(item => item.NewlyReady).ToArray();
        var selected = readyTargets.Length == 1
            ? readyTargets[0].Target
            : stableTargets.OrderByDescending(item => item.Target.Confidence).Select(item => item.Target).FirstOrDefault();

        if (staleReason is not null)
        {
            return observation with
            {
                Target = selected,
                Targets = trackedTargets.Select(item => item.Target).ToArray(),
                PredictedAction = "WAIT",
                Reason = staleReason,
            };
        }

        if (readyTargets.Length == 1)
        {
            return observation with
            {
                Target = selected,
                Targets = trackedTargets.Select(item => item.Target).ToArray(),
                PredictedAction = "CLICK (OBSERVE ONLY)",
                Reason = "One stable literal target completed its own bright-green READY dwell. Input remains disabled.",
            };
        }

        if (readyTargets.Length > 1)
        {
            return observation with
            {
                Target = null,
                Targets = trackedTargets.Select(item => item.Target).ToArray(),
                PredictedAction = "WAIT",
                Reason = "More than one target became READY together; input is withheld as ambiguous.",
            };
        }

        return observation with
        {
            Target = selected,
            Targets = trackedTargets.Select(item => item.Target).ToArray(),
            PredictedAction = stableTargets.Length == 0 ? "VERIFY" : "WAIT",
            Reason = stableTargets.Length == 0
                ? "Target candidates require one more matching frame before they are trusted."
                : "Targets are tracked independently. Waiting for one literal target's bright-green READY dwell.",
        };
    }

    internal void Reset()
    {
        ResetTarget();
        hiddenCount = 0;
        lastTimestamp = 0;
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
        tracks.Clear();
    }

    private void ResetTrackMotion()
    {
        foreach (var track in tracks)
        {
            track.ResetMotion();
        }
    }

    private static double TargetDistance(LockpickingTargetObservation first, LockpickingTargetObservation second) =>
        Math.Sqrt(Math.Pow(first.CenterX - second.CenterX, 2) + Math.Pow(first.CenterY - second.CenterY, 2));

    private LockpickingTargetObservation UpdateTrack(
        TargetTrack track,
        LockpickingTargetObservation candidate,
        long timestamp,
        string? staleReason)
    {
        track.MissingFrames = 0;
        track.ConsecutiveFrames++;
        track.Last = candidate;
        if (candidate.HasLiteralNumber && candidate.Number is not null)
        {
            if (track.Number is not null && track.Number != candidate.Number)
            {
                track.Number = null;
                track.HasLiteralNumber = false;
                track.LabelAmbiguous = true;
            }
            else if (!track.LabelAmbiguous && track.Number is null)
            {
                track.Number = candidate.Number;
                track.HasLiteralNumber = true;
            }
        }

        track.AddRingSample(timestamp, candidate.ApproachRatio);
        var velocity = track.CalculateVelocity();
        var timeToReady = CalculateTimeToReady(candidate.ApproachRatio, velocity);
        if (candidate.FillDensity >= 0.27)
        {
            track.BrightFrameCount++;
            track.BrightFirstTimestamp = track.BrightFirstTimestamp == 0 ? timestamp : track.BrightFirstTimestamp;
        }
        else
        {
            track.BrightFrameCount = 0;
            track.BrightFirstTimestamp = 0;
            track.ReadyReported = false;
        }
        var brightReady = track.BrightFrameCount >= RequiredBrightFrames
            && timestamp - track.BrightFirstTimestamp >= MinimumBrightDwellTicks;
        var ready = staleReason is null && track.ConsecutiveFrames >= 2 && track.HasLiteralNumber && brightReady;
        track.NewlyReady = ready && !track.ReadyReported;
        if (track.NewlyReady)
        {
            track.ReadyReported = true;
        }
        return candidate with
        {
            Number = track.Number,
            HasLiteralNumber = track.HasLiteralNumber,
            Phase = ready ? LockpickingTargetPhase.Ready : LockpickingTargetPhase.Approaching,
            RadialVelocity = velocity,
            TimeToReadyMilliseconds = timeToReady,
        };
    }

    private static double? CalculateTimeToReady(double ratio, double velocity) => ratio <= ReadyRatio
        ? 0
        : velocity >= -0.01 ? null : Math.Clamp((ratio - ReadyRatio) / -velocity * 1000, 0, 2000);

    private sealed class TargetTrack
    {
        internal TargetTrack(LockpickingTargetObservation candidate) => Last = candidate;
        internal LockpickingTargetObservation Last { get; set; }
        internal List<RingSample> RingSamples { get; } = [];
        internal int? Number { get; set; }
        internal bool HasLiteralNumber { get; set; }
        internal int ConsecutiveFrames { get; set; }
        internal int MissingFrames { get; set; }
        internal int BrightFrameCount { get; set; }
        internal long BrightFirstTimestamp { get; set; }
        internal bool ReadyReported { get; set; }
        internal bool NewlyReady { get; set; }
        internal bool LabelAmbiguous { get; set; }

        internal void ResetMotion()
        {
            RingSamples.Clear();
            BrightFrameCount = 0;
            BrightFirstTimestamp = 0;
            ReadyReported = false;
            NewlyReady = false;
        }

        internal void AddRingSample(long timestamp, double ratio)
        {
            if (ratio is < 1.15 or > 1.85) return;
            RingSamples.Add(new RingSample(timestamp, ratio));
            var cutoff = timestamp - (long)(Stopwatch.Frequency * 0.45);
            RingSamples.RemoveAll(item => item.Timestamp < cutoff);
            if (RingSamples.Count > 10) RingSamples.RemoveRange(0, RingSamples.Count - 10);
        }

        internal double CalculateVelocity()
        {
            if (RingSamples.Count < 2) return 0;
            var origin = RingSamples[0].Timestamp;
            var meanTime = RingSamples.Average(item => (item.Timestamp - origin) / (double)Stopwatch.Frequency);
            var meanRatio = RingSamples.Average(item => item.Ratio);
            var numerator = 0d;
            var denominator = 0d;
            foreach (var item in RingSamples)
            {
                var time = (item.Timestamp - origin) / (double)Stopwatch.Frequency;
                numerator += (time - meanTime) * (item.Ratio - meanRatio);
                denominator += Math.Pow(time - meanTime, 2);
            }
            return denominator <= double.Epsilon ? 0 : numerator / denominator;
        }
    }

    private sealed record RingSample(long Timestamp, double Ratio);
}
