# Handoff — CuePilot — 2026-08-15 01:13 CDT

## Current Objective

- Hold the published CuePilot 5.1.0 release stable while preparing an unpublished compatibility update locally.

## Current State

- CuePilot is now a Svelte/Tauri desktop UI with a packaged headless .NET engine sidecar; the prior WinForms presentation layer and WorkflowLooper project names are being replaced.
- Fishing is the ready activity. Prompt and meter discovery now share a centered 16:9 safe viewport so the recorded behavior is preserved on standard displays and translated correctly on ultrawide and super-ultrawide displays.
- Fishing requires FiveM to be foreground. The unsuccessful experimental covered-window/background-input path and compatibility UI were removed; saved `Application` mode settings migrate to `Automatic` without losing the selected target.
- Vehicle Lockpicking has an input-free Observe mode and an explicitly armed `Run Class C` mode. Classes A, B, and D remain unavailable.
- Class C uses live session `20260814-133216`: one click per verified READY target, two matching SPIN frames, then a clockwise 1,140°/s orbit at 0.61× HUD radius for at most 2.8 seconds.
- Class C has an independent configurable Start / Stop shortcut defaulting to `F9`; Fishing remains `F10` and Pause / Break remains the emergency stop.
- Class C HUD discovery now preserves the recorded 16:9 fast path and adds a centered, height-scaled safe-viewport search for 16:10, 5:4/windowed-style, 21:9, and 32:9 layouts. Cursor delivery remains normalized to the resolved FiveM window and virtual desktop.
- The headless engine is explicitly Per-Monitor-V2 DPI aware so capture bounds and physical cursor coordinates remain in the same coordinate space across Windows display scaling and mixed-monitor layouts.
- Version fields agree at `5.1.0` in the .NET, npm, Cargo, and Tauri manifests.
- The NSIS installer is per-user, needs no administrator rights, bundles the self-contained engine, and was installation-smoke-tested locally at version 5.1.0.
- CuePilot 5.1.0 is published at `Blazzer10200/CuePilot`; Git is on local branch `codex/post-release` with an unpublished compatibility batch.
- The local batch uses a height-scaled virtual 16:9 HUD canvas across 5:4 through 32:9, retains a bounded frame-relative Fishing meter fallback for cropped/windowed evidence, recognizes prompt scales down to 65%, and captures the FiveM client area instead of including title bars and borders.
- The unpublished full local gate passes: 181 .NET tests, engine self-test, 14 frontend tests, zero Svelte warnings/errors, frontend build, Rust format, and 8 Rust tests. Live Fishing and Lockpicking workspaces fit at 1180x760 with zero console errors.

## Recent Relevant Changes

- Added the activity library and separate Fishing and Lockpicking workspaces.
- Added lockpicking detection, temporal tracking, bounded evidence capture/replay, Class C control, and safety stops.
- Added transformed-frame and cursor-mapping regressions covering standard, ultrawide, super-ultrawide, narrow/windowed, offset, and negative-coordinate monitor layouts.
- Added shared safe-viewport geometry and Fishing prompt/meter regressions for 3440x1440 and 5120x1440 displays.
- Added narrow-layout Fishing regressions for 1920x1200 and 1280x1024 plus client-area window capture for borderless and decorated FiveM windows.
- Added DXGI capture support, instrumented Fishing sessions, bridge contract tests, frontend tests, and Rust bridge tests.
- Added development/production identity separation, CDP-based UI inspection, brand tooling, desktop shortcut tooling, and task-oriented project documentation.
- Renamed product/project/test assets from WorkflowLooper to CuePilot and removed the legacy WinForms UI source.

## Known Problems

- Class C still needs the complete live evidence/smoke-test bundle: a successful Observe attempt, a failed/retry attempt, all distinct stages, varied backgrounds/occlusion, and confirmed input/timing behavior.
- The new viewport and DPI compatibility is covered synthetically but still needs one live Class C attempt on the buddy's wider monitor before that hardware configuration can be called field-validated.
- Classes A, B, and D have no calibrated automatic-input path.
- The buddy's wider monitor still needs live Fishing and Class C field validation; automated geometry and detector regressions cover the expected mappings.

## Next Actions

1. Run a complete Class C Observe session in live gameplay and inspect the bounded evidence under `%LOCALAPPDATA%\CuePilot\diagnostics\lockpicking`.
2. Run the explicitly armed Class C controller through success and failure/retry cases; confirm READY clicks, SPIN direction/speed, terminal OPEN behavior, focus-loss stop, and Pause / Break release.
3. Add only representative, privacy-reviewed evidence as regression fixtures and adjust detector/controller calibration from measured results.
4. Run `scripts/verify.ps1 -All`, publish 5.1.0, and verify the GitHub release assets.
5. Do not push or publish the local post-release compatibility batch until the user explicitly approves replacing the live build.

## Relevant Files

- `docs/activities.md`, `docs/development.md`, `docs/code-map.md`
- `src/Automation/LockpickingDetector.cs`, `LockpickingObservationTracker.cs`, `LockpickingObserverEngine.cs`
- `src/Automation/LockpickingClassProfiles.cs`, `LockpickingClassController.cs`, `LockpickingSpinTracker.cs`
- `src/Application/UiBridge.cs`, `ui/src/lib/engine.svelte.ts`, `ui/src-tauri/src/engine_bridge.rs`
- `ui/src/lib/activities.ts`, `ui/src/lib/activities/LockpickingWorkspace.svelte`
- `tests/CuePilot.Tests/LockpickingDetectorTests.cs`

## Canonical Commands

- Full gate: `pwsh -NoProfile -File scripts/verify.ps1 -All`
- Run app: `npm --prefix ui run tauri:dev`
- UI inspection: `npm --prefix ui run cdp:dev`, then `bash ui/scripts/cdp/c.sh inspect`
- Analyze frame: `dotnet run --project CuePilot.csproj -- --analyze-lockpicking C:\path\to\frame.jpg`
- Replay sequence: `dotnet run --project CuePilot.csproj -- --replay-lockpicking C:\path\to\frames --fps 30`
- Cleanup preview: `pwsh -NoProfile -File scripts/clean-workspace.ps1`

## Important Decisions

- Only .NET may capture the game, make automation decisions, or send input; Svelte/Tauri communicates through the versioned local stdin/stdout bridge.
- Observe mode sends no input. Only explicitly armed Class C may automate lockpicking, and it must stop rather than guess when evidence or safety conditions fail.
- Do not reuse Class C timing for other vehicle classes or infer an unobserved OPEN action.
- Do not replace a known-good installed build solely because compilation succeeds; require the full gate plus activity-specific live validation.
