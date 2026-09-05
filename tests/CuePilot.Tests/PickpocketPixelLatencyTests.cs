using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Xunit.Abstractions;

namespace CuePilot.Tests;

public sealed class PickpocketPixelLatencyTests(ITestOutputHelper output)
{
    [Fact]
    public void FifthLiveAttemptRemainsActiveThroughExpensiveFrame()
    {
        PickpocketObservation? previous = null;
        foreach (var sample in new[] { 25, 29, 31, 32, 33, 34 })
        {
            using var frame = Load(sample);
            var clock = Stopwatch.StartNew();
            var observation = PickpocketDetector.Analyze(frame, new Rectangle(0, 0, frame.Width, frame.Height), previous);
            output.WriteLine($"sample={sample} state={observation.State} marker={observation.MarkerX} bar={observation.Bar} confidence={observation.Confidence:F3} elapsed={clock.Elapsed.TotalMilliseconds:F2}ms");
            Assert.Equal(sample == 25 ? PickpocketVisualState.Preparing : sample == 33 ? PickpocketVisualState.Missed
                : sample == 34 ? PickpocketVisualState.Hidden : PickpocketVisualState.Active, observation.State);
            if (sample == 32) Assert.InRange(observation.MarkerX, 700, 710);
            previous = observation.State == PickpocketVisualState.Hidden ? null : observation;
        }
    }

