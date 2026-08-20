# Handoff — CuePilot — 2026-08-20 00:28 CDT

## Current Objective

- Publish the fully verified CuePilot 5.1.5 fishing reliability and ultrawide/fullscreen compatibility release.

## Current State

- User gameplay validation passed without hiccups before the final ultrawide/version-label additions.
- The final 5.1.5 installer is installed at `%LOCALAPPDATA%\CuePilot`; UI, engine, and uninstall registry all report 5.1.5.
- Installer: `C:\cargo-targets\release\bundle\nsis\CuePilot_5.1.5_x64-setup.exe`.
- Installer SHA-256: `A9178CA86E0F63555C5227C9AC7BC62FDBC0C08FAD9C2597430B4D6A6CC5D443`.
- Branch `codex/post-release` is aligned with `origin/main`; the release batch is ready to commit, push, and tag.

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

1. Commit and push the 5.1.5 batch to `codex/post-release` and fast-forward `main` without force.
2. Tag `v5.1.5`, push the tag, and wait for the Release workflow.
3. Verify the GitHub release installer/checksum assets and latest-release link.
4. Refresh this handoff with the published release URL and workflow result.

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
