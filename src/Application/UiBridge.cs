using System.Text.Json;
using System.Text.Json.Serialization;

namespace CuePilot;

// Local stdin/stdout bridge for the Tauri shell. It intentionally exposes a
// tiny, allowlisted command surface rather than a listener on a network port.
internal static class UiBridge
{
    internal const int ProtocolVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly object OutputLock = new();

    internal static int Run() => Run(
        Console.In,
        Console.Out,
        SettingsStore.Load,
        SettingsStore.Save,
        WindowTargetService.FindFiveMTargets);

    internal static int Run(
        TextReader input,
        TextWriter output,
        Func<AppSettings> loadSettings,
        Action<AppSettings> saveSettings,
        Func<IReadOnlyList<WindowTargetService.FiveMWindowTarget>> findFiveMTargets)
    {
        var settings = loadSettings();
        using var routine = new AdaptiveRoutineEngine();
        using var lockpicking = new LockpickingObserverEngine();
        var statusLock = new object();
        var initialTargets = findFiveMTargets();
        var lastStatus = InitialStatus(settings, initialTargets);

        EventHandler<RoutineStatus> statusChanged = (_, status) =>
        {
            lock (statusLock) lastStatus = status;
            Emit(output, "status", StatusPayload(status, routine.DebugSnapshot));
        };
        routine.StatusChanged += statusChanged;
        EventHandler<LockpickingObserveStatus> lockpickingStatusChanged = (_, observeStatus) =>
            Emit(output, "lockpicking_status", observeStatus);
        lockpicking.StatusChanged += lockpickingStatusChanged;
        try
        {
            Emit(output, "ready", Snapshot(settings, ReadStatus(), initialTargets, debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
            string? line;
            while ((line = input.ReadLine()) is not null)
            {
                var id = string.Empty;
                try
                {
                    using var request = JsonDocument.Parse(line);
                    var root = request.RootElement;
                    id = RequiredString(root, "id");
                    var command = RequiredString(root, "command");
                    switch (command)
                    {
                        case "snapshot":
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "start":
                            lockpicking.Stop("Fishing started; lockpicking observation stopped.");
                            var startTargets = findFiveMTargets();
                            EnsureFiveMTargetAvailable(settings.Routine.TargetWindow, startTargets);
                            routine.Arm(settings.Routine);
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), startTargets, debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "toggle":
                            if (routine.State is RoutineState.Stopped or RoutineState.Faulted)
                            {
                                lockpicking.Stop("Fishing started; lockpicking observation stopped.");
                                var toggleTargets = findFiveMTargets();
                                EnsureFiveMTargetAvailable(settings.Routine.TargetWindow, toggleTargets);
                                routine.Arm(settings.Routine);
                                Respond(output, id, true, Snapshot(settings, ReadStatus(), toggleTargets, debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            }
                            else
                            {
                                routine.Stop("Stopped from the global Start / Stop shortcut.");
                                Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            }
                            break;
                        case "toggle_lockpicking_class_c":
                            if (lockpicking.IsObserving)
                            {
                                lockpicking.Stop("Stopped from the Lockpicking Start / Stop shortcut.");
                                Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            }
                            else
                            {
                                EnsureStopped(routine.State, "Class C lockpicking");
                                var shortcutTargets = findFiveMTargets();
                                EnsureFiveMTargetAvailable(settings.Routine.TargetWindow, shortcutTargets);
                                lockpicking.Start(settings.Routine.TargetWindow, LockpickingClassProfiles.ClassC);
                                Respond(output, id, true, Snapshot(settings, ReadStatus(), shortcutTargets, debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            }
                            break;
                        case "stop":
                            routine.Stop("Stopped from the Tauri dashboard.");
                            lockpicking.Stop("Emergency stop released lockpicking input.");
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "start_lockpicking_observe":
                            EnsureStopped(routine.State, "Lockpicking observation");
                            var observeTargets = findFiveMTargets();
                            EnsureFiveMTargetAvailable(settings.Routine.TargetWindow, observeTargets);
                            lockpicking.Start(settings.Routine.TargetWindow);
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), observeTargets, debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "start_lockpicking_class_c":
                            EnsureStopped(routine.State, "Class C lockpicking");
                            var classCTargets = findFiveMTargets();
                            EnsureFiveMTargetAvailable(settings.Routine.TargetWindow, classCTargets);
                            lockpicking.Start(settings.Routine.TargetWindow, LockpickingClassProfiles.ClassC);
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), classCTargets, debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "stop_lockpicking_observe":
                            lockpicking.Stop();
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "list_targets":
                            EnsureStopped(routine.State, "Target selection");
                            EnsureLockpickingStopped(lockpicking, "Target selection");
                            var candidates = findFiveMTargets();
                            lock (statusLock) lastStatus = InitialStatus(settings, candidates);
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), candidates, true, routine.DebugSnapshot, lockpicking.Status));
                            break;
                        case "select_target":
                            EnsureStopped(routine.State, "Target selection");
                            EnsureLockpickingStopped(lockpicking, "Target selection");
                            var processId = RequiredInt32(root, "processId");
                            var availableTargets = findFiveMTargets();
                            var selectedTarget = availableTargets.SingleOrDefault(candidate => candidate.ProcessId == processId)
                                ?? throw new InvalidOperationException("That FiveM window is no longer available. Scan again.");
                            settings.Routine.TargetWindow = selectedTarget.ToSettings();
                            saveSettings(settings);
                            lock (statusLock)
                            {
                                lastStatus = new RoutineStatus(
                                    RoutineState.Stopped,
                                    $"FiveM target ready: {selectedTarget.WindowTitle}.");
                            }
                            var targetSnapshot = Snapshot(settings, ReadStatus(), availableTargets, true, routine.DebugSnapshot, lockpicking.Status);
                            Emit(output, "target", targetSnapshot);
                            Respond(output, id, true, targetSnapshot);
                            break;
                        case "save_settings":
                            EnsureStopped(routine.State, "Settings");
                            EnsureLockpickingStopped(lockpicking, "Settings");
                            if (!root.TryGetProperty("settings", out var settingsValue))
                                throw new InvalidOperationException("The settings payload is required.");
                            var updated = SettingsStore.DeserializeAndMigrateForBridge(settingsValue.GetRawText());
                            updated.SelectedProfile = settings.SelectedProfile;
                            updated.EmergencyStop = settings.EmergencyStop.Copy();
                            updated.Routine.TargetWindow = settings.Routine.TargetWindow.Copy();
                            settings = updated;
                            saveSettings(settings);
                            var settingsSnapshot = Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status);
                            Emit(output, "settings", settingsSnapshot);
                            Respond(output, id, true, settingsSnapshot);
                            break;
                        case "shutdown":
                            lockpicking.Stop("Tauri shell closed.");
                            routine.Stop("Tauri shell closed.");
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            return 0;
                        default:
                            throw new InvalidOperationException($"Unsupported bridge command: {command}.");
                    }
                }
                catch (Exception exception)
                {
                    if (string.IsNullOrWhiteSpace(id))
                        Emit(output, "fault", new { detail = exception.Message });
                    else
                        Respond(output, id, false, null, exception.Message);
                }
            }

            routine.Stop("Tauri bridge input closed.");
            lockpicking.Stop("Tauri bridge input closed.");
            return 0;
        }
        finally
        {
            routine.StatusChanged -= statusChanged;
            lockpicking.StatusChanged -= lockpickingStatusChanged;
        }

