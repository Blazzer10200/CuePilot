# Handoff — CuePilot — 2026-08-25 08:00 CDT

## Current Objective

- Maintain the completed CuePilot 5.2.0 Velopack release, preserve the audited input/NUI safety rules, and continue only the explicitly listed live follow-ups and backlog work.

## Current State

- Release source: CuePilot 5.2.0, published from `main` merge `8208abad` as the public [`v5.2.0` GitHub release](https://github.com/Blazzer10200/CuePilot/releases/tag/v5.2.0).
- Versions are synchronized at 5.2.0 across .NET, npm/lock, Cargo/lock, and Tauri configuration.
- Velopack crate and CLI are pinned exactly to 1.2.0. Tauri's NSIS bundler is disabled.
- `release/velopack/` contains the verified 5.2.0 installer, portable zip, full package, feed, checksum, and release manifest; the tagged GitHub workflow is the public asset source of truth.
- This computer now has only the Velopack 5.2.0 app registered under `%LOCALAPPDATA%\CuePilotDesktop`; the legacy 5.1.7 NSIS app is uninstalled and its desktop development shortcut is removed.
- `%LOCALAPPDATA%\CuePilot` is now data-only: valid settings, patterns, small workflow backups, four fishing sessions, and the newest eight lockpicking sessions remain. Legacy binaries, WebView cache, old release archives, stale evidence, and migration staging were removed.
- Repository cleanup removed 22,767.5 MB of historical generated output and a final 5,650.2 MB of regenerated test/smoke/build output, while preserving `release/velopack/` and dependency caches. Measured C-drive free space increased from 844.0 GB to 863.5 GB during the main cleanup.
- Full gate passes after NUI/focus hardening: 239 .NET tests, headless self-test, 18 Vitest tests, Svelte 0 errors/0 warnings, production UI build, Rust fmt, Clippy with warnings denied, and 11 Rust tests. PR, `main`, and tagged-release GitHub runs also pass, including installer publication and public-feed verification.
- Disposable installed update proof passes: isolated `CuePilotUpdaterSmoke` 5.2.0 downloaded/applied the generated 5.2.1 delta, stopped the packaged .NET sidecar, relaunched as 5.2.1 with the replaced marker, then removed its process, install, registry entry, and shortcuts.
- Live CDP review passed at 1180x760 and emulated 760x620: update dialog fits, focus traps/restores, dev copy stays out of the feed, and console errors are zero. Repo-owned CDP processes were stopped.

## Recent Relevant Changes

- Added `ui/src-tauri/src/update_service.rs`: GitHub-source checks, off-UI-thread download/apply, progress events, source-build handling, and explicit engine-sidecar shutdown before relaunch.
- Added `ui/src/lib/updates.svelte.ts`, `UpdateCenter.svelte`, and focused tests; the version badge opens updates and active activities block installation.
- Added `scripts/package-velopack.ps1`, WebView2 bootstrapping, release manifest/checksums, exact-version gates, and GitHub feed publication verification.
- Chose pack ID `CuePilotDesktop` so Velopack never owns legacy `%LOCALAPPDATA%\CuePilot`, which mixes binaries with settings and diagnostics.
- Restored this handoff, refreshed README/security/development/backlog/code-map/AGENTS, and added `scripts/project-status.ps1` for fast orientation.
- Added `InputReleaseSafety` and `OwnedRoutineWorker`; Fishing and Lockpicking own and await run cleanup, reject overlapping restarts, and bound stop deadlines. Owned held input releases without foreground validation, while idle and observe-only stops emit no synthetic events.
- Removed the engine's `SetForegroundWindow`/`ShowWindow` path. Automatic delivery now waits up to ten seconds for the user to return to FiveM without changing NUI/raw-input focus; Foreground-only still fails immediately.
- Added `AutomationInputGate`: Stop closes the gate before publishing its diagnostic event, releases only owned E/LMB state, and prevents a detector result from sending input after stop. This addresses retained evidence where an E press began 303 ms after the old stop event.
- Applied the same owned-button/no-late-input rule to the Class C controller and removed observe-only Lockpicking's unconditional release injection.
- Serialized Rust `EngineBridge` start/stop through a shared lifecycle gate and atomically takes the child during shutdown.
- Added `LockpickingDiagnosticSession`: bounded asynchronous encoding/JSON/trace work, 10 Hz/900 target-trace ceiling, unique session IDs, and eight-session/500 MB retention.
- Added `scripts/test-velopack-update.ps1` plus a feature-gated installed smoke path in the real Rust updater.
- Hardened `scripts/clean-workspace.ps1`: current release artifacts and the tracked engine placeholder are preserved, stale smoke/audit/packaging staging has a dedicated selector, and previews show readable per-target and total sizes.
- Packaging removes its temporary staging directory even on failure, and the inspectable UI launcher now recreates `tmp/` after a clean workspace.
- Rebuilt and cleanly reinstalled the corrected production 5.2.0 package locally. Installed shell/engine hashes match the package, settings stayed byte-identical, the packaged engine self-test passed, and the installed app was relaunched for the user from the Velopack `current` path.
- Updated GitHub's official checkout, setup-dotnet, setup-node, and upload-artifact actions to their current Node 24-based major versions.

## Known Problems

- One-click cast acceleration has automated coverage but no recorded exactly-one-click live smoke test.
- The FiveM F1/NUI focus regression has code and automated coverage but still needs the user's live tab-out/tab-in/menu navigation confirmation.
- 5.2.0 is unsigned. This computer's protected first migration is complete, and public users require the documented manual first Velopack install before later in-app updates.
- Lockpicking remains Observe-only; Class C literal-label evidence is incomplete.
- `ui/src/App.svelte` / `app.css` remain large, and the .NET/Rust/TypeScript bridge contract still lacks one shared versioned fixture.

## Next Actions

1. Confirm in FiveM that tab-out/tab-in followed by F1/NUI navigation keeps the third-person camera still, both after opening CuePilot and after a stopped Fishing run.
2. Perform and record the supervised exactly-one-click cast live smoke test.
3. Add component/browser interaction CI, then split the Fishing workspace and shared dialogs/drawers out of the root shell.
4. Add a shared versioned bridge fixture across .NET, Rust, and TypeScript.
5. Keep Class C disabled until literal labels 1–4 pass the saved concurrent-sequence evidence gate and a supervised live test.

## Relevant Files

- `ui/src-tauri/src/update_service.rs`, `ui/src-tauri/src/lib.rs`, `ui/src-tauri/Cargo.toml`
- `ui/src/lib/updates.svelte.ts`, `ui/src/lib/UpdateCenter.svelte`, `ui/src/App.svelte`
- `scripts/package-velopack.ps1`, `scripts/project-status.ps1`, `.github/workflows/release.yml`
- `src/Automation/AdaptiveRoutineEngine.cs`, `src/Automation/InputSender.cs`, `src/Automation/RoutineWorker.cs`, `src/Automation/LockpickingObserverEngine.cs`
- `src/Diagnostics/LockpickingDiagnosticSession.cs`, `scripts/test-velopack-update.ps1`
- `ui/src-tauri/src/engine_bridge.rs`, `docs/product-backlog.md`, `docs/code-map.md`

## Canonical Commands

- `pwsh -NoProfile -File scripts/project-status.ps1`
- `pwsh -NoProfile -File scripts/verify.ps1 -All`
- `pwsh -NoProfile -File scripts/package-velopack.ps1`
- `pwsh -NoProfile -File scripts/test-velopack-update.ps1`
- `pwsh -NoProfile -File scripts/clean-workspace.ps1`
- `npm --prefix ui run cdp:dev` plus `npm --prefix ui run cdp:serve`

## Important Decisions

- GitHub Releases is the initial update host; checks are automatic, installs are user-confirmed, and pre-releases are excluded.
- Update apply must stop the owned .NET sidecar because it lives inside Velopack's replaceable `current` directory.
- `CuePilotDesktop` is a permanent package identity unless a deliberate data/install migration is designed; do not reuse the legacy mixed-use `CuePilot` root.
- Authenticode signing remains a separate ownership/certificate decision; do not describe the unsigned channel as signed.
