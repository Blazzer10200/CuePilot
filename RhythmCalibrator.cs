using System.Diagnostics;

namespace WorkflowLooper;

internal sealed class RhythmCalibrator : IDisposable
{
    private readonly PhysicalMouseMonitor monitor = new();
    private readonly List<double> downTimes = [];
    private readonly List<double> holdTimes = [];
    private double? currentDown;

    internal int ClickCount => downTimes.Count;

    internal RhythmCalibrator() => monitor.LeftButtonChanged += HandleMouse;

    internal void Start()
    {
        downTimes.Clear();
        holdTimes.Clear();
        currentDown = null;
        monitor.Start();
    }

    internal RhythmCalibration Complete()
    {
        monitor.Stop();
        if (downTimes.Count < 3 || holdTimes.Count < 2)
        {
            throw new InvalidOperationException("Record at least three manual clicks before using the calibration.");
        }

        var intervals = downTimes.Zip(downTimes.Skip(1), (left, right) => right - left).Where(value => value is >= 20 and <= 5_000).ToArray();
        if (intervals.Length == 0)
        {
            throw new InvalidOperationException("No usable click intervals were captured.");
        }

        return new RhythmCalibration(downTimes.Count, (int)Math.Round(Median(intervals)), (int)Math.Round(Median(holdTimes)));
    }

    private void HandleMouse(object? sender, PhysicalMouseEventArgs e)
    {
        var milliseconds = e.Timestamp * 1_000d / Stopwatch.Frequency;
        if (e.IsDown)
        {
            currentDown = milliseconds;
            downTimes.Add(milliseconds);
        }
        else if (currentDown is not null)
        {
            holdTimes.Add(Math.Clamp(milliseconds - currentDown.Value, 1, 5_000));
            currentDown = null;
        }
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2d : ordered[middle];
    }

    public void Dispose() => monitor.Dispose();
}
