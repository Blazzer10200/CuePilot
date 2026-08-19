using System.Drawing;

namespace CuePilot;

// Offline visual replay uses the live fishing detectors without constructing a
// routine or input router. It is safe to run against captured frames while
// tuning detector changes because it cannot target or control FiveM.
internal sealed record FishingReplayTransition(
    int FrameIndex,
    FishingHudState State,
    FishingPromptKind Prompt,
    bool MeterVisible,
    bool Caught,
    bool Failed,
    double Confidence);

internal sealed record FishingReplayReport(
    int FrameCount,
    int MeterFrames,
    int PromptFrames,
    int CaughtFrames,
    double MeanDetectorMilliseconds,
    IReadOnlyList<FishingReplayTransition> Transitions);

internal static class FishingReplayService
{
    internal static FishingReplayReport Replay(IEnumerable<string> framePaths)
    {
        ArgumentNullException.ThrowIfNull(framePaths);

        var tracker = new FishingMeterTracker();
        var transitions = new List<FishingReplayTransition>();
        var frameCount = 0;
        var meterFrames = 0;
        var promptFrames = 0;
        var caughtFrames = 0;
        var elapsedTicks = 0L;
        string? previousKey = null;

        foreach (var framePath in framePaths)
        {
            using var frame = new Bitmap(framePath);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var meter = FishingMeterService.AnalyzeFrameDetailed(frame, tracker).Observation;
            var prompt = FishingPromptDetector.Analyze(frame);
            elapsedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;

            if (meter.IsVisible) meterFrames++;
            if (prompt.Kind != FishingPromptKind.None) promptFrames++;
            if (meter.IsCaught) caughtFrames++;

            // A real HUD state outranks the inferred active-meter state. The
            // detector may still see a fading meter behind a result panel, so
            // this keeps the replay useful for state-transition regressions.
            var state = prompt.State != FishingHudState.None
                ? prompt.State
                : meter.IsVisible ? FishingHudState.Casting : FishingHudState.None;
            var confidence = Math.Max(prompt.StateConfidence, meter.Confidence);
            var key = $"{state}:{prompt.Kind}:{meter.IsVisible}:{meter.IsCaught}:{meter.IsFailed}";
            if (!string.Equals(previousKey, key, StringComparison.Ordinal))
            {
                transitions.Add(new FishingReplayTransition(
                    frameCount,
                    state,
                    prompt.Kind,
                    meter.IsVisible,
                    meter.IsCaught,
                    meter.IsFailed,
                    confidence));
                previousKey = key;
            }

            frameCount++;
        }

        var meanMilliseconds = frameCount == 0
            ? 0
            : elapsedTicks * 1000d / System.Diagnostics.Stopwatch.Frequency / frameCount;
        return new FishingReplayReport(
            frameCount,
            meterFrames,
            promptFrames,
            caughtFrames,
            meanMilliseconds,
            transitions);
    }
}
