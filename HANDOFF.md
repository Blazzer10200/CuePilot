# Handoff — CuePilot — 2026-09-04 20:37 CDT

## Current Objective

- CuePilot 5.3.4 is published as the latest stable GitHub release. Source, public downloads, update feed, release notes, and local release copies are verified. No further publication work remains.

## Current State

- Source manifests and public packages are 5.3.4, tagged at `6f569db7a867cbe114fd39de50d4c8e275556e04` on GitHub `main`. Release: https://github.com/Blazzer10200/CuePilot/releases/tag/v5.3.4.
- Public installer, full/delta updater packages, portable ZIP, feed, checksums, and manifest are mirrored in `release/velopack/`. A hash-verified `CuePilot-5.3.4-Setup.exe` is on the Desktop.
- The installed app remains the previously verified 5.3.3 build; publication did not reinstall it. Its Desktop shortcut points to `%LOCALAPPDATA%/CuePilotDesktop/current/cuepilot-ui.exe`. It can update to public 5.3.4 through the update center.
- Windows inspection previously verified the installed 5.3.3 UI showing Local Engine Online and Idle · input off. The 5.3.4 patch changes test isolation, CI failure handling, documentation, and version metadata; runtime detection/input behavior is unchanged.
- Settings and `pickpocket-state.json` SHA-256 hashes matched their pre-install values.
- Fishing remains accepted and parked. Lockpicking remains observe-only. Saved Pickpocket timing remains unchanged; Yellow 14 ms is still an unverified proposal.

## Recent Relevant Changes

- Shared FiveM window, Settings, and Diagnostics toolbar across activities, with active-run guards, safe stop before navigation, and dialog focus restoration.
- Compact readable Home rows, larger descriptions and labels, amber calibration styling, and truthful disconnected indicators.
- Explicit Pickpocket idle/observing/armed/tap-sent/cooldown/disconnected states; dated saved results, save feedback across controls, and secondary technical measurements.
- Diagnostics initially selects the current activity. Lockpicking setup guidance stays in its workspace.
- Responsive regression coverage includes 760×620, 820×700, 900×700, and 1180×760, plus shared tools, state labels, save failures, and keyboard focus.

## Verification

- Full local `scripts/verify.ps1 -All` passed on 5.3.4: 435 .NET tests, headless self-test, 42 UI unit tests, zero Svelte diagnostics, production frontend build, Rust format/Clippy, and 14 Rust tests. All 20 Playwright scenarios passed again.
- GitHub Build run `33935668623` and Release run `33935825378` succeeded at the tagged source. The published release is stable and latest.
- All eight public assets were downloaded without authentication and matched their GitHub SHA-256 digests. Installer/full-package manifest hashes, full/delta feed SHA-256/SHA-1 hashes, sizes, and packaged executable versions matched. The downloaded engine self-test passed.
- Local verification and publication receipts are in `release/velopack/verification-receipt.json` and `release/velopack/publication-receipt.json`.
- Public Setup SHA-256: `4b0a6cc977839464cee3975e451d07cd94ac4bba073ddfd3fa99e2932cbb6c42`.
- Public full package SHA-256: `79edf5e156416a146cb17121e4fada648473f15eab048fef17ed8e5d96683338`.

## Known Problems

- An initial hidden launch hung before starting the engine. Restarting the exact hung shell with a normal visible launch restored the engine; bringing the window forward made the app visible over FiveM. No source change was needed for this recovery.
- Automatic approval review rejected the combined old-release deletion/promotion command with “blocked by policy.” No deletion occurred. The new artifacts were copied into the official release directory instead.
- Old 5.3.2/5.3.3 full packages, duplicate 5.3.3 staging under `release/velopack-next/`, and disposable development executables remain. No cleanup deletion was retried during publication.
- The 5.3.3 tag remains as the failed publication attempt: parallel detector suites exceeded wall-clock timing budgets in CI. The 5.3.4 tests isolate Fishing suites without changing the 60 ms meter or 250 ms prompt limits; both GitHub jobs then passed.
- The public release remains unsigned. Item identity is the planned target; inventory acquisition is not independently verified.

## Next Actions

1. No further UI or GitHub release work is required for this batch.
2. Remove retained old/staged build artifacts only when a permitted deletion path is available. Preserve source, settings, gameplay evidence, and dependency caches.
3. Gameplay calibration remains separate work. The installed 5.3.3 app may use the published update to 5.3.4 when desired.

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
- Prior snapshot preserved at `C:/Users/BLAZZER/.codex/archive/handoffs/WorkflowLooper/HANDOFF-20260904-203637-before-github-534.md`.
