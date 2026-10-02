using Xunit;
using System.Drawing;
using System.Text.Json;

namespace CuePilot.Tests;

[Collection("Detector timing")]
public sealed class PickpocketFlightRecorderTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "CuePilotTests", "pickpocket-flight", Guid.NewGuid().ToString("N"));

    private static async Task<(string Evidence, string Root)> RunAutomatic(bool preparation, bool headerless)
    {
        var root = NewRoot();
        var input = new PickpocketInputController(_ => { });
        var count = 0;
        var started = 0d;
        using var observer = new PickpocketObserverEngine(() => new Source(),
            _ => new(IntPtr.Zero, 3258, "FiveM_b3258_GTAProcess", "Fixture", new Rectangle(0, 0, 1920, 1080), true, false),
            _ => false, (_, target) => new("SingleAttempt", target, root), input,
            (_, _) =>
            {
                count++;
                var time = HighResolutionSampleClock.NowMilliseconds;
                if (started == 0) started = time;
                var elapsed = time - started;
                var state = preparation && count <= 2 ? PickpocketVisualState.Preparing
                    : input.PressCount > 0 || elapsed > 1000 ? PickpocketVisualState.Grabbed : PickpocketVisualState.Active;
                return new(state, new Rectangle(0, 100, 576, 23), 80 + elapsed * .38,
                    [new(PickpocketBandColor.White, 250, 450)], 1, "Synthetic pipeline fixture") { HeaderlessFrames = headerless ? 1 : 0 };
            });
        var cooldown = new TaskCompletionSource<PickpocketObserveStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.StatusChanged += (_, status) => { if (status.State == "Cooldown") cooldown.TrySetResult(status); };
        observer.Configure("Widest", "SingleAttempt");
        observer.Start(new() { ProcessName = "FiveM_b3258_GTAProcess", ProcessId = 3258 });
        var result = await cooldown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(SpinWait.SpinUntil(() => !observer.IsObserving, 3000));
        Assert.StartsWith(root, Path.GetFullPath(result.EvidenceDirectory), StringComparison.OrdinalIgnoreCase);
        return (result.EvidenceDirectory, root);
    }

    [Fact]
    public async Task NoTapAutomaticRunWritesIncident()
    {
        var (evidence, root) = await RunAutomatic(preparation: false, headerless: true);
        using var incident = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "incident.json")));
        Assert.Equal("panel-seen-never-armed", incident.RootElement.GetProperty("category").GetString());
        Assert.True(incident.RootElement.GetProperty("activeSamples").GetInt32() > 0);
        Assert.Equal("DXGI fixture", incident.RootElement.GetProperty("captureBackend").GetString());
        var summary = File.ReadAllLines(Path.Combine(root, "incidents.jsonl")).Single();
        Assert.Contains("panel-seen-never-armed", summary);
        Assert.Contains("PANEL-SEEN-NEVER-ARMED".ToLowerInvariant(), PickpocketFlightRecorder.ReadIncidentSummary(evidence));
    }

    [Fact]
    public async Task TappedRunWritesNoIncident()
    {
        var (evidence, root) = await RunAutomatic(preparation: true, headerless: false);
        Assert.False(File.Exists(Path.Combine(evidence, "incident.json")));
        Assert.False(File.Exists(Path.Combine(root, "incidents.jsonl")));
    }

    [Theory]
    [InlineData(0, false, 0, "no-panel-seen")]
    [InlineData(5, false, 0, "panel-seen-never-armed")]
    [InlineData(5, true, 0, "armed-no-shot")]
    [InlineData(5, true, 1, "manual-space")]
    public void IncidentCategories(int active, bool armed, int manual, string expected) =>
        Assert.Equal(expected, PickpocketFlightRecorder.Categorize(active, armed, manual));

    [Fact]
    public void ShortcutLogRotatesAtLimitKeepingOneBackup()
    {
        var path = Path.Combine(NewRoot(), "shortcuts.jsonl");
        for (var i = 0; i < 3; i++) PickpocketFlightRecorder.Append(path, new { command = "c", n = i }, rotateBytes: 20);
        Assert.True(File.Exists(path + ".1"));
        Assert.Single(File.ReadAllLines(path));
        Assert.Contains("\"n\":2", File.ReadAllText(path));
        Assert.Contains("\"n\":1", File.ReadAllText(path + ".1"));
        Assert.False(File.Exists(path + ".2"));
    }

    [Fact]
    public void ShortcutLogIoFailureIsSwallowed()
    {
        var blocker = Path.Combine(NewRoot(), "file");
        Directory.CreateDirectory(Path.GetDirectoryName(blocker)!);
        File.WriteAllText(blocker, "x");
        PickpocketFlightRecorder.Append(Path.Combine(blocker, "sub", "shortcuts.jsonl"), new { a = 1 });
    }

    [Fact]
    public void BridgeLogsRejectedShortcutAndStop()
    {
        var log = Path.Combine(NewRoot(), "shortcuts.jsonl");
        var input = new StringReader("{\"id\":\"1\",\"command\":\"start_pickpocket_observe\"}\n{\"id\":\"2\",\"command\":\"stop_pickpocket_observe\"}\n");
        UiBridge.Run(input, new StringWriter(), () => new AppSettings(), _ => { }, () => [], null, log);
        var lines = File.ReadAllLines(log).Select(l => JsonDocument.Parse(l).RootElement).ToArray();
        Assert.Equal("rejected", lines[0].GetProperty("action").GetString());
        Assert.Equal("start_pickpocket_observe", lines[0].GetProperty("command").GetString());
        Assert.False(lines[0].GetProperty("wasObserving").GetBoolean());
        Assert.False(string.IsNullOrEmpty(lines[0].GetProperty("error").GetString()));
        Assert.Equal("stopped", lines[1].GetProperty("action").GetString());
        Assert.Equal("Observe", lines[1].GetProperty("inputMode").GetString());
    }

    private sealed class Source : IFrameSource
    {
        public string Name => "DXGI fixture";
        public bool TryCapture(WindowTargetSettings target, Rectangle region, out FrameLease? frame, out FrameSourceStatus status)
        {
            status = new(FrameSourceState.Ready, Name, "Synthetic pipeline fixture", TimeSpan.Zero, 0);
            frame = new(new Bitmap(2, 2), status);
            return true;
        }
        public void Dispose() { }
    }
}