        RoutineStatus ReadStatus()
        {
            lock (statusLock) return lastStatus;
        }
    }

    private static RoutineStatus InitialStatus(
        AppSettings settings,
        IReadOnlyList<WindowTargetService.FiveMWindowTarget> availableTargets) => new(
        RoutineState.Stopped,
        TargetAvailable(settings.Routine.TargetWindow, availableTargets)
            ? "FiveM target loaded. Ready when you are."
            : WindowTargetService.IsFiveMTarget(settings.Routine.TargetWindow)
                ? "Saved FiveM target is offline. Start FiveM, then scan again."
            : settings.Routine.TargetWindow.IsConfigured
                ? "The saved target is not FiveM. Select FiveM before starting."
                : "Select FiveM as the target before starting.");

    private static object StatusPayload(RoutineStatus status, FishingDebugSnapshot? debug = null) => new
    {
        state = status.State,
        detail = status.Detail,
        sampleCount = status.SampleCount,
        confidence = status.Confidence,
        debug,
    };

    private static object Snapshot(
        AppSettings settings,
        RoutineStatus status,
        IReadOnlyList<WindowTargetService.FiveMWindowTarget> availableTargets,
        bool includeTargets = false,
        FishingDebugSnapshot? debug = null,
        LockpickingObserveStatus? lockpicking = null)
    {
        var targetValid = TargetAvailable(settings.Routine.TargetWindow, availableTargets);
        var targetValidation = targetValid
            ? "FiveM target ready."
            : WindowTargetService.IsFiveMTarget(settings.Routine.TargetWindow)
                ? "Saved FiveM target is not currently available."
                : settings.Routine.TargetWindow.IsConfigured
                    ? $"{settings.Routine.TargetWindow.ProcessName} is not a valid FiveM target."
                    : "Select FiveM before starting.";

        return new
        {
            protocolVersion = ProtocolVersion,
            engineVersion = typeof(UiBridge).Assembly.GetName().Version?.ToString(3) ?? "unknown",
            routineState = status.State,
            status = StatusPayload(status, debug),
            targetValid,
            canStart = targetValid
                && status.State is RoutineState.Stopped or RoutineState.Faulted,
            targetValidation,
            targets = includeTargets ? availableTargets.Select(candidate => new
            {
                processId = candidate.ProcessId,
                processName = candidate.ProcessName,
                windowTitle = candidate.WindowTitle,
                isForeground = candidate.IsForeground,
                isMinimized = candidate.IsMinimized,
                isSelected = MatchesTarget(settings.Routine.TargetWindow, candidate),
            }).ToArray() : null,
            settings,
            diagnosticsDirectory = AppPaths.DiagnosticsDirectory,
            debug,
            lockpicking = lockpicking ?? LockpickingObserveStatus.Stopped(),
        };
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"The {propertyName} property is required.");
        return value.GetString()!;
    }

    private static int RequiredInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result)
            || result <= 0)
            throw new InvalidOperationException($"The {propertyName} property must be a positive integer.");
        return result;
    }

    private static void EnsureStopped(RoutineState state, string operation)
    {
        if (state is not (RoutineState.Stopped or RoutineState.Faulted))
            throw new InvalidOperationException($"{operation} is only available while automation is stopped.");
    }

    private static void EnsureLockpickingStopped(LockpickingObserverEngine observer, string operation)
    {
        if (observer.IsObserving)
            throw new InvalidOperationException($"{operation} is only available while lockpicking observation is stopped.");
    }

    private static void EnsureFiveMTarget(WindowTargetSettings target)
    {
        if (!WindowTargetService.IsFiveMTarget(target))
            throw new InvalidOperationException("A valid FiveM target has not been selected. Find FiveM and try again.");
    }

    private static void EnsureFiveMTargetAvailable(
        WindowTargetSettings target,
        IReadOnlyList<WindowTargetService.FiveMWindowTarget> availableTargets)
    {
        EnsureFiveMTarget(target);
        if (!TargetAvailable(target, availableTargets))
            throw new InvalidOperationException("The saved FiveM window is not available. Start FiveM, then scan again.");
    }

    private static bool TargetAvailable(
        WindowTargetSettings target,
        IReadOnlyList<WindowTargetService.FiveMWindowTarget> availableTargets) =>
        WindowTargetService.IsFiveMTarget(target)
        && availableTargets.Any(candidate => MatchesTarget(target, candidate));

    private static bool MatchesTarget(
        WindowTargetSettings target,
        WindowTargetService.FiveMWindowTarget candidate)
    {
        if (!target.ProcessName.Equals(candidate.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (target.ProcessId > 0 && target.ProcessId == candidate.ProcessId)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(target.WindowTitle)
            || target.WindowTitle.Equals(candidate.WindowTitle, StringComparison.OrdinalIgnoreCase);
    }

    private static void Respond(TextWriter output, string id, bool ok, object? result, string? error = null) =>
        Write(output, new { type = "response", id, ok, result, error });

    private static void Emit(TextWriter output, string name, object payload) =>
        Write(output, new { type = "event", name, payload });

    private static void Write(TextWriter output, object value)
    {
        lock (OutputLock)
        {
            output.WriteLine(JsonSerializer.Serialize(value, Json));
            output.Flush();
        }
    }
}
