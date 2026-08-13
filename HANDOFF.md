# Handoff — WorkflowLooper — 2026-08-13 CDT

## Current Objective

- Replace the WinForms operator dashboard with a Rift-style Svelte/Tauri shell without changing the .NET capture, detector, input, or safety behavior.

## Current State

- Stable installed fishing build remains 5.0.12 at `%LOCALAPPDATA%\Programs\Workflow Looper\Workflow Looper.exe`; do not replace it with the Tauri build until feature parity and live validation pass.
- The new Tauri shell now has an operator-polished control surface: live state/telemetry, dynamic routine phase, target capture that restores the console after the capture event, stopped-only editable controller settings, and a local diagnostics review drawer.
- The native Tauri layer registers global `Pause / Break`; it sends the bridge `stop`, which releases held input through the existing engine path.
- The prototype installer built at `C:\cargo-targets\release\bundle\nsis\Workflow Looper_0.1.0_x64-setup.exe`. It was launched and visibly rendered the shell; its .NET sidecar stayed alive after the snapshot.

## Recent Relevant Changes

- Added `--ui-bridge` to the existing executable and `UiBridge.cs`: newline JSON over stdin/stdout, allowlisted `snapshot`, `start`, `stop`, `capture_target`, `save_settings`, and `shutdown` commands; no network listener.
- Added `ui/`: Svelte 5/Vite field-console dashboard with target selection, live state/confidence/sample telemetry, start/stop controls, validated stopped-only settings, in-app diagnostics, responsive layout, focus states, and reduced-motion handling.
- Added `ui/src-tauri/`: Tauri 2 shell, packaged .NET resource sidecar, explicit local command bridge, native Pause/Break shortcut, custom window controls, and narrow local diagnostics access (CSV tail + capped latest loss image only).
- Added `ui/scripts/build-engine.ps1` to stage the current .NET release executable into the Tauri resource bundle.

## Known Problems

- The Tauri UI does not yet expose named profiles or a live detector-frame preview.
- The `capture_target` flow still uses a 3.5-second minimize window; it restores automatically when the bridge reports the captured target, but needs live gameplay validation.
- The Tauri installer is a prototype artifact under `C:\cargo-targets`, not an official installed release.

## Next Actions

1. Add named profiles and a live detector-frame preview without widening the bridge permission surface.
2. Add frontend/component tests and bridge protocol tests, then do a daytime and nighttime live-test parity pass.
3. Package as a versioned Tauri installer only after all existing safety/recovery behavior is verified; retain WinForms fallback until then.

## Relevant Files

- `src/Application/UiBridge.cs`, `src/Application/Program.cs`, `src/Application/AppSettings.cs`
- `src/Automation/AdaptiveRoutineEngine.cs`, `src/Automation/FishingMeterDetector.cs`
- `ui/src/App.svelte`, `ui/src/lib/engine.svelte.ts`, `ui/src/app.css`
- `ui/src-tauri/src/lib.rs`, `ui/src-tauri/tauri.conf.json`, `ui/src-tauri/capabilities/default.json`
- `ui/scripts/build-engine.ps1`, `ui/README.md`

## Canonical Commands

- `dotnet test tests\WorkflowLooper.Tests\WorkflowLooper.Tests.csproj -c Release --no-restore`
- `dotnet build WorkflowLooper.sln -c Release --no-restore`
- `& '.\ui\scripts\build-engine.ps1' -Release`
- `npm run check` and `npm run tauri build` from `ui\`
- `cargo check` from `ui\src-tauri\`

## Important Decisions

- The Tauri frontend never accesses FiveM or sends input directly; only the existing .NET engine may do so.
- Communication is a local stdin/stdout sidecar bridge, not HTTP/WebSocket.
- Keep original 35–90 ms pulse cadence unchanged.
- Use the existing Workflow Looper icon and Rift-style frameless-window conventions; do not copy Rift application logic.
