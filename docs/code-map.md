# CuePilot code map

Use this map to enter the repository by task instead of scanning the whole tree.
See the [documentation index](README.md) for maintained guides and historical records.

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
3. [src/Automation/FishingMeterDetector.cs](../src/Automation/FishingMeterDetector.cs) — meter identity, tracking, feedback, and numeric diagnostics.
4. [src/Diagnostics/FishingDebugSession.cs](../src/Diagnostics/FishingDebugSession.cs) — bounded session evidence.
5. [tests/CuePilot.Tests/FishingPromptTests.cs](../tests/CuePilot.Tests/FishingPromptTests.cs) and `FishingMeterTests.cs` — regressions and live fixtures.

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

### Desktop UI

1. [ui/src/App.svelte](../ui/src/App.svelte) — shared activity navigation and target/settings/diagnostics toolbar, Fishing workspace, and dialogs with focus restoration.
2. [ui/src/lib/activities.ts](../ui/src/lib/activities.ts) — activity identity, availability, and capability metadata.
3. [ui/src/lib/activities/ActivityPicker.svelte](../ui/src/lib/activities/ActivityPicker.svelte) — launch library.
4. [ui/src/lib/activities/LockpickingWorkspace.svelte](../ui/src/lib/activities/LockpickingWorkspace.svelte) — Lockpicking controls and live telemetry.
5. [ui/src/app.css](../ui/src/app.css) — shared product styling.
6. [.agents/skills/cuepilot-ui/SKILL.md](../.agents/skills/cuepilot-ui/SKILL.md) and `ui/scripts/cdp/` — focus-safe live inspection.
7. [ui/e2e/ui-polish.spec.ts](../ui/e2e/ui-polish.spec.ts) and [ui/e2e/workspaces.spec.ts](../ui/e2e/workspaces.spec.ts) — responsive layout, shared controls, live-state wording, save feedback, and keyboard-focus regressions using isolated scenarios.

### Updates and releases

1. [ui/src-tauri/src/update_service.rs](../ui/src-tauri/src/update_service.rs) — Velopack manager, GitHub source, blocking work isolation, progress, and sidecar-safe apply.
2. [ui/src/lib/updates.svelte.ts](../ui/src/lib/updates.svelte.ts) — launch/periodic checks, state machine, download progress, and retry behavior.
3. [ui/src/lib/UpdateCenter.svelte](../ui/src/lib/UpdateCenter.svelte) — version-badge dialog, release notes, active-activity gate, and user confirmation.
4. [scripts/package-velopack.ps1](../scripts/package-velopack.ps1) — version guard, allowlisted staging, WebView2 prerequisite, package/feed/checksum/manifest generation.
5. [.github/workflows/release.yml](../.github/workflows/release.yml) — tag gate, prior delta baseline, publication, and public asset/feed verification.
6. [scripts/test-velopack-update.ps1](../scripts/test-velopack-update.ps1) — disposable installed 5.2.0 → 5.2.1 delta/apply/relaunch/cleanup test under a separate package identity.
7. [ui/src/lib/updates.svelte.test.ts](../ui/src/lib/updates.svelte.test.ts) and Rust tests in `update_service.rs` — focused updater contracts.

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
