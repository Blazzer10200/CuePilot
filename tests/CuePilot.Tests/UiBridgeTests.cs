using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CuePilot.Tests;

public sealed class UiBridgeTests
{
    [Fact]
    public void SharedBridgeFixtureMatchesProtocolAndRequiredSnapshotFields()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "contracts", "bridge-response-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = document.RootElement.GetProperty("result");
        Assert.Equal(UiBridge.ProtocolVersion, result.GetProperty("protocolVersion").GetInt32());
        foreach (var property in new[] { "engineVersion", "routineState", "targetValid", "canStart", "targetValidation", "targets", "diagnosticsDirectory", "setupVerification" })
            Assert.True(result.TryGetProperty(property, out _), $"Missing shared fixture property: {property}");
    }
    [Fact]
    public void PickpocketShortcutIsSavedAndDuplicatesOrFiveMConsoleAreRejected()
    {
        foreach (var key in new[] { "F6", "F10", "F8" })
        {
            var settings = AppSettings.Defaults();
            var incoming = settings.Copy();
            incoming.PickpocketStartStop.Key = key;
            AppSettings? saved = null;
            var messages = RunBridge(settings, JsonSerializer.Serialize(new { id = "binding", command = "save_settings", settings = incoming }, Json), value => saved = value.Copy());
            Assert.Equal(key == "F6", FindResponse(messages, "binding").GetProperty("ok").GetBoolean());
            if (key == "F6") Assert.Equal("F6", saved!.PickpocketStartStop.Key);
            else Assert.Null(saved);
        }
        var result = FindResponse(RunBridge(AppSettings.Defaults(), """
            {"id":"toggle","command":"toggle_pickpocket_observe"}
            """), "toggle");
        Assert.False(result.GetProperty("ok").GetBoolean());
        Assert.Contains("FiveM", result.GetProperty("error").GetString());
    }

    [Fact]
    public void AddingPickpocketBindingPreservesExistingShortcutsAndSettings()
    {
        var restored = SettingsStore.DeserializeAndMigrateForTest("""
            {"formatVersion":9,"startStop":{"key":"F7"},"lockpickingStartStop":{"key":"F9"},"routine":{"fishingLowerTensionPercent":60}}
            """);
        Assert.Equal("F7", restored.StartStop.Key);
        Assert.Equal("F6", restored.PickpocketStartStop.Key);
        Assert.Equal(60, restored.Routine.FishingLowerTensionPercent);
        Assert.Equal("F6", restored.Copy().PickpocketStartStop.Key);
        Assert.Equal("F6", SettingsStore.RoundTripForTest(restored).PickpocketStartStop.Key);
    }
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void SnapshotReturnsCorrelatedAuthoritativeState()
    {
        var settings = AppSettings.Defaults();
        settings.Routine.TargetWindow = new WindowTargetSettings
        {
            ProcessName = "FiveM_b3258_GTAProcess",
            WindowTitle = "FiveM",
        };

        var messages = RunBridge(
            settings,
            """
            {"id":"snapshot-1","command":"snapshot"}
            """,
            findTargets: () => [Candidate(3258, "FiveM")]);

        var response = FindResponse(messages, "snapshot-1");
        Assert.True(response.GetProperty("ok").GetBoolean());
        var result = response.GetProperty("result");
        Assert.Equal(UiBridge.ProtocolVersion, result.GetProperty("protocolVersion").GetInt32());
        Assert.True(result.GetProperty("canStart").GetBoolean());
        Assert.Equal("Stopped", result.GetProperty("status").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("debug").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("status").GetProperty("debug").ValueKind);
        Assert.Contains("FiveM", result.GetProperty("status").GetProperty("detail").GetString());
        Assert.False(result.GetProperty("pickpocket").GetProperty("observing").GetBoolean());
    }

    [Fact]
    public void PickpocketPolicyIsValidatedAndStartRequiresAvailableTarget()
    {
        var messages = RunBridge(AppSettings.Defaults(), """
            {"id":"policy","command":"configure_pickpocket","settings":{"targetPolicy":"Widest"}}
            {"id":"invalid","command":"configure_pickpocket","settings":{"targetPolicy":"Unknown item"}}
            {"id":"start","command":"start_pickpocket_observe"}
            {"id":"stop","command":"stop_pickpocket_observe"}
            """);
        Assert.True(FindResponse(messages, "policy").GetProperty("ok").GetBoolean());
        Assert.Equal("Widest", FindResponse(messages, "policy").GetProperty("result").GetProperty("pickpocket").GetProperty("targetPolicy").GetString());
        Assert.False(FindResponse(messages, "invalid").GetProperty("ok").GetBoolean());
        Assert.False(FindResponse(messages, "start").GetProperty("ok").GetBoolean());
        Assert.True(FindResponse(messages, "stop").GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData("Yellow")]
    [InlineData("RarestFirst")]
    [InlineData("Custom")]
    public void PickpocketPreferencesPersistWithoutArmingAndInvalidChangesDoNotSave(string policy)
    {
        AppSettings? saved = null;
        var writes = 0;
        var messages = RunBridge(AppSettings.Defaults(), """
            {"id":"configure","command":"configure_pickpocket","settings":{"targetPolicy":"Yellow","inputMode":"PrecisionAttempt","redAdvanceMs":8,"yellowAdvanceMs":12}}
            {"id":"bad","command":"configure_pickpocket","settings":{"targetPolicy":"Red","inputMode":"PrecisionAttempt","yellowAdvanceMs":21}}
            """.Replace("\"Yellow\"", $"\"{policy}\""), save: value => { saved = value.Copy(); writes++; });
        Assert.True(FindResponse(messages, "configure").GetProperty("ok").GetBoolean());
        Assert.False(FindResponse(messages, "bad").GetProperty("ok").GetBoolean());
        Assert.Equal(1, writes);
        Assert.Equal(8, saved!.Pickpocket.RedAdvanceMs);
        Assert.Equal(12, saved.Pickpocket.YellowAdvanceMs);
        var restored = SettingsStore.RoundTripForTest(saved);
        var restart = RunBridge(restored, """{"id":"read","command":"snapshot"}""");
        var status = FindResponse(restart, "read").GetProperty("result").GetProperty("pickpocket");
        Assert.Equal(policy, status.GetProperty("targetPolicy").GetString());
        Assert.Equal("PrecisionAttempt", status.GetProperty("inputMode").GetString());
        Assert.Equal(12, status.GetProperty("yellowAdvanceMs").GetInt32());
        Assert.False(status.GetProperty("inputArmed").GetBoolean());
        Assert.False(status.GetProperty("observing").GetBoolean());
        restored.Copy().Pickpocket.RedAdvanceMs = 0;
        Assert.Equal(8, restored.Pickpocket.RedAdvanceMs);
    }

    [Fact]
    public void CustomOrderRoundTripsAndRejectsDuplicatesWithoutSaving()
    {
        AppSettings? saved = null;
        var writes = 0;
        var messages = RunBridge(AppSettings.Defaults(), """
            {"id":"order","command":"configure_pickpocket","settings":{"targetPolicy":"Custom","inputMode":"PrecisionAttempt","customPriority":["Purple","Yellow","PaleGreen"]}}
            {"id":"duplicate","command":"configure_pickpocket","settings":{"targetPolicy":"Custom","customPriority":["Yellow","Yellow"]}}
            {"id":"empty","command":"configure_pickpocket","settings":{"targetPolicy":"Custom","customPriority":[]}}
            """, save: value => { saved = value.Copy(); writes++; });
        Assert.True(FindResponse(messages, "order").GetProperty("ok").GetBoolean());
        Assert.False(FindResponse(messages, "duplicate").GetProperty("ok").GetBoolean());
        Assert.False(FindResponse(messages, "empty").GetProperty("ok").GetBoolean());
        Assert.Equal(1, writes);
        var restored = SettingsStore.RoundTripForTest(saved!);
        Assert.Equal(new[] { PickpocketBandColor.Purple, PickpocketBandColor.Yellow, PickpocketBandColor.PaleGreen }, restored.Pickpocket.CustomPriority);
        var status = FindResponse(RunBridge(restored, """{"id":"read","command":"snapshot"}"""), "read").GetProperty("result").GetProperty("pickpocket");
        Assert.Equal(new[] { "Purple", "Yellow", "PaleGreen" }, status.GetProperty("customPriority").EnumerateArray().Select(e => e.GetString()));
        Assert.False(status.GetProperty("inputArmed").GetBoolean());
        restored.Copy().Pickpocket.CustomPriority[0] = PickpocketBandColor.Red;
        Assert.Equal(PickpocketBandColor.Purple, restored.Pickpocket.CustomPriority[0]);
    }

    [Fact]
    public void SameColorItemOrderPersistsAndRejectsDuplicates()
    {
        var order = new PickpocketPreferences().ItemPriority;
        (order[3], order[4]) = (order[4], order[3]);
        var command = JsonSerializer.Serialize(new { id = "items", command = "configure_pickpocket", settings = new { targetPolicy = "RarestFirst", inputMode = "PrecisionAttempt", itemPriority = order } });
        AppSettings? saved = null;
        var response = FindResponse(RunBridge(AppSettings.Defaults(), command, save: value => saved = value.Copy()), "items");
        Assert.True(response.GetProperty("ok").GetBoolean());
        var restored = SettingsStore.RoundTripForTest(saved!);
        Assert.Equal("Ring", restored.Pickpocket.ItemPriority[3]);
        var status = FindResponse(RunBridge(restored, """{"id":"read","command":"snapshot"}"""), "read").GetProperty("result").GetProperty("pickpocket");
        Assert.Equal(order, status.GetProperty("itemPriority").EnumerateArray().Select(e => e.GetString()));
        var duplicate = RunBridge(restored, """{"id":"bad","command":"configure_pickpocket","settings":{"targetPolicy":"RarestFirst","itemPriority":["Ring","Ring"]}}""", save: _ => throw new Exception("Invalid order must not be saved"));
        Assert.False(FindResponse(duplicate, "bad").GetProperty("ok").GetBoolean());
        restored.Copy().Pickpocket.ItemPriority[3] = "Ruby";
        Assert.Equal("Ring", restored.Pickpocket.ItemPriority[3]);
    }

    [Fact]
    public void LegacyPickpocketSettingsDefaultSafelyAndInvalidSavedValuesNormalize()
    {
        var legacy = SettingsStore.DeserializeAndMigrateForTest("""{"formatVersion":9}""");
        Assert.Equal("Observe", legacy.Pickpocket.InputMode);
        Assert.Equal("RarestFirst", legacy.Pickpocket.TargetPolicy);
        Assert.Equal(20, legacy.Pickpocket.YellowAdvanceMs);
        var bad = SettingsStore.DeserializeAndMigrateForTest("""{"formatVersion":9,"pickpocket":{"targetPolicy":"Nope","inputMode":"Unlimited","redAdvanceMs":-1,"yellowAdvanceMs":21}}""");
        Assert.Equal("RarestFirst", bad.Pickpocket.TargetPolicy);
        Assert.Equal("Observe", bad.Pickpocket.InputMode);
        Assert.Equal(8, bad.Pickpocket.RedAdvanceMs);
        Assert.Equal(20, bad.Pickpocket.YellowAdvanceMs);
    }

    [Fact]
    public void PickpocketInputRequiresExplicitValidModeAndDefaultsBackToObserve()
    {
        var messages = RunBridge(AppSettings.Defaults(), """
            {"id":"armed","command":"configure_pickpocket","settings":{"targetPolicy":"Widest","inputMode":"SingleAttempt"}}
            {"id":"precision","command":"configure_pickpocket","settings":{"targetPolicy":"PurpleBlueWhite","inputMode":"PrecisionAttempt"}}
            {"id":"bad","command":"configure_pickpocket","settings":{"targetPolicy":"Widest","inputMode":"Unlimited"}}
            {"id":"observe","command":"configure_pickpocket","settings":{"targetPolicy":"Widest"}}
            """);
        var armed = FindResponse(messages, "armed").GetProperty("result").GetProperty("pickpocket");
        Assert.Equal("SingleAttempt", armed.GetProperty("inputMode").GetString());
        Assert.False(armed.GetProperty("inputArmed").GetBoolean());
        var precision = FindResponse(messages, "precision").GetProperty("result").GetProperty("pickpocket");
        Assert.Equal("PrecisionAttempt", precision.GetProperty("inputMode").GetString());
        Assert.Equal("PurpleBlueWhite", precision.GetProperty("targetPolicy").GetString());
        Assert.False(precision.GetProperty("inputArmed").GetBoolean());
        Assert.False(FindResponse(messages, "bad").GetProperty("ok").GetBoolean());
        Assert.Equal("Observe", FindResponse(messages, "observe").GetProperty("result").GetProperty("pickpocket").GetProperty("inputMode").GetString());
    }

    [Fact]
    public void SnapshotDisablesStartWhenTheSavedFiveMWindowIsOffline()
    {
        var settings = AppSettings.Defaults();
        settings.Routine.TargetWindow = new WindowTargetSettings
        {
            ProcessId = 3258,
            ProcessName = "FiveM_b3258_GTAProcess",
            WindowTitle = "FiveM",
        };

        var messages = RunBridge(settings, """
            {"id":"snapshot-1","command":"snapshot"}
            """);

        var result = FindResponse(messages, "snapshot-1").GetProperty("result");
        Assert.False(result.GetProperty("targetValid").GetBoolean());
        Assert.False(result.GetProperty("canStart").GetBoolean());
        Assert.Contains("not currently available", result.GetProperty("targetValidation").GetString());
        Assert.Contains("offline", result.GetProperty("status").GetProperty("detail").GetString());
    }

    [Fact]
    public void CommandFailureReturnsErrorForTheSameRequest()
    {
        var messages = RunBridge(AppSettings.Defaults(), """
            {"id":"bad-1","command":"not_allowed"}
            """);

        var response = FindResponse(messages, "bad-1");
        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Contains("Unsupported bridge command", response.GetProperty("error").GetString());
    }

    [Fact]
    public void SetupVerificationReportsAnUnconfiguredTargetWithoutSendingInput()
    {
        var messages = RunBridge(AppSettings.Defaults(), """
            {"id":"setup-1","command":"verify_setup"}
            """);

        var response = FindResponse(messages, "setup-1");
        Assert.True(response.GetProperty("ok").GetBoolean());
        var verification = response.GetProperty("result").GetProperty("setupVerification");
        Assert.False(verification.GetProperty("ready").GetBoolean());
        Assert.False(verification.GetProperty("target").GetProperty("passed").GetBoolean());
        Assert.False(verification.GetProperty("input").GetProperty("passed").GetBoolean());
        Assert.False(verification.GetProperty("capture").GetProperty("passed").GetBoolean());
    }

    [Fact]
    public void TargetDiscoveryReturnsOnlyBackendValidatedCandidates()
    {
        var settings = AppSettings.Defaults();
        var saveCount = 0;
        var messages = RunBridge(
            settings,
            """
            {"id":"targets-1","command":"list_targets"}
            """,
            _ => saveCount++,
            () => [Candidate(3258, "FiveM® by Cfx.re")]);

        var response = FindResponse(messages, "targets-1");
        Assert.True(response.GetProperty("ok").GetBoolean());
        var target = Assert.Single(response.GetProperty("result").GetProperty("targets").EnumerateArray());
        Assert.Equal(3258, target.GetProperty("processId").GetInt32());
        Assert.Equal("FiveM® by Cfx.re", target.GetProperty("windowTitle").GetString());
        Assert.False(target.GetProperty("isSelected").GetBoolean());
        Assert.Equal(0, saveCount);
        Assert.False(settings.Routine.TargetWindow.IsConfigured);
    }

    [Fact]
    public void TargetSelectionSavesTheRevalidatedProcess()
    {
        var settings = AppSettings.Defaults();
        AppSettings? saved = null;
        var messages = RunBridge(
            settings,
            """
            {"id":"target-1","command":"select_target","processId":3258}
            """,
            value => saved = value.Copy(),
            () => [Candidate(3258, "FiveM® by Cfx.re")]);

        var response = FindResponse(messages, "target-1");
        Assert.True(response.GetProperty("ok").GetBoolean());
        Assert.NotNull(saved);
        Assert.Equal(3258, saved.Routine.TargetWindow.ProcessId);
        Assert.Equal("FiveM_b3258_GTAProcess", saved.Routine.TargetWindow.ProcessName);
        Assert.True(response.GetProperty("result").GetProperty("canStart").GetBoolean());
        var target = Assert.Single(response.GetProperty("result").GetProperty("targets").EnumerateArray());
        Assert.True(target.GetProperty("isSelected").GetBoolean());
    }

    [Fact]
    public void TargetSelectionRejectsAStaleProcessWithoutSaving()
    {
        var settings = AppSettings.Defaults();
        var saveCount = 0;
        var messages = RunBridge(
            settings,
            """
            {"id":"target-1","command":"select_target","processId":3258}
            """,
            _ => saveCount++,
            () => [Candidate(9001, "FiveM")]);

        var response = FindResponse(messages, "target-1");
        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Contains("no longer available", response.GetProperty("error").GetString());
        Assert.Equal(0, saveCount);
        Assert.False(settings.Routine.TargetWindow.IsConfigured);
    }

    [Fact]
    public void SettingsCommandUpdatesActivityShortcutsButCannotReplaceTargetOrEmergencyShortcut()
    {
        var settings = AppSettings.Defaults();
        settings.Routine.TargetWindow = new WindowTargetSettings { ProcessName = "FiveM", WindowTitle = "FiveM" };
        AppSettings? saved = null;
        var incoming = settings.Copy();
        incoming.Routine.FishingLowerTensionPercent = 60;
        incoming.Routine.TargetWindow = new WindowTargetSettings { ProcessName = "ChatGPT", WindowTitle = "ChatGPT" };
        incoming.StartStop.Key = "F11";
        incoming.LockpickingStartStop.Key = "F6";
        incoming.EmergencyStop.Key = "F12";
        var request = JsonSerializer.Serialize(new
        {
            id = "settings-1",
            command = "save_settings",
            settings = incoming,
        }, Json);

        var messages = RunBridge(settings, request, value => saved = value.Copy());

        Assert.True(FindResponse(messages, "settings-1").GetProperty("ok").GetBoolean());
        Assert.NotNull(saved);
        Assert.Equal(60, saved.Routine.FishingLowerTensionPercent);
        Assert.Equal("FiveM", saved.Routine.TargetWindow.ProcessName);
        Assert.Equal("F11", saved.StartStop.Key);
        Assert.Equal("F6", saved.LockpickingStartStop.Key);
        Assert.Equal("Pause", saved.EmergencyStop.Key);
    }

    [Fact]
    public void LockpickingShortcutRejectsUnavailableClassCAutomation()
    {
        var settings = AppSettings.Defaults();
        settings.Routine.TargetWindow = new WindowTargetSettings
        {
            ProcessId = 3258,
            ProcessName = "FiveM_b3258_GTAProcess",
            WindowTitle = "FiveM",
        };

        var messages = RunBridge(
            settings,
            """
            {"id":"lockpicking-toggle-1","command":"toggle_lockpicking_class_c"}
            """,
            findTargets: () => [Candidate(3258, "FiveM")]);

        var response = FindResponse(messages, "lockpicking-toggle-1");
        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Contains("Class C automation is unavailable", response.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("FiveM")]
    [InlineData("FiveM_b3258_GTAProcess")]
    [InlineData("fivem_gameprocess")]
    public void FiveMTargetValidationAcceptsFiveMProcesses(string processName)
    {
        Assert.True(WindowTargetService.IsFiveMTarget(new WindowTargetSettings { ProcessName = processName }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ChatGPT")]
    [InlineData("NotFiveM")]
    public void FiveMTargetValidationRejectsOtherProcesses(string processName)
    {
        Assert.False(WindowTargetService.IsFiveMTarget(new WindowTargetSettings { ProcessName = processName }));
    }

    private static IReadOnlyList<JsonElement> RunBridge(
        AppSettings settings,
        string request,
        Action<AppSettings>? save = null,
        Func<IReadOnlyList<WindowTargetService.FiveMWindowTarget>>? findTargets = null)
    {
        using var input = new StringReader(request);
        using var output = new StringWriter();
        UiBridge.Run(
            input,
            output,
            () => settings,
            save ?? (_ => { }),
            findTargets ?? (() => []));

        return output.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
    }

    private static JsonElement FindResponse(IEnumerable<JsonElement> messages, string id) =>
        messages.Single(message =>
            message.GetProperty("type").GetString() == "response"
            && message.GetProperty("id").GetString() == id);

    private static WindowTargetService.FiveMWindowTarget Candidate(int processId, string title) => new(
        processId,
        "FiveM_b3258_GTAProcess",
        title,
        new Rectangle(100, 100, 1280, 720),
        false,
        false);
}
