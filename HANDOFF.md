# Handoff — CuePilot 5.3.6 — 2026-09-12

## Current Objective

The documentation, verification, diagnostics, local installation, and cleanup batch is complete. Start with [docs/README.md](docs/README.md), the [code map](docs/code-map.md), and [current backlog](docs/product-backlog.md).

## Current State

- Source versions are 5.3.6 across all six release files, including both root npm lockfile fields and the exact Cargo lockfile application entry.
- CuePilot 5.3.6 is installed in the normal Windows user session. The previous registered 5.3.5 installation was uninstalled. One 5.3.6 registration remains, and the duplicate Codex-redirected 5.3.5 payload was removed.
- The installed shell and engine hashes match the new full package. Headless self-test and a protocol-1 stopped/disarmed bridge snapshot passed. A normal visible launch produced a responsive CuePilot window and its owned installed engine.
- Settings remained byte-for-byte unchanged. All 126 pre-install history entries remained unchanged; a new post-launch attempt increased History to 127. Do not restore the older backup over new activity.
- Current installer and full package: `release/velopack-5.3.6/`. Install and startup proof: `release/velopack-5.3.6/installed-verification.json`.
- The batch includes the previously uncommitted 5.3.5 recovery and the subsequent documentation/tooling work. The user authorized committing and pushing the complete batch to the configured `origin/main` destination; no release tag or public installer publication was requested.

## Recent Relevant Changes

- Organized maintained documentation, linked source navigation, a short actionable backlog, and dated design/calibration history. Original plan URLs remain as compatibility landing pages.
- Receipts now identify staged, unstaged, deleted, and untracked content, retain the dirty-tree fingerprint fields, and separately record index identity. Fixtures cover mixed changes, Unicode paths, repeat stability, and safe temporary cleanup.
- A shared six-file version reader serves project status, packaging, and the release tag gate. Package status shows the matching versioned manifest rather than an older mirror.
- `verify.ps1 -Browser` runs the existing Edge/Playwright suite; default and `-All` include it. Both GitHub workflows now run documentation/tooling and browser checks.
- Launcher cleanup requires the exact checkout Tauri `dev` command and owned process descendants or exact development executable paths; independent Vite builds and other installations are preserved.
- Diagnostics use a bounded 256 KiB log tail. Screenshot reads enforce the existing per-image/aggregate budgets on actual bytes, including growth after metadata inspection.
- Default workspace cleanup preserves `tmp/`, user-data backups, replay evidence, and Cargo caches.

## Verification

- Full `scripts/verify.ps1 -All` passed in about 153 seconds.
- .NET Release build: zero warnings/errors; 438 tests passed; headless self-test passed.
- Frontend: 42 unit tests; zero Svelte errors/warnings; production build passed.
- Browser: all 20 Playwright scenarios passed, including compact/intermediate layouts, History, settings, focus, disconnected states, and save failures.
- Rust: format and Clippy with warnings denied passed; all 17 tests passed.
- Docs/tooling: local links, 4 link-checker tests, 4 package-selection scenarios, 5 version assertions, 16 receipt assertions, and 11 launcher assertions passed. Final receipt regressions also cover empty/single-file array shape.
- Native development WebView: rendered 5.3.6, engine online, no console errors, Home/Pickpocket fitting at 1180×760, idle/input-off state, and Detection Review opening successfully.
- Saved grass-scene replay: two frames, zero mismatches, zero hypothetical presses. No gameplay input was sent by this task.
- Packaging passed; installed payload/registration/self-test/bridge checks passed. The installed release was not inspected using computer-use automation.
- Bounded code review found no remaining actionable findings in the changed engine, diagnostics, launcher, verification, and CI paths. Regression images were visually checked for unrelated private overlays.
- Local logs and receipts are under `tmp/host-536/`; GitHub workflow results should be checked against the release commit.

## Cleanup

- Removed 21 inspected obsolete package/profile/install targets: about 785 MiB.
- Removed about 324 MiB of disposable build outputs: total recovery approximately 1.08 GiB.
- Retained the 5.3.6 installer/feed/full package, small historical receipts, diagnostic evidence, settings backups, dependency/compiler caches, and source.
- Older release directories retain historical metadata but no longer contain their installer/package payloads. Do not treat those old manifests as available installers.
- The supported installed Desktop shortcut remains. Temporary installation/inventory/launch tasks and the inspection wrapper were removed/stopped.

## Known Problems / Limits

- The first background shortcut launch did not remain healthy; a normal visible relaunch recovered a responsive window and engine. A Windows AppHang event was recorded. If it recurs, investigate startup/WebView evidence rather than changing gameplay timing.
- Narrow-target Pickpocket timing and occasional DXGI capture failure still need fresh live-session evidence. This batch preserved calibration and input safety gates.
- Lockpicking remains observe-only; Fishing remains accepted and parked.
- Stage/package before starting development watchers to avoid transient engine-resource locks.
- Codex MSIX redirection can mislead installer checks. Use normal-user host verification for installations.

## Next Actions

1. Use the installed CuePilot shortcut. Preserve any new failed diagnostic session before changing detector thresholds or timing.
2. Keep documentation/tooling gates current; use the full gate for runtime releases.
3. Build outputs were cleaned after verification; rebuild before running source-tree engine executables.
4. Use GitHub run status for this commit to distinguish local verification from hosted CI.

## Canonical Commands

- Orientation: `pwsh -NoProfile -File scripts/project-status.ps1`
- Docs/tooling: `pwsh -NoProfile -File scripts/verify.ps1 -Docs`
- Browser only: `pwsh -NoProfile -File scripts/verify.ps1 -Browser`
- Full gate: `pwsh -NoProfile -File scripts/verify.ps1 -All`

## Important Decisions

- No Windows computer-use automation; source, CLI, and the local development bridge remain the inspection routes.
- Previous snapshot preserved under `$CODEX_HOME/archive/handoffs/WorkflowLooper/HANDOFF-20260912-before-release536-6d9cc7412e2042aea743f07649512d46.md`.
