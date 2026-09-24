# CuePilot code map

Use this map to enter the repository by task instead of scanning the whole tree.
See the [documentation index](README.md) for maintained guides and historical
records, [HANDOFF.md](../HANDOFF.md) for the current state of the checkout, and
the [backlog](product-backlog.md) for the open-issue table.

Routes below: [Fishing](#fishing-detection-or-timing) ·
[Lockpicking](#vehicle-lockpicking) · [Engine/UI contract](#engineui-contract) ·
[Pickpocket](#pickpocket-history-timing-and-evidence) ·
[Shortcuts and notifications](#shortcuts-and-desktop-notifications) ·
[Desktop UI](#desktop-ui) · [Updates and releases](#updates-and-releases) ·
[Windows integration](#windows-integration-and-packaging) ·
[Maintenance tools](#repository-maintenance-tools)

For a ten-second orientation pass (branch, version sync, handoff, package, and
working tree), run:

```powershell
pwsh -NoProfile -File .\scripts\project-status.ps1
```

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

1. [src/Automation/AdaptiveRoutineEngine.cs](../src/Automation/AdaptiveRoutineEngine.cs) — Cast → meter → collect orchestration and safety gates.
2. [src/Automation/FishingPromptDetector.cs](../src/Automation/FishingPromptDetector.cs) — Cast and Keep Fish prompt recognition.
3. [src/Automation/FishingMeterDetector.cs](../src/Automation/FishingMeterDetector.cs) — meter identity, tracking, feedback, numeric diagnostics, and `FishingTensionController` (pulse timing, scaled to the measured sample cadence).
4. [src/Diagnostics/FishingDebugSession.cs](../src/Diagnostics/FishingDebugSession.cs) — bounded session evidence, including per-sample `captureMilliseconds`.
5. [tests/CuePilot.Tests/FishingPromptTests.cs](../tests/CuePilot.Tests/FishingPromptTests.cs) and `FishingMeterTests.cs` — regressions and live fixtures.

If taps feel sparse, check sample latency before touching controller tuning:
[src/Capture/DxgiFrameSource.cs](../src/Capture/DxgiFrameSource.cs) (region copy,
GPU priority) and [src/Platform/WindowTargetService.cs](../src/Platform/WindowTargetService.cs)
(cached target resolve) set it. `--capture-probe` measures it.

### Vehicle lockpicking

1. [src/Automation/LockpickingObserverEngine.cs](../src/Automation/LockpickingObserverEngine.cs) — foreground capture loop, lifecycle, evidence, and fail-safe stops.
2. [src/Automation/LockpickingDetector.cs](../src/Automation/LockpickingDetector.cs) — HUD, target, SPIN, OPEN, and disappearance classification.
3. [src/Automation/LockpickingObservationTracker.cs](../src/Automation/LockpickingObservationTracker.cs) — target sequence, ring motion, READY timing, and freshness gates.
4. [src/Automation/LockpickingClassProfiles.cs](../src/Automation/LockpickingClassProfiles.cs) — evidence-backed per-class calibration. Add a class here only after a complete live recording.
5. [src/Automation/LockpickingClassController.cs](../src/Automation/LockpickingClassController.cs) — reusable verified click and clockwise-orbit executor.
6. [src/Automation/LockpickingSpinTracker.cs](../src/Automation/LockpickingSpinTracker.cs) — cursor telemetry used for calibration evidence.
7. [src/Diagnostics/LockpickingDiagnosticSession.cs](../src/Diagnostics/LockpickingDiagnosticSession.cs) — bounded background evidence writer, trace sampling, and cross-session retention.
8. [tests/CuePilot.Tests/LockpickingDetectorTests.cs](../tests/CuePilot.Tests/LockpickingDetectorTests.cs), `LockpickingDiagnosticSessionTests.cs`, and `Fixtures/Lockpicking/` — deterministic controller, persistence, retention, and replay coverage.

### Engine/UI contract

1. [src/Application/UiBridge.cs](../src/Application/UiBridge.cs) — command semantics and protocol snapshot.
2. [ui/src-tauri/src/engine_bridge.rs](../ui/src-tauri/src/engine_bridge.rs) — sidecar lifecycle, response correlation, and shortcut routing.
3. [ui/src-tauri/src/lib.rs](../ui/src-tauri/src/lib.rs) — Tauri command allowlist and local diagnostics commands.
4. [ui/src/lib/engine.svelte.ts](../ui/src/lib/engine.svelte.ts) — typed frontend client and reconnect behavior.
5. [tests/CuePilot.Tests/UiBridgeTests.cs](../tests/CuePilot.Tests/UiBridgeTests.cs), [ui/src/lib/engine.svelte.test.ts](../ui/src/lib/engine.svelte.test.ts), and Rust unit tests — matching contract coverage.

### Pickpocket history, timing, and evidence

For acquisition failures, start with [src/Automation/PickpocketDetector.cs](../src/Automation/PickpocketDetector.cs)
(`Analyze`), `PickpocketProgress.cs`, and
[tests/CuePilot.Tests/PickpocketAcquisitionTests.cs](../tests/CuePilot.Tests/PickpocketAcquisitionTests.cs). The fixtures include an
active bar surrounded by green scenery and a matching no-minigame negative.
Use the [debugging guide](pickpocket-debugging.md) for the replay workflow and
the [evidence catalog](pickpocket-evidence.md) to distinguish recognition from
live timing proof.

1. [src/Automation/PickpocketSessionState.cs](../src/Automation/PickpocketSessionState.cs) — versioned bounded attempt history and cooldown persistence.
2. [src/Automation/PickpocketObserverEngine.cs](../src/Automation/PickpocketObserverEngine.cs) — completion metadata and history query boundary.
3. [src/Diagnostics/PickpocketDiagnosticSession.cs](../src/Diagnostics/PickpocketDiagnosticSession.cs) / `PickpocketReplay.cs` — bounded evidence and pixel replay with explicit timing overrides.
4. [ui/src/lib/activities/PickpocketHistory.svelte](../ui/src/lib/activities/PickpocketHistory.svelte) / `history.ts` — paginated history, selected report and timing labels.
5. [ui/src-tauri/src/support.rs](../ui/src-tauri/src/support.rs) / [ui/src/lib/SupportCenter.svelte](../ui/src/lib/SupportCenter.svelte) — safe session resolution, health, timeline, and text-only export.
6. [tests/CuePilot.Tests/PickpocketSessionStateTests.cs](../tests/CuePilot.Tests/PickpocketSessionStateTests.cs), [ui/src/lib/activities/history.test.ts](../ui/src/lib/activities/history.test.ts), and [ui/e2e/workspaces.spec.ts](../ui/e2e/workspaces.spec.ts) — migration, semantics and interaction checks.

Compare a recorded small-target plan without input while retaining its observed outcome:

```powershell
foreach ($advance in 8, 14, 17, 20) {
  dotnet run --project .\CuePilot.csproj -- --replay-pickpocket <manifest.json> --target-color Yellow --advance-ms $advance
}
# Or save a machine/build/fixture receipt for the comparison:
pwsh -NoProfile -File .\scripts\benchmark-pickpocket.ps1 -Manifest <manifest.json>
```

### Shortcuts and desktop notifications

1. [ui/src/lib/hotkeys.ts](../ui/src/lib/hotkeys.ts) — key naming, the supported-code table, keyboard/mouse capture outcomes, and conflict detection (vitest in `hotkeys.test.ts`).
2. [ui/src/lib/HotkeyCapture.svelte](../ui/src/lib/HotkeyCapture.svelte) — the click-to-capture Settings field; calls `shortcut_capture` so the shell releases its keys while listening.
3. [ui/src-tauri/src/mouse_shortcuts.rs](../ui/src-tauri/src/mouse_shortcuts.rs) — Windows low-level mouse hook for Mouse 4 / Mouse 5 / middle bindings; bound presses are swallowed.
4. [ui/src-tauri/src/engine_bridge.rs](../ui/src-tauri/src/engine_bridge.rs) — keyboard slot registration, mouse routing, capture suspension, and shortcut labels.
5. [ui/src-tauri/src/notifications.rs](../ui/src-tauri/src/notifications.rs) — readiness and shortcut-confirmation popups, preferences, sound, and the click-through notification window pump.
6. [ui/src/lib/NotificationSettings.svelte](../ui/src/lib/NotificationSettings.svelte) and [ui/src/Overlay.svelte](../ui/src/Overlay.svelte) — preferences with previews, and the popup presentation.
7. [src/Application/AppSettings.cs](../src/Application/AppSettings.cs) — engine-side binding validation and display text; [ui/e2e/hotkeys.spec.ts](../ui/e2e/hotkeys.spec.ts) and [ui/e2e/notifications.spec.ts](../ui/e2e/notifications.spec.ts) cover the Settings flows.
8. [ui/src-tauri/src/tray.rs](../ui/src-tauri/src/tray.rs) — notification-area icon (left click / Open restores, Quit stops the engine and exits). Close-to-tray itself is the `CloseRequested` branch of `on_window_event` in [lib.rs](../ui/src-tauri/src/lib.rs), which also queues the one-time `announce_background` popup.

### Desktop UI

1. [ui/src/App.svelte](../ui/src/App.svelte) — shared activity navigation and target/settings/diagnostics toolbar, Fishing workspace, and dialogs with focus restoration.
2. [ui/src/lib/activities.ts](../ui/src/lib/activities.ts) — activity identity, availability, and capability metadata.
3. [ui/src/lib/activities/ActivityPicker.svelte](../ui/src/lib/activities/ActivityPicker.svelte) — launch library.
4. [ui/src/lib/activities/LockpickingWorkspace.svelte](../ui/src/lib/activities/LockpickingWorkspace.svelte) — Lockpicking controls and live telemetry.
5. [ui/src/app.css](../ui/src/app.css) — shared product styling.
6. [.agents/skills/cuepilot-ui/SKILL.md](../.agents/skills/cuepilot-ui/SKILL.md) and `ui/scripts/cdp/` — focus-safe live inspection. The Claude-side copy is the untracked `.claude/skills/cuepilot-ui/SKILL.md`; both drive the same `c.sh`.
7. [ui/e2e/ui-polish.spec.ts](../ui/e2e/ui-polish.spec.ts) and [ui/e2e/workspaces.spec.ts](../ui/e2e/workspaces.spec.ts) — responsive layout, shared controls, live-state wording, save feedback, and keyboard-focus regressions using isolated scenarios.

### Updates and releases

1. [ui/src-tauri/src/update_service.rs](../ui/src-tauri/src/update_service.rs) — Velopack manager, GitHub source, blocking work isolation, progress, and sidecar-safe apply.
2. [ui/src/lib/updates.svelte.ts](../ui/src/lib/updates.svelte.ts) — launch/periodic checks, state machine, download progress, and retry behavior.
3. [ui/src/lib/UpdateCenter.svelte](../ui/src/lib/UpdateCenter.svelte) — version-badge dialog, release notes, active-activity gate, and user confirmation.
4. [scripts/package-velopack.ps1](../scripts/package-velopack.ps1) — version guard, allowlisted staging, WebView2 prerequisite, package/feed/checksum/manifest generation.
5. [.github/workflows/release.yml](../.github/workflows/release.yml) — tag gate, prior delta baseline, publication, and public asset/feed verification.
6. [scripts/test-velopack-update.ps1](../scripts/test-velopack-update.ps1) — disposable installed 5.2.0 → 5.2.1 delta/apply/relaunch/cleanup test under a separate package identity.
7. [ui/src/lib/updates.svelte.test.ts](../ui/src/lib/updates.svelte.test.ts) and Rust tests in `update_service.rs` — focused updater contracts.

### Launch and startup timing

- [ui/src-tauri/src/lib.rs](../ui/src-tauri/src/lib.rs) — `record_startup_phase` and the ordered markers it writes (`velopack_done`, `builder_ready`, `plugins_ready`, `setup_begin`, `notifications_ready`, `setup_end`, `ui_first_command`). Read them from the `startup` lines in `%LOCALAPPDATA%\CuePilot\diagnostics\shell.jsonl`; this is the only launch instrumentation a release build has, since there is no CDP.
- [ui/src-tauri/src/splash.rs](../ui/src-tauri/src/splash.rs) — the Win32/GDI window covering the seconds before WebView2 paints. Shown right after Velopack, hidden on the webview's first command, and bounded by a lifetime timer. It draws no web content on purpose: the browser engine is what the launch is waiting on.
- [ui/src-tauri/tauri.conf.json](../ui/src-tauri/tauri.conf.json) — the main window's `backgroundColor`, which is all that shows behind the splash.

### Windows integration and packaging

- `src/Capture/`, `src/Input/`, and `src/Platform/` — desktop capture, input delivery, target resolution, and Win32 interop.
- [ui/src-tauri/tauri.conf.json](../ui/src-tauri/tauri.conf.json) and `tauri.dev.conf.json` — distinct Release/Dev identities; Tauri's own bundler is disabled because Velopack owns release packaging.
- [ui/scripts/build-engine.ps1](../ui/scripts/build-engine.ps1) — stages the sidecar for Tauri.
- [scripts/build-brand-assets.ps1](../scripts/build-brand-assets.ps1) and [ui/scripts/run-dev-inspectable.ps1](../ui/scripts/run-dev-inspectable.ps1) — icons and the isolated development launcher. Velopack owns installed shortcuts.

## Fast searches

```powershell
rg -n "command-name" src/Application ui/src ui/src-tauri/src
rg -n "Detector|Tracker|Controller" src/Automation tests/CuePilot.Tests
rg -n "LockpickingVisualState|FishingPromptKind|RoutineState" src tests ui/src
rg -n "UpdateService|check_for_updates|updates\.state|package-velopack" ui scripts .github
rg --files src ui/src ui/src-tauri/src tests/CuePilot.Tests
```

Generated schemas, locks, build outputs, dependencies, temporary diagnostics, and CDP screenshots are excluded by [.rgignore](../.rgignore). Search those paths explicitly only when the task concerns packaging or generated state.

### Repository maintenance tools

- [scripts/project-status.ps1](../scripts/project-status.ps1) — local source/installation/package orientation; `-AsJson` provides structured output.
- [scripts/project-version.ps1](../scripts/project-version.ps1) — shared six-file version validation for status, packaging, and the release tag gate.
- [scripts/check-docs.cjs](../scripts/check-docs.cjs) — dependency-free local Markdown link checks.
- `scripts/verify.ps1 -Docs` — documentation and developer-tool regression gate.
- [scripts/write-verification-receipt.ps1](../scripts/write-verification-receipt.ps1) — records commands and the dirty-source fingerprint at verification time.
- [scripts/clean-workspace.ps1](../scripts/clean-workspace.ps1) — previews generated output cleanup; inspect exact targets before authorizing removal.

## Verification

```powershell
pwsh -NoProfile -File .\scripts\verify.ps1 -All
cargo clippy --manifest-path .\ui\src-tauri\Cargo.toml --all-targets -- -D warnings
pwsh -NoProfile -File .\scripts\package-velopack.ps1
pwsh -NoProfile -File .\scripts\test-velopack-update.ps1
```

Preview disposable workspace output without deleting dependency caches:

```powershell
pwsh -NoProfile -File .\scripts\clean-workspace.ps1
```
