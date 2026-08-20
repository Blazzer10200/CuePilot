# Handoff — CuePilot — 2026-08-20 00:43 CDT

## Current Objective

- Maintain the published CuePilot 5.1.5 fishing reliability and ultrawide/fullscreen compatibility release.

## Current State

- User gameplay validation passed without hiccups before the final ultrawide/version-label additions.
- The final 5.1.5 installer is installed at `%LOCALAPPDATA%\CuePilot`; UI, engine, and uninstall registry all report 5.1.5.
- Installer: `C:\cargo-targets\release\bundle\nsis\CuePilot_5.1.5_x64-setup.exe`.
- Installer SHA-256: `A9178CA86E0F63555C5227C9AC7BC62FDBC0C08FAD9C2597430B4D6A6CC5D443`.
- Commit `4c7471f8b9e2dfca874d4139808a25612de98420` is on `main`, `codex/post-release`, and tag `v5.1.5`.
- GitHub release `v5.1.5` is public, latest, non-draft, and non-prerelease: `https://github.com/Blazzer10200/CuePilot/releases/tag/v5.1.5`.
- GitHub Release workflow `32335879197` and main Build workflow `32335868134` both completed successfully.
- Published installer SHA-256: `9c42f7c8eec1785481a7be419ae93291af9711df306c83231b5eb095df30e385`; the downloaded asset matches its attached checksum.

## Recent Relevant Changes

- Added bounded fishing stage recovery for stale meter tracks, manually advanced prompts, ignored casts, and no-state timeouts.
- Reused one capture source across prompt and meter sampling and preserved decisive suppression evidence for replay/diagnostics.
- Added adaptive centered-safe-canvas plus full-frame HUD searches for 3440×1440, 5120×1440, and other non-16:9 layouts.
- Added conservative black-frame rejection with a precise Borderless Windowed fallback when exclusive fullscreen blocks capture.
- Added captured/synthesized regressions for the supplied ultrawide view and post-collect Cast stall.
- Added an always-visible `v5.1.5` titlebar badge sourced from Tauri release metadata and verified it in the live WebView.
- Removed generated build/capture artifacts, one unused prompt wrapper, and one unused test calculation; dependencies and source fixtures were preserved.

## Known Problems

- Some exclusive-fullscreen/driver combinations can deny desktop capture. CuePilot now detects the black frame and directs the user to Borderless Windowed instead of silently stalling.
- LMB pulse cadence remains intentionally unchanged pending separate live timing evidence.

## Next Actions

1. Let the user continue live-testing the installed 5.1.5 build, including 3440×1440 and exclusive fullscreen where supported by the active graphics stack.
2. If a capture stall occurs, preserve the newest `%LOCALAPPDATA%\CuePilot\diagnostics\sessions\<session-id>` and `diagnostics\fishing-loop.csv` before changing thresholds.
3. Treat any future LMB cadence change as a separate evidence-backed patch; do not mix it into detector identity tuning.

## Relevant Files

- `src/Automation/AdaptiveRoutineEngine.cs`
- `src/Automation/FishingPromptDetector.cs`
- `src/Automation/FishingMeterDetector.cs`
- `src/Automation/GameViewportGeometry.cs`
- `src/Capture/FrameSources.cs`
- `tests/CuePilot.Tests/FishingPromptTests.cs`
- `tests/CuePilot.Tests/FishingMeterTests.cs`
- `ui/src/App.svelte`

## Canonical Commands

- Full gate: `pwsh -NoProfile -File scripts/verify.ps1 -All`
- Installer: `npm --prefix ui run tauri:build`
- Release workflow: `.github/workflows/release.yml`

## Important Decisions

- Keep detector identity thresholds strict; recover from verified visual state transitions instead of blind input.
- Keep capture, detection, and input authoritative in the .NET engine.
- Keep 35–90 ms LMB pulses and `Pause / Break` emergency release unchanged until new evidence justifies a timing patch.
