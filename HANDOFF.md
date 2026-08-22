# Handoff — CuePilot — 2026-08-21 23:42 CDT

## Current Objective

- Publish CuePilot 5.1.7 with the guarded one-click fishing cast acceleration, usability/alignment pass, and reliability hardening. Overlay notifications remain disabled-by-default and backburnered.

## Current State

- The version-synchronized 5.1.7 release candidate and NSIS installer are built. The local `%LOCALAPPDATA%\CuePilot` installation remains 5.1.5 because the sandbox denied both installer writes and the documented portable-shortcut refresh; install the verified 5.1.7 artifact outside the sandbox.
- Git publication is prepared but not completed: this sandbox explicitly denies `.git` index writes, and GitHub authentication/network access is unavailable. No file was staged, committed, tagged, or pushed.
- Overlay source is included for later continuation, but official builds require an explicit `CUEPILOT_OVERLAY_ENABLED=1` opt-in and therefore do not create the unfinished overlay by default.
- Cast acceleration is implemented in source with a persisted 5,000 ms default delay, a single 40 ms LMB pulse, skip-on-meter/prompt behavior, and cancellation-safe release. It has not yet been tested in live FiveM gameplay.
- The main UI now exposes a prominent Start/Stop Fishing button, explicit five-stage progress states, a single primary status hierarchy, Basic/Advanced settings guidance, clearer observe-only Lockpicking states, and a non-overlapping Detection Review toolbar.
- The first CDP/WebView preview attempt hit a WebView2 breakpoint while FiveM and installed CuePilot instances were active. Subsequent one-instance launches with the overlay disabled, both software and normal rendering, and fresh WebView profiles all stopped before Tauri's setup callback and created zero WebView2 children. Every hung repository Tauri tree was stopped afterward; the installed build and FiveM were not terminated.
- The inspectable launcher now uses Vite's runner config loader, repository-local Cargo output, a fresh WebView profile per launch, timestamped stdout/stderr logs, a graceful fallback when CIM process inventory is denied, and disables the unfinished overlay during main-window review.
- The browser-only Vite preview was stopped before release preparation; no repository development server or native dev app remains running.

## Recent Relevant Changes

- Added a transparent Tauri overlay window and mailbox polling between the Rust shell and overlay WebView.
- Added F10/F9/Pause global-shortcut notifications and styled status/fault/shortcut cards.
- Reworked overlay show ordering to position the window, then reassert topmost and click-through behavior.
- Verified `svelte-check` and `cargo check`; a live F10 probe reached the callback and rendered the overlay WebView.
- Added a guarded cast-acceleration gate inside the meter-wait loop, a 3,000–10,000 ms settings control, diagnostic events, persistence coverage, and focused gate tests.
- Made diagnostic evidence logging honor its supplied output directory so the full test suite remains isolated from `%LOCALAPPDATA%`.
- Hardened Win32 DLL lookup and window-target validation, made frame-source construction cleanup-safe, cached detector JSON options, and normalized culture-sensitive parsing/evidence names.
- Reworked Activity Library counts, Fishing status/action/progress hierarchy, settings explanations, Detection Review scrolling/actions, and Lockpicking’s observe-only/technical-detail separation.
- Verified the 5.1.7 Release solution build, all 227 .NET tests, the headless self-test, all 14 UI tests, all 9 Rust tests, Rust formatting, the production UI build, and zero Svelte diagnostics.

## Known Problems

- Native z-order over FiveM/other accelerated windows was not accepted as user-validated; exclusive fullscreen may prevent a normal desktop overlay from appearing.
- Overlay behavior should not be treated as finished or released until it is tested on the user’s actual display mode.
- The cast-acceleration delay and actual gameplay speed-up remain unverified until several complete live fishing cycles pass.
- Visual alignment and interaction checks through the repo CDP bridge remain pending. Vite is reachable, but the native executable freezes during configured main-window creation before Tauri setup and before any WebView2 child or CDP port exists.
- The native launch must be retried after all installed CuePilot instances are closed. Renderer flags, the overlay window, and reused profile state have been ruled out in controlled A/B attempts. Windows WER shows a history of LiveKernelEvent 141 GPU watchdog reports, but the attached dumps predate this launch and do not prove the current failure is a GPU timeout.

## Next Actions

1. Live-test several complete fishing cycles and tune the 5,000 ms cast-bar delay if necessary without changing the existing circular-meter pulse controller.
2. Confirm diagnostics contain exactly one cast-acceleration click per successful `E` cast and no click when a circular meter or actionable prompt appears first.
3. Install the verified 5.1.7 NSIS artifact outside the workspace sandbox, then launch `npm --prefix ui run cdp:dev` once and inspect the timestamped `tmp/cdp-dev-*.out.log` and `.err.log` files if CDP does not bind. The launcher already uses a fresh profile and disables the unfinished overlay; do not add software-rendering flags unless new evidence calls for them.
4. From a normal user shell with GitHub access, commit the prepared tree, push `codex/post-release`, create annotated tag `v5.1.7`, and push the tag so `.github/workflows/release.yml` publishes the installer and checksum.
5. Once CDP is healthy, visually verify Activity Library, Fishing, both drawers, Lockpicking, and narrow-window breakpoints; confirm the persistent Detection Review toolbar never covers content.
6. After gameplay and visual validation, tune only the cast-bar delay if evidence requires it; keep the shipped one-click and skip-on-meter/prompt safety bounds intact.
7. When overlay work resumes, start the development server only and test native visibility/z-order over the actual FiveM display mode.
8. Before any release build containing the overlay, decide whether it needs a stronger Windows-native/topmost implementation or an explicit borderless-window requirement.

## Relevant Files

- `ui/src/Overlay.svelte`
- `ui/src/overlay.css`
- `ui/src-tauri/src/lib.rs`
- `ui/src-tauri/src/engine_bridge.rs`
- `ui/src/main.ts`
- `src/Automation/AdaptiveRoutineEngine.cs`
- `src/Automation/FishingCastAcceleration.cs`
- `src/Domain/RoutineModels.cs`
- `tests/CuePilot.Tests/FishingCastAccelerationTests.cs`
- `ui/src/App.svelte`
- `ui/src/app.css`
- `ui/src/lib/activities/ActivityPicker.svelte`
- `ui/src/lib/activities/LockpickingWorkspace.svelte`
- `ui/package.json`
- `ui/scripts/run-dev-inspectable.ps1`

## Canonical Commands

- Full gate: `pwsh -NoProfile -File scripts/verify.ps1 -All`
- UI checks: `npm run check --prefix ui`
- Rust checks: `cargo check --manifest-path ui/src-tauri/Cargo.toml`
- Development UI: `npm --prefix ui run cdp:dev`

## Important Decisions

- Keep overlay work development-only until native z-order is proven over the target application.
- Keep the overlay explicit-opt-in only while this subject is backburnered.
- Keep cast acceleration to exactly one bounded click per cast; never reuse the circular-meter pulse loop for it.
- Preserve the verified 5.1.7 engine/release behavior while working on other features.
