using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CuePilot.Tests;

public sealed class PickpocketRecordedEngineTests
{
    private sealed record Sample(int SampleCount, double MonotonicMilliseconds, double PresentationMilliseconds,
        PickpocketObservation Observation, bool? ManualSpaceDown = null, PickpocketInputDelivery? InputDelivery = null);

    [Theory]
    [InlineData("first-attempt.json", "White")]
    [InlineData("second-attempt.json", "White")]
    [InlineData("third-attempt.json", "PaleGreen")]
    [InlineData("fourth-attempt.json", "PaleGreen")]
    [InlineData("automatic-ring.json", "Purple", "PurpleBlueWhite", "PrecisionAttempt", true)]
    [InlineData("automatic-red-miss.json", "Red", "Red", "PrecisionAttempt", true, 8)]
    [InlineData("automatic-ruby.json", "Red", "Red", "PrecisionAttempt", true)]
    [InlineData("automatic-ruby.json", "Yellow", "Yellow", "PrecisionAttempt")]
    [InlineData("automatic-ruby.json", "Yellow", "RarestFirst", "PrecisionAttempt")]
    [InlineData("automatic-tnt-miss.json", "Yellow", "Yellow", "PrecisionAttempt", true, 12, 20)]
    [InlineData("automatic-tnt-miss.json", "Yellow", "Yellow", "PrecisionAttempt", true, 6, 14)]
    [InlineData("automatic-tnt-early-miss.json", "Yellow", "RarestFirst", "PrecisionAttempt", true, -6, 14)]
    public async Task RecordedRunSendsExactlyOneTapThroughRealEngine(string file, string expectedColor,
        string policy = "Widest", string mode = "SingleAttempt", bool recordedAutomatic = false, double advanceMs = 0, int yellowAdvanceMs = 8)
    {
        var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        var frames = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Pickpocket", "Live", file)), json)!;
        // Raw live traces retain the machine's monotonic clock origin.
        var now = frames[0].MonotonicMilliseconds;
        var utc = 1_000_000L;
        var statePath = Path.Combine(Path.GetTempPath(), "CuePilotTests", "pickpocket-recorded-state", Guid.NewGuid().ToString("N") + ".json");
        var sessionState = new PickpocketSessionState(statePath, () => utc);
        var keys = new List<(bool Up, double Time)>();
        var source = new RecordedSource(frames, () => now, value => now = value);
        var input = new PickpocketInputController(up => keys.Add((up, now)), () => now);
        PickpocketBand? sentBand = null;
        var done = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observer = new PickpocketObserverEngine(() => source,
            _ => new(IntPtr.Zero, 3258, "FiveM_b3258_GTAProcess", "Recorded fixture", new Rectangle(0, 0, 2560, 1440), true, false),
            _ => source.Current?.ManualSpaceDown == true,
            (policy, target) => new(policy, target, Path.Combine(Path.GetTempPath(), "CuePilotTests", "pickpocket-recorded-engine")), input,
            (_, previous) => PickpocketDetector.StabilizeMarkerOcclusion(source.Current!.Observation, previous),
            () => now, (deadline, token) => { token.ThrowIfCancellationRequested(); now = Math.Max(now, deadline); }, sessionState: sessionState);
        observer.StatusChanged += (_, status) =>
        {
            if (status.AutomatedPressCount == 1 && sentBand is null && status.SelectedBandIndex is int index)
                sentBand = status.Observation.Bands[index];
            if (!status.Observing && status.SampleCount > 0) done.TrySetResult(status);
        };
        observer.Configure(policy, mode, yellowAdvanceMs: yellowAdvanceMs);
        observer.Start(new() { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 3258 });
        var final = await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, final.AutomatedPressCount);
        Assert.Equal(2, keys.Count);
        Assert.False(keys[0].Up);
        Assert.True(keys[1].Up);
        Assert.Equal(expectedColor, sentBand!.Color.ToString());
        Assert.True(keys[1].Time - keys[0].Time >= 35);
        if (recordedAutomatic)
        {
            var delivered = frames.First(f => f.InputDelivery?.State == "Sent").InputDelivery!;
            // The original run ended Active when the real key froze the marker.
            // Reproduce its plan; do not invent future moving frames after Grabbed.
            Assert.InRange(Math.Abs(keys[0].Time - (delivered.PlannedMs!.Value - advanceMs)), 0, 1);
            var result = frames.First(f => f.Observation.State is PickpocketVisualState.Grabbed or PickpocketVisualState.Missed);
            if (advanceMs == 0) Assert.InRange(result.Observation.MarkerX, sentBand.Left, sentBand.Right);
            else
            {
                // The recorded result remains a MISS. Only the earlier plan is
                // verified here; a counterfactual successful grab is not footage.
                Assert.Equal(PickpocketVisualState.Missed, result.Observation.State);
                Assert.False(result.Observation.MarkerX >= sentBand.Left && result.Observation.MarkerX <= sentBand.Right);
            }
        }
        // A hypothetical tiny-yellow shot uses the provisional 16 ms assumption;
        // wide baseline replays still test the full original delay envelope.
        else foreach (var delay in mode == "PrecisionAttempt" ? new[] { 16d } : new[] { 0d, 8d, 16d })
        {
            var delivery = keys[0].Time + delay;
            var before = frames.Last(f => f.PresentationMilliseconds <= delivery && f.Observation.State == PickpocketVisualState.Active);
            var after = frames.First(f => f.PresentationMilliseconds >= delivery && f.Observation.State == PickpocketVisualState.Active);
            var span = after.PresentationMilliseconds - before.PresentationMilliseconds;
            var x = span == 0 ? before.Observation.MarkerX : before.Observation.MarkerX
                + (after.Observation.MarkerX - before.Observation.MarkerX) * (delivery - before.PresentationMilliseconds) / span;
            Assert.InRange(x, sentBand.Left, sentBand.Right);
        }
        Assert.False(final.InputArmed);
        Assert.Single(final.RecentAttempts!);
        Assert.Equal(expectedColor, final.RecentAttempts![0].Color.ToString());
        utc += 70_000;
        var restored = new PickpocketSessionState(statePath, () => utc);
        Assert.Equal(110_000, restored.RemainingMs);
        Assert.Equal(final.RecentAttempts[0], Assert.Single(restored.Recent));
        Assert.Contains("Automated Space taps: 1", final.Debug!.Report);
        if (recordedAutomatic) Assert.Contains($"Shot result: {(advanceMs == 0 ? "Grabbed" : "Missed")} | {expectedColor}", final.Debug.Report);
    }

    private sealed class RecordedSource(Sample[] frames, Func<double> now, Action<double> setNow) : IFrameSource
    {
        private int index;
        internal Sample? Current { get; private set; }
        public string Name => "DXGI recorded fixture";
        public bool TryCapture(WindowTargetSettings target, Rectangle region, out FrameLease? frame, out FrameSourceStatus status)
        {
            if (index >= frames.Length)
            {
                frame = null;
                status = new(FrameSourceState.CaptureFailed, Name, "Recorded fixture finished.", TimeSpan.Zero, 0);
                return false;
            }
            Current = frames[index++];
            setNow(Math.Max(now(), Current.MonotonicMilliseconds));
            status = new(FrameSourceState.Ready, Name, "Recorded fixture", TimeSpan.FromMilliseconds(now() - Current.PresentationMilliseconds), 0,
                PresentationMilliseconds: Current.PresentationMilliseconds);
            frame = new(new Bitmap(2, 2), status);
            return true;
        }
        public void Dispose() { }
    }
}
