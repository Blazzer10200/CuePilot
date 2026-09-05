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
        WindowTargetService.FindFiveMTargets,
        new PickpocketSessionState(Path.Combine(AppPaths.LocalDataDirectory, "pickpocket-state.json")));

    internal static int Run(
        TextReader input,
        TextWriter output,
        Func<AppSettings> loadSettings,
        Action<AppSettings> saveSettings,
        Func<IReadOnlyList<WindowTargetService.FiveMWindowTarget>> findFiveMTargets,
        PickpocketSessionState? pickpocketState = null)
    {
        var settings = loadSettings();
        using var routine = new AdaptiveRoutineEngine();
        using var lockpicking = new LockpickingObserverEngine();
        using var pickpocket = new PickpocketObserverEngine(sessionState: pickpocketState);
        settings.Pickpocket.Normalize();
        pickpocket.Configure(settings.Pickpocket.TargetPolicy, settings.Pickpocket.InputMode,
            settings.Pickpocket.RedAdvanceMs, settings.Pickpocket.YellowAdvanceMs, settings.Pickpocket.CustomPriority, settings.Pickpocket.ItemPriority);
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
        EventHandler<PickpocketObserveStatus> pickpocketStatusChanged = (_, observeStatus) =>
            Emit(output, "pickpocket_status", observeStatus);
        pickpocket.StatusChanged += pickpocketStatusChanged;
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
                        case "pickpocket_history":
                            var history = root.GetProperty("settings");
                            Respond(output, id, true, pickpocket.History(RequiredNonnegativeInt32(history, "page"), RequiredString(history, "outcome")));
                            break;
                        case "snapshot":
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "verify_setup":
                            EnsureStopped(routine.State, "Setup verification");
                            EnsureLockpickingStopped(lockpicking, "Setup verification");
                            EnsurePickpocketStopped(pickpocket, "Setup verification");
                            using (var setupCapture = FrameSourceFactory.Create())
                            {
                                var verification = FishingSetupVerifier.VerifyAsync(
                                    settings.Routine,
                                    setupCapture,
                                    new TargetInputRouter(settings.Routine.InputMode),
                                    waitForForeground: false,
                                    CancellationToken.None).GetAwaiter().GetResult();
                                Respond(output, id, true, Snapshot(
                                    settings,
                                    ReadStatus(),
                                    findFiveMTargets(),
                                    debug: routine.DebugSnapshot,
                                    lockpicking: lockpicking.Status,
                                    setupVerification: verification));
                            }
                            break;
                        case "start":
                            pickpocket.Stop();
                            EnsurePickpocketStopped(pickpocket, "Fishing");
                            lockpicking.Stop("Fishing started; lockpicking observation stopped.");
                            var startTargets = findFiveMTargets();
                            EnsureFiveMTargetAvailable(settings.Routine.TargetWindow, startTargets);
                            routine.Arm(settings.Routine);
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), startTargets, debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "toggle":
                            if (routine.State is RoutineState.Stopped or RoutineState.Faulted)
                            {
                                pickpocket.Stop();
                                EnsurePickpocketStopped(pickpocket, "Fishing");
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
                            throw new InvalidOperationException("Class C automation is unavailable in this release while concurrent-target label calibration is verified. Observe-only lockpicking remains available.");
                        case "stop":
                            pickpocket.Stop();
                            routine.Stop("Stopped from the Tauri dashboard.");
                            lockpicking.Stop("Emergency stop released lockpicking input.");
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "start_lockpicking_observe":
                            EnsurePickpocketStopped(pickpocket, "Lockpicking observation");
                            EnsureStopped(routine.State, "Lockpicking observation");
                            var observeTargets = findFiveMTargets();
                            EnsureFiveMTargetAvailable(settings.Routine.TargetWindow, observeTargets);
                            lockpicking.Start(settings.Routine.TargetWindow);
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), observeTargets, debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "start_lockpicking_class_c":
                            throw new InvalidOperationException("Class C automation is unavailable in this release while concurrent-target label calibration is verified. Observe-only lockpicking remains available.");
                        case "stop_lockpicking_observe":
                            lockpicking.Stop();
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status));
                            break;
                        case "configure_pickpocket":
                            if (!root.TryGetProperty("settings", out var pickpocketSettings))
                                throw new InvalidOperationException("The settings payload is required.");
                            EnsurePickpocketStopped(pickpocket, "Pickpocket configuration");
                            var preferences = new PickpocketPreferences {
                                TargetPolicy = RequiredString(pickpocketSettings, "targetPolicy"),
                                InputMode = pickpocketSettings.TryGetProperty("inputMode", out _) ? RequiredString(pickpocketSettings, "inputMode") : "Observe",
                                RedAdvanceMs = pickpocketSettings.TryGetProperty("redAdvanceMs", out _) ? RequiredNonnegativeInt32(pickpocketSettings, "redAdvanceMs") : settings.Pickpocket.RedAdvanceMs,
                                YellowAdvanceMs = pickpocketSettings.TryGetProperty("yellowAdvanceMs", out _) ? RequiredNonnegativeInt32(pickpocketSettings, "yellowAdvanceMs") : settings.Pickpocket.YellowAdvanceMs };
                            preferences.CustomPriority = pickpocketSettings.TryGetProperty("customPriority", out var customOrder)
                                ? JsonSerializer.Deserialize<PickpocketBandColor[]>(customOrder.GetRawText(), Json) ?? []
                                : settings.Pickpocket.CustomPriority.ToArray();
                            preferences.ItemPriority = pickpocketSettings.TryGetProperty("itemPriority", out var itemOrder)
                                ? JsonSerializer.Deserialize<string[]>(itemOrder.GetRawText(), Json) ?? []
                                : settings.Pickpocket.ItemPriority.ToArray();
                            preferences.Validate();
                            var savedPreferences = settings.Copy();
                            savedPreferences.Pickpocket = preferences;
                            saveSettings(savedPreferences);
                            settings = savedPreferences;
                            pickpocket.Configure(preferences.TargetPolicy, preferences.InputMode, preferences.RedAdvanceMs, preferences.YellowAdvanceMs, preferences.CustomPriority, preferences.ItemPriority);
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets()));
                            break;
                        case "start_pickpocket_observe":
                        case "toggle_pickpocket_observe":
                            if (command == "toggle_pickpocket_observe" && pickpocket.IsObserving)
                            {
                                pickpocket.Stop("Stopped from the pickpocket shortcut.");
                                Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets()));
                                break;
                            }
                            EnsureStopped(routine.State, "Pickpocket observation");
                            EnsureLockpickingStopped(lockpicking, "Pickpocket observation");
                            var pickpocketTargets = findFiveMTargets();
                            EnsureFiveMTargetAvailable(settings.Routine.TargetWindow, pickpocketTargets);
                            pickpocket.Start(settings.Routine.TargetWindow);
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), pickpocketTargets));
                            break;
                        case "stop_pickpocket_observe":
                            pickpocket.Stop();
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), findFiveMTargets()));
                            break;
                        case "list_targets":
                            EnsurePickpocketStopped(pickpocket, "Target selection");
                            EnsureStopped(routine.State, "Target selection");
                            EnsureLockpickingStopped(lockpicking, "Target selection");
                            var candidates = findFiveMTargets();
                            lock (statusLock) lastStatus = InitialStatus(settings, candidates);
                            Respond(output, id, true, Snapshot(settings, ReadStatus(), candidates, true, routine.DebugSnapshot, lockpicking.Status));
                            break;
                        case "select_target":
                            EnsurePickpocketStopped(pickpocket, "Target selection");
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
                            EnsurePickpocketStopped(pickpocket, "Settings");
                            EnsureStopped(routine.State, "Settings");
                            EnsureLockpickingStopped(lockpicking, "Settings");
                            if (!root.TryGetProperty("settings", out var settingsValue))
                                throw new InvalidOperationException("The settings payload is required.");
                            var proposed = JsonSerializer.Deserialize<AppSettings>(settingsValue.GetRawText(), Json);
                            if (proposed is not null && new[] { (proposed.StartStop, settings.StartStop), (proposed.LockpickingStartStop, settings.LockpickingStartStop), (proposed.PickpocketStartStop, settings.PickpocketStartStop) }
                                .Any(pair => pair.Item1.Key.Equals("F8", StringComparison.OrdinalIgnoreCase) &&
                                    (!pair.Item1.Key.Equals(pair.Item2.Key, StringComparison.OrdinalIgnoreCase) || pair.Item1.Control != pair.Item2.Control || pair.Item1.Shift != pair.Item2.Shift || pair.Item1.Alt != pair.Item2.Alt)))
                                throw new InvalidOperationException("F8 is reserved for the FiveM console. Choose another shortcut.");
                            if (settingsValue.TryGetProperty("pickpocketStartStop", out _) && (proposed is null || !SettingsStore.IsValid(proposed)))
                                throw new InvalidOperationException("Choose valid, different shortcuts for each activity and emergency stop.");
                            var updated = SettingsStore.DeserializeAndMigrateForBridge(settingsValue.GetRawText());
                            updated.SelectedProfile = settings.SelectedProfile;
                            updated.Pickpocket = settings.Pickpocket.Copy();
                            updated.EmergencyStop = settings.EmergencyStop.Copy();
                            updated.Routine.TargetWindow = settings.Routine.TargetWindow.Copy();
                            settings = updated;
                            saveSettings(settings);
                            var settingsSnapshot = Snapshot(settings, ReadStatus(), findFiveMTargets(), debug: routine.DebugSnapshot, lockpicking: lockpicking.Status);
                            Emit(output, "settings", settingsSnapshot);
                            Respond(output, id, true, settingsSnapshot);
                            break;
                        case "shutdown":
                            pickpocket.Stop("Tauri shell closed.");
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
            pickpocket.Stop("Tauri bridge input closed.");
            lockpicking.Stop("Tauri bridge input closed.");
            return 0;
        }
        finally
        {
            routine.StatusChanged -= statusChanged;
            lockpicking.StatusChanged -= lockpickingStatusChanged;
            pickpocket.StatusChanged -= pickpocketStatusChanged;
        }

        RoutineStatus ReadStatus()
        {
            lock (statusLock) return lastStatus;
        }
        LockpickingObserveStatus ReadLockpicking() => lockpicking.Status;

        object Snapshot(AppSettings currentSettings, RoutineStatus currentStatus,
            IReadOnlyList<WindowTargetService.FiveMWindowTarget> targets, bool includeTargets = false,
            FishingDebugSnapshot? debug = null, LockpickingObserveStatus? lockpicking = null,
            FishingSetupVerification? setupVerification = null) => CreateSnapshot(currentSettings, currentStatus,
                targets, includeTargets, debug ?? routine.DebugSnapshot, lockpicking ?? ReadLockpicking(), setupVerification, pickpocket.Status);
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

    private static object CreateSnapshot(
        AppSettings settings,
        RoutineStatus status,
        IReadOnlyList<WindowTargetService.FiveMWindowTarget> availableTargets,
        bool includeTargets = false,
        FishingDebugSnapshot? debug = null,
        LockpickingObserveStatus? lockpicking = null,
        FishingSetupVerification? setupVerification = null,
        PickpocketObserveStatus? pickpocket = null)
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
                && pickpocket?.Observing != true && lockpicking?.Observing != true
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
            pickpocket = pickpocket ?? PickpocketObserveStatus.Stopped,
            setupVerification,
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

    private static int RequiredNonnegativeInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result) || result < 0)
            throw new InvalidOperationException($"The {propertyName} property must be a nonnegative integer.");
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

    private static void EnsurePickpocketStopped(PickpocketObserverEngine observer, string operation)
    {
        if (observer.IsObserving)
            throw new InvalidOperationException($"{operation} is only available while pickpocket observation is stopped.");
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
