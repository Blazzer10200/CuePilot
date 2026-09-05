using System.Drawing;
using System.Text.Json;

namespace CuePilot.Tests;

public sealed class PickpocketSessionStateTests
{
    private static string StatePath() => Path.Combine(Path.GetTempPath(), "CuePilotTests", "pickpocket-history", Guid.NewGuid().ToString("N"), "state.json");
    private static PickpocketObserveStatus Result(PickpocketVisualState state = PickpocketVisualState.Missed) => PickpocketObserveStatus.Stopped with
    {
        Observation = new(state, new Rectangle(0, 0, 576, 23), 244, [], 1, "Recorded result"), AutomatedPressCount = 1
    };

    [Fact]
    public void RestartRestoresFiveNewestResultsAndOnlyRemainingCooldownWithoutArming()
    {
        var now = 1_000_000L;
        var path = StatePath();
        var store = new PickpocketSessionState(path, () => now);
        for (var i = 0; i < 7; i++)
        {
            now += 200_000;
            store.Complete(Result(), new("Missed", PickpocketBandColor.Yellow, 4, i), null);
        }
        now += 70_000;
        var restored = new PickpocketSessionState(path, () => now);
        Assert.Equal(110_000, restored.RemainingMs);
        Assert.Equal(new double?[] { 6, 5, 4, 3, 2 }, restored.Recent.Select(r => r.OffsetPixels));
        using var observer = new PickpocketObserverEngine(clockNow: () => 500, sessionState: restored);
        observer.Configure("RarestFirst", "PrecisionAttempt");
        Assert.False(observer.Status.Observing);
        Assert.False(observer.Status.InputArmed);
        Assert.Equal(5, observer.Status.RecentAttempts!.Count);
        Assert.InRange(observer.Status.CooldownUntilUnixMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 109_000, 110_001);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(document.RootElement.TryGetProperty("inputArmed", out _));
        now += 110_001;
        Assert.Equal(0, new PickpocketSessionState(path, () => now).RemainingMs);
    }

    [Fact]
    public void FullHistoryKeepsAttemptSettingsAndPaginatesIndependentlyOfRecentStatus()
    {
        var now = 1_000_000L;
        var path = StatePath();
        var store = new PickpocketSessionState(path, () => ++now);
        for (var index = 0; index < 12; index++)
            store.Complete(Result() with { YellowAdvanceMs = 17, RedAdvanceMs = 8, EvidenceDirectory = $"C:\\evidence\\session-{index}", InputMode = "PrecisionAttempt", TargetPolicy = "RarestFirst" },
                new("Missed", PickpocketBandColor.Yellow, 4, -5), new(PickpocketBandColor.Yellow, 10, 14, "TNT Recipe"));
        Assert.Equal(5, store.Recent.Count);
        using var query = JsonDocument.Parse(JsonSerializer.Serialize(store.Query(1, "Missed"), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Assert.Equal(12, query.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(5, query.RootElement.GetProperty("attempts").GetArrayLength());
        var attempt = query.RootElement.GetProperty("attempts")[0];
        Assert.Equal("TNT Recipe", attempt.GetProperty("itemName").GetString());
        Assert.Equal(17, attempt.GetProperty("yellowAdvanceMs").GetInt32());
        Assert.Equal("session-6", attempt.GetProperty("sessionId").GetString());
    }

    [Fact]
    public void VersionOneHistoryMigratesWithoutInventingNewMetadata()
    {
        var path = StatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {"version":1,"cooldownUntilUnixMs":1000500,"recent":[
              {"id":"legacy-attempt","endedAtUnixMs":1000000,"outcome":"Missed","color":"Yellow","widthPixels":4.5,"offsetPixels":-2,"automaticPresses":1}
            ]}
            """);

        var store = new PickpocketSessionState(path, () => 1_000_100);

        Assert.Equal(400, store.RemainingMs);
        var attempt = Assert.Single(store.Recent);
        Assert.Equal("legacy-attempt", attempt.Id);
        Assert.Equal(PickpocketBandColor.Yellow, attempt.Color);
        Assert.Null(attempt.ItemName);
        Assert.Null(attempt.YellowAdvanceMs);
        Assert.Null(store.Error);
    }

    [Fact]
    public void ClockRollbackIsBoundedAndRestoredTrackerUsesMonotonicTime()
    {
        var now = 1_000_000L;
        var path = StatePath();
        new PickpocketSessionState(path, () => now).Complete(Result(), null, new(PickpocketBandColor.Red, 238, 242));
        now -= 3_600_000;
        var restored = new PickpocketSessionState(path, () => now);
        Assert.Equal(180_000, restored.RemainingMs);
        var tracker = new PickpocketAttemptTracker();
        tracker.RestoreCooldown(restored.RemainingMs, 200);
        now += 9_000_000; // Later wall clock changes cannot end a running cooldown early.
        Assert.False(tracker.Observe(PickpocketVisualState.Active, 1000));
        Assert.Equal(179_200, tracker.RemainingMs(1000));
        Assert.Equal(0, tracker.RemainingMs(180_200));
        Assert.False(tracker.Observe(PickpocketVisualState.Hidden, 180_201));
        Assert.False(tracker.Observe(PickpocketVisualState.Active, 180_202));
        Assert.Equal(1, tracker.Attempt);
    }

    [Fact]
    public void StorageFailureRetainsCurrentCooldownAndResultAndReportsTheProblem()
    {
        var path = StatePath();
        Directory.CreateDirectory(path); // Cannot replace a directory with the state file.
        var store = new PickpocketSessionState(path, () => 1_000_000);
        store.Complete(Result(PickpocketVisualState.Hidden), null, new(PickpocketBandColor.Red, 238, 242));
        Assert.Contains("could not be saved", store.Error);
        Assert.Equal(180_000, store.RemainingMs);
        Assert.Equal("Ended", Assert.Single(store.Recent).Outcome);
        Assert.Null(store.Recent[0].OffsetPixels);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":99,\"cooldownUntilUnixMs\":0,\"recent\":[]}")]
    [InlineData("{\"version\":1,\"cooldownUntilUnixMs\":0,\"recent\":[null]}")]
    public void CorruptStateIsReportedWithoutPreventingStartup(string content)
    {
        var path = StatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        var store = new PickpocketSessionState(path);
        Assert.Contains("Could not restore", store.Error);
        Assert.Empty(store.Recent);
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void ProgressDistinguishesSurveyReturnDeliveryAndSkippedInput()
    {
        var status = Result(PickpocketVisualState.Active) with { InputMode = "PrecisionAttempt", State = "Tracking", SelectedBandIndex = 0,
            Observation = new(PickpocketVisualState.Active, new Rectangle(0, 0, 576, 23), 250, [new(PickpocketBandColor.Yellow, 238, 242)], 1, "Fixture"),
            Prediction = PickpocketTimingPrediction.Wait("Target center has already passed.", 400) };
        Assert.Contains("first sweep for yellow", PickpocketProgress.Describe(status, true, false));
        Assert.Equal("Waiting for yellow on the return.", PickpocketProgress.Describe(status, true, true));
        Assert.Contains("start a new pickpocket", PickpocketProgress.Describe(status, false, false));
        Assert.Contains("Space sent", PickpocketProgress.Describe(status with { InputDelivery = new("Sent", "Internal detail") }, true, true));
        Assert.Equal("Window changed; shot skipped.", PickpocketProgress.Describe(status with { InputDelivery = new("Skipped", "Window changed; shot skipped.") }, true, true));
        Assert.Contains("cooldown", PickpocketProgress.Describe(status with { State = "Cooldown" }, true, true));
    }
}
