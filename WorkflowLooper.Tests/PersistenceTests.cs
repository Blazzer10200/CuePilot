using System.Text.Json;

namespace WorkflowLooper.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void VersionOnePatternLoadsAsVersionTwo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"workflow-looper-{Guid.NewGuid():N}.workflow.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "formatVersion": 1,
                  "name": "Legacy",
                  "durationMicroseconds": 1000,
                  "recordedWidth": 1920,
                  "recordedHeight": 1080,
                  "events": [
                    { "offsetMicroseconds": 1000, "type": "KeyDown", "virtualKey": 65, "scanCode": 30 }
                  ]
                }
                """);

            var restored = WorkflowStore.Load(path);

            Assert.Equal(2, restored.FormatVersion);
            Assert.Equal(1, restored.LoopCount);
            Assert.Equal(100, restored.PlaybackSpeedPercent);
            Assert.Single(restored.Events);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RoutineSettingsClampUnsafeValues()
    {
        var settings = new TriggeredRoutineSettings
        {
            TapIntervalMilliseconds = 10,
            HoldMilliseconds = 500,
            MaximumDurationSeconds = 1,
            VisualCue = new VisualCueSettings { SimilarityPercent = 100 },
        };

        settings.Clamp();

        Assert.Equal(20, settings.TapIntervalMilliseconds);
        Assert.Equal(19, settings.HoldMilliseconds);
        Assert.Equal(5, settings.MaximumDurationSeconds);
        Assert.Equal(95, settings.VisualCue.SimilarityPercent);
    }

    [Fact]
    public void VisualCueStoresFingerprintWithoutBitmapData()
    {
        var cue = new VisualCueSettings { Fingerprint = Convert.ToBase64String(new byte[240]), Enabled = true };
        var json = JsonSerializer.Serialize(cue, WorkflowJson.Options);

        Assert.DoesNotContain("png", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bitmap", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(cue.IsConfigured);
    }
}
