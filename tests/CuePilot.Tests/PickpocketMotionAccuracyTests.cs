using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit.Abstractions;

namespace CuePilot.Tests;

public sealed class PickpocketMotionAccuracyTests(ITestOutputHelper output)
{
    private sealed record Sample(double PresentationMilliseconds, PickpocketObservation Observation);

    [Theory]
    [InlineData("second-attempt.json")]
    [InlineData("third-attempt.json")]
    [InlineData("fourth-attempt.json")]
    [InlineData("automatic-rope.json")]
    [InlineData("automatic-ring.json")]
    public void PrecisionFitReducesHeldOutMarkerError(string file)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        var rows = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "Live", file)), options)!;
        var baseline = new PickpocketTimingPredictor();
        var precision = new PickpocketTimingPredictor();
        var oldErrors = new List<double>();
        var newErrors = new List<double>();
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var t = row.PresentationMilliseconds;
            // Hold geometry constant to measure motion estimation alone. Actual
            // engine/pixel replays separately cover randomized target selection.
            var observation = row.Observation with { Bands = [new(PickpocketBandColor.White, row.Observation.Bar.Left, row.Observation.Bar.Right)] };
            baseline.Observe(observation, PickpocketBandColor.White, t, t, new(0, 16));
            precision.Observe(observation, PickpocketBandColor.White, t, t, new(0, 16), precision: true);
            if (baseline.MotionFit is not { Count: >= 6, Span: >= 75 } oldFit || precision.MotionFit is not { } fit) continue;
            foreach (var horizon in new[] { 16d, 32d })
            {
                var future = t + horizon;
                var j = i + 1;
                var valid = true;
                for (; j < rows.Length; j++)
                {
                    var a = rows[j - 1];
                    var b = rows[j];
                    var gap = b.PresentationMilliseconds - a.PresentationMilliseconds;
                    if (b.Observation.State != PickpocketVisualState.Active || gap is <= 0 or > 60
                        || Math.Sign(b.Observation.MarkerX - a.Observation.MarkerX) != Math.Sign(oldFit.Velocity)) { valid = false; break; }
                    if (b.PresentationMilliseconds >= future) break;
                }
                if (!valid || j >= rows.Length) continue;
                var before = rows[j - 1];
                var after = rows[j];
                var truth = before.Observation.MarkerX + (after.Observation.MarkerX - before.Observation.MarkerX)
                    * (future - before.PresentationMilliseconds) / (after.PresentationMilliseconds - before.PresentationMilliseconds);
                oldErrors.Add(Math.Abs(oldFit.X + oldFit.Velocity * horizon - truth));
                newErrors.Add(Math.Abs(fit.X + fit.Velocity * horizon - truth));
            }
        }
        Assert.True(oldErrors.Count > 50);
        output.WriteLine($"{file} paired_forecasts={oldErrors.Count} baseline_mae_px={oldErrors.Average():F3} precision_mae_px={newErrors.Average():F3}");
        Assert.True(newErrors.Average() < oldErrors.Average(), "Precision must improve predictions on identical held-out future samples.");
    }

    [Fact]
    public void LongerPrecisionHistoryStillRejectsReversalAndSuddenSpeedChange()
    {
        foreach (var changedX in new[] { 212d, 235d })
        {
            var predictor = new PickpocketTimingPredictor();
            PickpocketObservation Frame(double x) => new(PickpocketVisualState.Active, new Rectangle(0, 100, 576, 23), x,
                [new(PickpocketBandColor.Red, 238, 242)], 1, "Controlled motion");
            for (var t = 0; t <= 288; t += 16)
                predictor.Observe(Frame(100 + .4 * t), PickpocketBandColor.Red, t, t + 3, new(0, 16), precision: true);
            var prediction = predictor.Observe(Frame(changedX), PickpocketBandColor.Red, 304, 307, new(0, 16), precision: true);
            Assert.False(prediction.CanSchedule, prediction.Reason);
        }
    }
}