    [Fact]
    public void FifthLiveBrightFrameFitsActiveCadenceAndRejectsErasedHeader()
    {
        using var frame = Load(32);
        var previous = PickpocketDetector.Analyze(frame, new Rectangle(0, 0, frame.Width, frame.Height));
        Assert.Equal(PickpocketVisualState.Active, previous.State);
        for (var i = 0; i < 5; i++) PickpocketDetector.Analyze(frame, null, previous);
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 30; i++)
            Assert.Equal(PickpocketVisualState.Active, PickpocketDetector.Analyze(frame, null, previous).State);
        var mean = clock.Elapsed.TotalMilliseconds / 30;
        output.WriteLine($"fifth_bright_tracked_mean_ms={mean:F3}");
        Assert.True(mean < 16, $"Active detector averaged {mean:F2} ms.");
        foreach (var background in new[] { Color.White, Color.FromArgb(175, 175, 175), Color.Black })
        {
            using var erased = (Bitmap)frame.Clone();
            using (var graphics = Graphics.FromImage(erased))
            using (var brush = new SolidBrush(background))
                graphics.FillRectangle(brush, 505, 75, 155, 33);
            clock.Restart();
            Assert.Equal(PickpocketVisualState.Hidden, PickpocketDetector.Analyze(erased, null, previous).State);
            output.WriteLine($"erased_header={background} elapsed_ms={clock.Elapsed.TotalMilliseconds:F3}");
            Assert.True(clock.Elapsed.TotalMilliseconds < 100, "A missing header caused an unbounded search.");
        }
    }

    [Theory]
    [InlineData("SingleAttempt", "Widest", 579, 702, 1)]
    [InlineData("SingleAttempt", "Red", 809, 813, 0)]
    [InlineData("PrecisionAttempt", "Red", 809, 813, 1)]
    [InlineData("PrecisionAttempt", "Red", 809, 813, 0, false)]
    [InlineData("PrecisionAttempt", "Yellow", 979, 983, 1)]
    [InlineData("PrecisionAttempt", "RarestFirst", 979, 983, 1)]
    [InlineData("PrecisionAttempt", "RarestFirst", 979, 983, 0, false)]
    public async Task ReconstructedFifthLayoutSendsSpaceThroughPixelDetectorAndEngine(string mode, string policy, double left, double right, int expectedPresses, bool includeReturn = true)
    {
        // The live stall discarded the intervening frames. This is explicitly a
        // constructed motion sequence over its retained pixels, not recovered video.
        var now = 0d;
        var keys = new List<(bool Up, double Time)>();
        using var active = Load(29);
        using var preparing = Load(25);
        using (var stalled = Load(32))
        using (var graphics = Graphics.FromImage(active))
            graphics.DrawImage(stalled, new Rectangle(0, 0, active.Width, 329), new Rectangle(0, 0, active.Width, 329), GraphicsUnit.Pixel);
        var source = new MovingPanelSource(active, preparing, () => now, value => now = value, mode == "PrecisionAttempt" && includeReturn);
        var input = new PickpocketInputController(up => keys.Add((up, now)), () => now);
        var done = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observer = new PickpocketObserverEngine(() => source,
            _ => new(IntPtr.Zero, 3258, "FiveM_b3258_GTAProcess", "Constructed pixel fixture", new Rectangle(0, 0, 2560, 1440), true, false),
            _ => false, (policy, target) => new(policy, target, Path.Combine(Path.GetTempPath(), "CuePilotTests", "pickpocket-pixel-engine")), input,
            (bitmap, previous) =>
            {
                var clock = Stopwatch.StartNew();
                var observation = PickpocketDetector.Analyze(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height), previous);
                now += clock.Elapsed.TotalMilliseconds;
                return observation;
            }, () => now, (deadline, token) => { token.ThrowIfCancellationRequested(); now = Math.Max(now, deadline); });
        observer.StatusChanged += (_, status) => { if (!status.Observing && status.SampleCount > 0) done.TrySetResult(status); };
        // This constructed fixture models the original 16 ms total delay.
        // Keep its 8 ms yellow advance explicit; the native TNT replay tests 20 ms.
        observer.Configure(policy, mode, yellowAdvanceMs: 8);
        observer.Start(new() { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 3258 });
        var final = await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(expectedPresses, final.AutomatedPressCount);
        Assert.Equal(expectedPresses * 2, keys.Count);
        if (expectedPresses == 0) return;
        Assert.False(keys[0].Up);
        Assert.True(keys[1].Up);
        Assert.True(keys[1].Time - keys[0].Time >= 35);
        if (mode == "PrecisionAttempt") Assert.True(keys[0].Time > source.FirstTurnaround, "Tiny targets must skip the outward pass.");
        // Slivers trial an extra 8 ms lead: verify its assumed 16 ms total delay,
        // not a claim that actual delay is known or that every delay fits.
        foreach (var delay in mode == "PrecisionAttempt" ? new[] { 16d } : new[] { 0d, 8d, 16d })
            Assert.InRange(source.MarkerAt(keys[0].Time + delay), left, right);
        output.WriteLine($"pixel_engine_down_ms={keys[0].Time:F2} marker={source.MarkerAt(keys[0].Time):F2} up_ms={keys[1].Time:F2}");
    }

    private sealed class MovingPanelSource(Bitmap active, Bitmap preparing, Func<double> now, Action<double> setNow, bool includeReturn) : IFrameSource
    {
        private int index;
        private double started;
        private readonly Bitmap marker = ExtractMarker(active);
        public string Name => "DXGI constructed pixel fixture";
        internal double FirstTurnaround => started + (1277 - 527) / .38;
        internal double MarkerAt(double time)
        {
            var phase = (13 + Math.Max(0, time - started) * .38) % (2 * 763);
            return 514 + (phase <= 763 ? phase : 2 * 763 - phase);
        }
        public bool TryCapture(WindowTargetSettings target, Rectangle region, out FrameLease? frame, out FrameSourceStatus status)
        {
            if (index >= (includeReturn ? 260 : 55))
            {
                frame = null;
                status = new(FrameSourceState.CaptureFailed, Name, "Constructed fixture finished.", TimeSpan.Zero, 0);
                return false;
            }
            setNow(now() + 3); // representative capture work; detector adds measured work
            if (index == 1) started = now();
            var bitmap = (Bitmap)(index == 0 ? preparing : active).Clone();
            if (index > 0)
            {
                using var graphics = Graphics.FromImage(bitmap);
                // Move the actual marker strip, restoring its original location
                // from a nearby empty strip. Item regions/header stay from the PNG.
                graphics.DrawImage(active, new Rectangle(519, 329, 17, 63), new Rectangle(548, 329, 17, 63), GraphicsUnit.Pixel);
                graphics.DrawImage(marker, new Rectangle((int)Math.Round(MarkerAt(now())) - 8, 329, 17, 63),
                    new Rectangle(0, 0, 17, 63), GraphicsUnit.Pixel);
            }
            index++;
            status = new(FrameSourceState.Ready, Name, "Constructed fifth-layout pixels", TimeSpan.Zero, 3);
            frame = new(bitmap, status);
            return true;
        }
        private static Bitmap ExtractMarker(Bitmap source)
        {
            var marker = new Bitmap(17, 63);
            for (var y = 0; y < marker.Height; y++)
            for (var x = 0; x < marker.Width; x++)
            {
                var color = source.GetPixel(519 + x, 329 + y);
                if (color.G > 150 && color.G > color.R * 1.22 && color.G > color.B * 1.45) marker.SetPixel(x, y, color);
            }
            return marker;
        }
        public void Dispose() => marker.Dispose();
    }

    internal static Bitmap Load(int sample)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "Live");
        using var manifest = JsonDocument.Parse("[" + string.Join(',', File.ReadLines(Path.Combine(root, "fifth-frames.jsonl"))) + "]");
        var entry = manifest.RootElement.EnumerateArray().Single(item => item.GetProperty("sampleCount").GetInt32() == sample);
        using (var crop = new Bitmap(Path.Combine(root, $"fifth-{sample}.png")))
        {
            var region = entry.GetProperty("imageRegion");
            var frame = new Bitmap(1792, 518);
            using var graphics = Graphics.FromImage(frame);
            graphics.Clear(Color.Black);
            graphics.DrawImage(crop, new Rectangle(region.GetProperty("x").GetInt32(), region.GetProperty("y").GetInt32(), crop.Width, crop.Height),
                new Rectangle(0, 0, crop.Width, crop.Height), GraphicsUnit.Pixel);
            return frame;
        }
    }
}
