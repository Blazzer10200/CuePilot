# CuePilot code map

Use this map to enter the repository by task instead of scanning the whole tree.

## Runtime boundaries

```text
Svelte workspace
  -> Tauri command allowlist
    -> Rust sidecar bridge
      -> .NET UiBridge
        -> capture / detection / controller / input
```

The .NET engine is authoritative for capture, detection, timing, input, and safety. Svelte renders state and requests allowlisted commands. Rust owns the local sidecar process and newline-JSON transport.

## Task routes

### Fishing detection or timing

1. `src/Automation/AdaptiveRoutineEngine.cs` — Cast → meter → collect orchestration and safety gates.
2. `src/Automation/FishingPromptDetector.cs` — Cast and Keep Fish prompt recognition.
3. `src/Automation/FishingMeterDetector.cs` — meter identity, tracking, feedback, and numeric diagnostics.
4. `src/Diagnostics/FishingDebugSession.cs` — bounded session evidence.
5. `tests/CuePilot.Tests/FishingPromptTests.cs` and `FishingMeterTests.cs` — regressions and live fixtures.

### Vehicle lockpicking

1. `src/Automation/LockpickingObserverEngine.cs` — foreground capture loop, lifecycle, evidence, and fail-safe stops.
2. `src/Automation/LockpickingDetector.cs` — HUD, target, SPIN, OPEN, and disappearance classification.
3. `src/Automation/LockpickingObservationTracker.cs` — target sequence, ring motion, READY timing, and freshness gates.
4. `src/Automation/LockpickingClassProfiles.cs` — evidence-backed per-class calibration. Add a class here only after a complete live recording.
5. `src/Automation/LockpickingClassController.cs` — reusable verified click and clockwise-orbit executor.
6. `src/Automation/LockpickingSpinTracker.cs` — cursor telemetry used for calibration evidence.
7. `tests/CuePilot.Tests/LockpickingDetectorTests.cs` and `Fixtures/Lockpicking/` — deterministic replay coverage.

### Engine/UI contract

1. `src/Application/UiBridge.cs` — command semantics and protocol snapshot.
2. `ui/src-tauri/src/engine_bridge.rs` — sidecar lifecycle, response correlation, and shortcut routing.
3. `ui/src-tauri/src/lib.rs` — Tauri command allowlist and local diagnostics commands.
4. `ui/src/lib/engine.svelte.ts` — typed frontend client and reconnect behavior.
5. `tests/CuePilot.Tests/UiBridgeTests.cs`, `ui/src/lib/engine.svelte.test.ts`, and Rust unit tests — matching contract coverage.

### Desktop UI

1. `ui/src/App.svelte` — shell, Fishing workspace, settings, and diagnostics drawer.
2. `ui/src/lib/activities.ts` — activity identity, availability, and capability metadata.
3. `ui/src/lib/activities/ActivityPicker.svelte` — launch library.
4. `ui/src/lib/activities/LockpickingWorkspace.svelte` — Lockpicking controls and live telemetry.
5. `ui/src/app.css` — shared product styling.
6. `.agents/skills/cuepilot-ui/SKILL.md` and `ui/scripts/cdp/` — focus-safe live inspection.

### Windows integration and packaging

- `src/Capture/`, `src/Input/`, and `src/Platform/` — desktop capture, input delivery, target resolution, and Win32 interop.
- `ui/src-tauri/tauri.conf.json` and `tauri.dev.conf.json` — distinct Release/Dev identities.
- `ui/scripts/build-engine.ps1` — stages the sidecar for Tauri.
- `scripts/build-brand-assets.ps1` and `install-desktop-shortcuts.ps1` — icons and launchers.

## Fast searches

```powershell
rg -n "command-name" src/Application ui/src ui/src-tauri/src
rg -n "Detector|Tracker|Controller" src/Automation tests/CuePilot.Tests
rg -n "LockpickingVisualState|FishingPromptKind|RoutineState" src tests ui/src
rg --files src ui/src ui/src-tauri/src tests/CuePilot.Tests
```

Generated schemas, locks, build outputs, dependencies, temporary diagnostics, and CDP screenshots are excluded by `.rgignore`. Search those paths explicitly only when the task concerns packaging or generated state.

## Verification

```powershell
pwsh -NoProfile -File .\scripts\verify.ps1 -All
```

Preview disposable workspace output without deleting dependency caches:

```powershell
pwsh -NoProfile -File .\scripts\clean-workspace.ps1
```
