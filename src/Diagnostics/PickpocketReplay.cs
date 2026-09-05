using System.Diagnostics;
using System.Drawing;
using System.Text.Json;

namespace CuePilot;

internal static class PickpocketReplay
{
    internal sealed record Frame(string File, double PresentationMs, string? ExpectedState = null);

    internal static int Run(string manifestPath, string? targetColor, double advanceMs = 8)
    {
        if (!Enum.TryParse<PickpocketBandColor>(targetColor, true, out var color) || !Enum.IsDefined(color))
        {
            Console.Error.WriteLine("Specify --target-color White, Purple, Red, PaleGreen, Blue, or Yellow. Replay sends no input.");
            return 2;
        }
        if (!double.IsFinite(advanceMs) || advanceMs is < 0 or > 20)
        {
            Console.Error.WriteLine("Specify --advance-ms from 0 through 20. Replay sends no input.");
            return 2;
        }
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
            var frames = JsonSerializer.Deserialize<Frame[]>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (frames is not { Length: > 0 } || frames.Length > 10000) throw new InvalidDataException("Expected 1–10000 timestamped frames.");
            if (frames.Any(f => !double.IsFinite(f.PresentationMs) || f.PresentationMs < 0)
                || frames.Zip(frames.Skip(1)).Any(pair => pair.First.PresentationMs >= pair.Second.PresentationMs))
                throw new InvalidDataException("Frame timestamps must be finite, nonnegative, and strictly increasing.");
            var predictor = new PickpocketTimingPredictor();
            // Explicit simulated envelope; these values do not calibrate a live machine.
            var budget = new PickpocketTimingBudget(0, 16);
            var times = new List<double>();
            var mismatches = 0;
            var candidates = 0;
            string? previousKey = null;
            Console.WriteLine($"PICKPOCKET_REPLAY target={color} advance_ms={advanceMs:F0} simulated_input_delay_ms=0..16 input=disabled observed_results_are_unchanged=true");
            using (var warmup = new Bitmap(Path.Combine(directory, frames[0].File))) PickpocketDetector.Analyze(warmup);
            foreach (var frame in frames)
            {
                using var bitmap = new Bitmap(Path.Combine(directory, frame.File));
                var started = Stopwatch.GetTimestamp();
                var observation = PickpocketDetector.Analyze(bitmap);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                times.Add(elapsed);
                var prediction = predictor.Observe(observation, color, frame.PresentationMs, frame.PresentationMs + elapsed, budget, precision: true, tinyAdvanceMs: advanceMs);
                if (frame.ExpectedState is not null && frame.ExpectedState != observation.State.ToString())
                {
                    mismatches++;
                    Console.WriteLine($"MISMATCH file={frame.File} expected={frame.ExpectedState} actual={observation.State}");
                }
                var key = $"{observation.State}/{prediction.CanSchedule}/{prediction.Reason}";
                if (key != previousKey)
                {
                    Console.WriteLine($"t_ms={frame.PresentationMs:F2} state={observation.State} x={observation.MarkerX:F1} speed_px_s={prediction.SpeedPixelsPerSecond:F1} press_at_ms={prediction.PressAtMs?.ToString("F2") ?? "-"} uncertainty_px={prediction.UncertaintyPixels:F2} reason={prediction.Reason}");
                    previousKey = key;
                }
                if (prediction.CanSchedule)
                {
                    // Reserve at most one hypothetical action per observed attempt.
                    // No timer or input sender is created by this replay command.
                    candidates++;
                    predictor.MarkAttempted();
                }
            }
            times.Sort();
            Console.WriteLine($"frames={frames.Length} mismatches={mismatches} hypothetical_presses={candidates} detector_mean_ms={times.Average():F3} detector_p95_ms={times[(int)Math.Floor((times.Count - 1) * .95)]:F3} detector_max_ms={times[^1]:F3}");
            return mismatches == 0 ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"PICKPOCKET_REPLAY_FAILED {exception.Message}");
            return 2;
        }
    }
}
