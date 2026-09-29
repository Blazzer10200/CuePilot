using System.Drawing;

namespace CuePilot.Tests;

public sealed class AdaptiveRoutineEngineTests
{
    private sealed class TrackingFrameSource : IFrameSource
    {
        internal bool Disposed { get; private set; }
        public string Name => "tracking";

        public bool TryCapture(WindowTargetSettings target, Rectangle relativeRegion, out FrameLease? frame, out FrameSourceStatus status)
            => throw new NotSupportedException();

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Arm_WhenStartupThrows_ReturnsToFaultedAndDisposesSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "CuePilot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var releases = new List<string>();
            var safety = new InputReleaseSafety(
                (key, up) => releases.Add($"{key}:{up}"),
                up => releases.Add($"Left:{up}"));
            var source = new TrackingFrameSource();
            FishingDebugSession? session = null;
            using var engine = new AdaptiveRoutineEngine(
                safety,
                () => source,
                routineSettings => session = new FishingDebugSession(routineSettings, root));
            engine.StatusChanged += (_, status) =>
            {
                if (status.State == RoutineState.Casting)
                {
                    throw new InvalidOperationException("listener failed");
                }
            };
            var settings = AppSettings.Defaults().Routine;
            settings.TargetWindow.ProcessName = "FiveM_test";

            var error = Assert.Throws<InvalidOperationException>(() => engine.Arm(settings));

            Assert.Equal("listener failed", error.Message);
            Assert.Equal(RoutineState.Faulted, engine.State);
            Assert.True(source.Disposed);
            Assert.NotNull(session);
            Assert.False(session.Snapshot.Active);
            Assert.StartsWith("Faulted", session.Snapshot.Outcome, StringComparison.Ordinal);
            Assert.Empty(releases);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
