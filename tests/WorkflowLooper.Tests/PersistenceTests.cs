namespace WorkflowLooper.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void RoutineSettingsClampUnsafeValues()
    {
        var settings = new FishingRoutineSettings
        {
            MaximumDurationSeconds = 1,
            FishingLowerTensionPercent = 90,
            FishingUpperTensionPercent = 10,
            FishingSampleMilliseconds = 1,
            FishingMinimumPulseMilliseconds = 1,
            FishingMaximumPulseMilliseconds = 500,
            FishingMinimumRestMilliseconds = 1,
        };

        settings.Clamp();

        Assert.Equal(5, settings.MaximumDurationSeconds);
        Assert.Equal(80, settings.FishingLowerTensionPercent);
        Assert.Equal(85, settings.FishingUpperTensionPercent);
        Assert.Equal(20, settings.FishingSampleMilliseconds);
        Assert.Equal(20, settings.FishingMinimumPulseMilliseconds);
        Assert.Equal(120, settings.FishingMaximumPulseMilliseconds);
        Assert.Equal(20, settings.FishingMinimumRestMilliseconds);
    }

    [Fact]
    public void FishingFeedbackSettingsRoundTrip()
    {
        var settings = AppSettings.Defaults();
        settings.Routine.FishingLowerTensionPercent = 52;
        settings.Routine.FishingUpperTensionPercent = 74;
        settings.Routine.FishingSampleMilliseconds = 35;
        settings.Routine.FishingMinimumPulseMilliseconds = 30;
        settings.Routine.FishingMaximumPulseMilliseconds = 85;
        settings.Routine.FishingMinimumRestMilliseconds = 65;

        var restored = SettingsStore.RoundTripForTest(settings);

        Assert.Equal(52, restored.Routine.FishingLowerTensionPercent);
        Assert.Equal(74, restored.Routine.FishingUpperTensionPercent);
        Assert.Equal(35, restored.Routine.FishingSampleMilliseconds);
        Assert.Equal(30, restored.Routine.FishingMinimumPulseMilliseconds);
        Assert.Equal(85, restored.Routine.FishingMaximumPulseMilliseconds);
        Assert.Equal(65, restored.Routine.FishingMinimumRestMilliseconds);
    }

    [Fact]
    public void VersionTwoFishingSettingsMigrateToBoundedPulsesAndKeepTarget()
    {
        var restored = SettingsStore.DeserializeAndMigrateForTest("""
            {
              "formatVersion": 2,
              "record": { "key": "D1" },
              "playback": { "key": "D2" },
              "emergencyStop": { "key": "Pause" },
              "routine": {
                "fishingLowerTensionPercent": 55,
                "fishingUpperTensionPercent": 75,
                "targetWindow": {
                  "processName": "FiveM_b3258_GTAProcess",
                  "windowTitle": "FiveM",
                  "requireForeground": true
                }
              }
            }
            """);

        Assert.Equal(7, restored.FormatVersion);
        Assert.Equal("fishing", restored.SelectedProfile);
        Assert.Equal("FiveM_b3258_GTAProcess", restored.Routine.TargetWindow.ProcessName);
        Assert.Equal(55, restored.Routine.FishingLowerTensionPercent);
        Assert.Equal(68, restored.Routine.FishingUpperTensionPercent);
        Assert.Equal(35, restored.Routine.FishingMinimumPulseMilliseconds);
        Assert.Equal(90, restored.Routine.FishingMaximumPulseMilliseconds);
        Assert.Equal(70, restored.Routine.FishingMinimumRestMilliseconds);
        Assert.False(restored.Routine.CollectOnTimeout);
    }

    [Fact]
    public void VersionSevenProfilesKeepTheirExistingPulseEnvelope()
    {
        var restored = SettingsStore.DeserializeAndMigrateForTest("""
            {
              "formatVersion": 7,
              "selectedProfile": "fishing",
              "emergencyStop": { "key": "Pause" },
              "routine": {
                "fishingMinimumPulseMilliseconds": 35,
                "fishingMaximumPulseMilliseconds": 90,
                "fishingMinimumRestMilliseconds": 70,
                "targetWindow": { "processName": "FiveM_b3258_GTAProcess" }
              }
            }
            """);

        Assert.Equal(7, restored.FormatVersion);
        Assert.Equal(35, restored.Routine.FishingMinimumPulseMilliseconds);
        Assert.Equal(90, restored.Routine.FishingMaximumPulseMilliseconds);
        Assert.Equal(70, restored.Routine.FishingMinimumRestMilliseconds);
        Assert.Equal("FiveM_b3258_GTAProcess", restored.Routine.TargetWindow.ProcessName);
    }

}
