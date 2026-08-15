using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CuePilot.Tests;

public sealed class UiBridgeTests
{
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
        incoming.LockpickingStartStop.Key = "F8";
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
        Assert.Equal("F8", saved.LockpickingStartStop.Key);
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
