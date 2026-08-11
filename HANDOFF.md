# Workflow Looper Handoff

## Session 1 — 2026-08-10 19:05 -05:00 (complete)

### Completed
- Released Workflow Looper v3.1.0: https://github.com/Blazzer10200/WorkflowLooper/releases/tag/v3.1.0
- Added adaptive triggered routines, precision editing, target-window safety, rhythm calibration, configurable shortcuts, visual cue detection, and atomic pattern persistence.
- Removed built-in presets and all preset UI/documentation assets.
- Completed the native UI pass: custom controls/icons, restrained page motion, responsive 1060×720 minimum layout, full borderless window chrome, and custom branding.
- Added the multi-size Windows icon, matching in-app brand mark, solution file, organized source tree, tests, CI, release packaging, checksum, and current screenshots.
- Verified formatting, Release build (0 warnings/errors), 5/5 xUnit tests, development/published self-tests, GitHub Build/Release Actions, public checksum, and clean worktree at commit `da7be81`.

### In Progress
- None. Project is intentionally parked after v3.1.0.

### Key Decisions
- Physical hold/release starts adaptive tapping — the minigame can appear early or late.
- Visual completion uses a local 20×12 grayscale fingerprint — no screenshots, account, telemetry, or network runtime.
- Default visual-change threshold is 86% — derived from the supplied fishing recording and remains user-adjustable.
- Presets stay removed — recorded/calibrated rhythms are more trustworthy than canned timing.
- App remains a portable self-contained Windows x64 executable — easiest GitHub distribution path.

### Failed / Don't Retry
- Shortcut values painted blank inside controls placed in the settings card's second table column; the final full-card selector layout is reliable and visually clearer.
- Do not use the video pipeline directly on odd-width source frames; the analysis copy required even dimensions. The original recording was never modified.

### Gotchas
- GUI preview commands must use `Start-Process -Wait`; direct PowerShell invocation can return before the WinExe saves its screenshot.
- Keep app and target process at the same Windows integrity level or injected input may be rejected.
- Do not perform automated live-game validation while the user is playing; it can steal focus or interfere with input.

### Load-Bearing Invariants
- `Pause / Break` must always release held input and stop playback/routines.
- Target locks must stop automation when foreground focus leaves the captured process.
- Physical mouse hooks must ignore injected events or the routine can trigger itself.
- Pattern/settings migration, atomic replacement, and `.bak` preservation must remain intact.

### Don't Touch
- Published tag `v3.1.0` and release assets.
- Existing user patterns/settings under `%LOCALAPPDATA%\WorkflowLooper`.
- The source fishing recording under `C:\Users\BLAZZER\Videos\Snipping Tool`.

### Next Steps
1. No required development work.
2. When resuming, open `WorkflowLooper.sln`, read this file and `README.md`, then run the commands under README “Build and verify.”
3. First optional task: user-driven in-game acceptance testing and tuning of interval, hold, and visual threshold.

### Files Modified
- `WorkflowLooper.csproj:11` — current version and packaging metadata.
- `README.md:33` — routine usage; `README.md:91` — verification; `README.md:100` — project map.
- `CHANGELOG.md:3` — v3.1.0 release notes.
- `src/Application/MainForm.cs` — complete native UI and workflow orchestration.
- `src/Automation/AdaptiveRoutineEngine.cs` — adaptive routine state machine.
- `assets/branding/` — source artwork, transparent PNG, and Windows ICO.
- `.github/workflows/` — verified build and tagged-release automation.

---
