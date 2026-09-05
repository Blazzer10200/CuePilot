# Handoff — CuePilot — 2026-09-04 15:52 CDT

## Current Objective

- UI polish and official 5.3.3 build/install are complete. Requested old-build deletion was blocked by automatic approval review; retained artifacts are listed below.

## Current State

- Source manifests, official package, installed shell, and installed engine are synchronized at 5.3.3. The Desktop shortcut points to `%LOCALAPPDATA%/CuePilotDesktop/current/cuepilot-ui.exe`.
- Official 5.3.3 installer/feed/portable package are in `release/velopack/`. A hash-verified `CuePilot-5.3.3-Setup.exe` is on the Desktop.
- Windows inspection verified the installed UI visibly showing v5.3.3, Local Engine Online, and Idle · input off. Shell and child engine processes report 5.3.3 and 5.3.3.0.
- Settings and `pickpocket-state.json` SHA-256 hashes matched their pre-install values.
- Fishing remains accepted and parked. Lockpicking remains observe-only. Saved Pickpocket timing remains unchanged; Yellow 14 ms is still an unverified proposal.

## Recent Relevant Changes

- Shared FiveM window, Settings, and Diagnostics toolbar across activities, with active-run guards, safe stop before navigation, and dialog focus restoration.
- Compact readable Home rows, larger descriptions and labels, amber calibration styling, and truthful disconnected indicators.
- Explicit Pickpocket idle/observing/armed/tap-sent/cooldown/disconnected states; dated saved results, save feedback across controls, and secondary technical measurements.
- Diagnostics initially selects the current activity. Lockpicking setup guidance stays in its workspace.
- Responsive regression coverage includes 760×620, 820×700, 900×700, and 1180×760, plus shared tools, state labels, save failures, and keyboard focus.

## Verification

- Full `scripts/verify.ps1 -All` passed: 435 .NET tests, headless self-test, 42 UI unit tests, zero Svelte diagnostics, production frontend build, Rust format/Clippy, and 14 Rust tests.
- All 20 Playwright scenarios passed on the final UI source.
- Native development Pickpocket/Diagnostics inspection reported no console errors. Installed UI visibility and engine connection were verified through Windows inspection after normal relaunch.
- Installed engine self-test passed. Installed shell and engine hashes match their entries in the 5.3.3 full package.
- Setup SHA-256: `380ef117fe826115f543d972a7143d1c5f68b5a3a3e3cc2158aac632ea2d016e`.
- Full package SHA-256: `74260ff6726ad84392bac8a5ec359bc189a122f519a2cfc33acf7c2ffb52ae65`.

## Known Problems

- An initial hidden launch hung before starting the engine. Restarting the exact hung shell with a normal visible launch restored the engine; bringing the window forward made the app visible over FiveM. No source change was needed for this recovery.
- Automatic approval review rejected the combined old-release deletion/promotion command with “blocked by policy.” No deletion occurred. The new artifacts were copied into the official release directory instead.
- `release/velopack/CuePilotDesktop-5.3.2-full.nupkg`, duplicate 5.3.3 staging under `release/velopack-next/`, and disposable development executables remain. The installed package cache already contains only 5.3.3.
- The local release remains unsigned. Item identity is the planned target; inventory acquisition is not independently verified.

## Next Actions

1. No further UI changes are required for this batch.
2. Remove retained old/staged build artifacts only when a permitted deletion path is available. Preserve source, settings, gameplay evidence, and dependency caches.
3. Gameplay calibration, Git commit/tag/push, and public publication remain separate work.

## Relevant Files

- `ui/src/App.svelte`, `ui/src/app.css`, `ui/src/lib/SupportCenter.svelte`.
- `ui/src/lib/activities/ActivityPicker.svelte`, `LockpickingWorkspace.svelte`, `PickpocketWorkspace.svelte`, `PickpocketLiveWorkspace.svelte`.
- `ui/src/dev/scenarios.ts`, `ui/e2e/ui-polish.spec.ts`, `ui/e2e/workspaces.spec.ts`.
- `CHANGELOG.md`, `docs/development.md`, `docs/code-map.md`, `docs/product-backlog.md`.

## Canonical Commands

- `pwsh -NoProfile -File scripts/verify.ps1 -All`
- `npm --prefix ui run test:e2e`
- `pwsh -NoProfile -File scripts/package-velopack.ps1`
- `pwsh -NoProfile -File scripts/project-status.ps1 -AsJson`

## Important Decisions

- .NET remains authoritative for capture, detection, input, safety, and persisted facts.
- Only the official installed shortcut is the user launch path; development and raw build outputs are not replacement shortcuts.
- Prior snapshot preserved at `C:/Users/BLAZZER/.codex/archive/handoffs/WorkflowLooper/HANDOFF-20260904-ui-polish-before-533.md`.
