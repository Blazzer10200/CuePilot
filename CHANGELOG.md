# Changelog

## 5.0.12 - Coherent daylight frames

- Captures one full FiveM frame per meter sample and searches all calibrated meter positions within it, preventing one control sample from combining several animated UI states.
- Retains the original 35–90 ms pulse cadence and daylight prompt/meter calibration.

## 5.0.11 - Live loss evidence

- Saves one full FiveM frame when a confirmed fishing-meter lock is first lost, alongside the existing CSV trace.
- Keeps input timing and recovery behavior unchanged so a daytime run produces actionable detector evidence without changing game control.

## 5.0.10 - Daytime completion lock

- Keeps the meter locked when FiveM replaces the warm tension ring with the bright lime completion arc, avoiding a needless visual-loss transition at catch time.
- Added a full-resolution daylight caught-state regression and JPEG replay support for high-rate local validation.
- Validated the supplied daytime video at 10 fps: 566/566 meter frames detected.

## 5.0.9 - Daytime pulse-meter lock

- Corrected the daytime capture scale so the visual meter's ring geometry matches the detector, including its initial pulse state before the outer progress arc appears.
- Expanded the radial ring search to recognize the small warm inner ring that is visible while the pulse prompt is active.
- Added replay diagnostics and a full-video validation pass: all 113 sampled supplied daytime frames now lock the meter without changing the established 35–90 ms input envelope.

## 5.0.8 - Daytime video detection calibration

- Calibrated the live meter capture square against the supplied 1080p daytime FiveM recording, retaining the complete tension arc instead of sampling only the inner disk.
- Added six full-resolution early, mid, late, initial, active, and high-progress replay fixtures from the video.
- Searches the observed center placement first and stops only at a decisive confidence lock, reducing repeated capture work without weakening alternate-position coverage.

## 5.0.7 - Large prompt-scale coverage

- Expanded verified prompt matching through 4× UI scale for the `E Keep Fish` / `G Release Fish` layout supplied from FiveM.
- Added standalone and large-scale collect-prompt replays while preserving cast and meter false-positive guards.

## 5.0.6 - Prompt-cutout and failure-confirmation hardening

- Added alpha-template support so transparent prompt cutouts match only their visible buttons and glyphs while opaque live references retain their background checks.
- Requires two consecutive red failure-meter frames before recasting, preventing a one-frame false red match from prematurely ending a catch.
- Removed the unused saved foreground flag; visible desktop capture and physical input continue to enforce foreground operation directly.

## 5.0.5 - Meter position and bright-scene detection

- Replaced the single hard-coded meter crop with five compact candidate regions covering left, center, and right UI placements.
- Increased crop sizing for the full circular meter across FiveM UI scales and resolutions.
- Added a circular tension-ring strength requirement that rejects dark player/HUD geometry in cast scenes.
- Detects the red “FISHING GOT AWAY” circle as a failed minigame and safely returns to the cast flow without extra mouse input.
- Added supplied bright-scene frames, legacy full-frame coverage, and cast-scene false-positive cases to the detector regression suite.

## 5.0.4 - Detection and retry hardening

- Added scale-tolerant prompt matching for common FiveM NUI sizes instead of requiring exact recorded pixel dimensions.
- Prompt and meter startup verification now tolerate one missed detector frame while still rejecting a conflicting prompt/state.
- Prompt clearing requires stronger visual evidence, preventing two detector misses from being mistaken for a successful E press.
- Rechecks for a persistent cast prompt while waiting for the meter and automatically returns to the bounded cast retry path.
- Revalidates foreground input readiness before every prompt retry.
- Increases the bounded E retry budget from three to five attempts before stopping safely.

## 5.0.3 - Meter reacquisition hardening

- Fixed a live-loop failure where brief FiveM minigame transition frames immediately after a mouse pulse could be mistaken for the meter ending.
- Increased the meter capture tolerance around FiveM's animated UI while retaining the detector's target-centered search.
- The controller now pauses input and scans for up to about two seconds of successfully captured detector misses before treating a meter as gone.
- Clears prior tension velocity during a visual transition, preventing a stale meter value from causing an extra pulse after reacquisition.
- Added high-level diagnostics for prompt clearing, meter lock, completion, and failed reacquisition.
- Migrates stale application-window capture settings to the stable desktop capture backend.

## 5.0.2 - Live meter capture fix

- Fixed the fishing loop losing the circular meter mid-minigame when FiveM returned empty `PrintWindow` frames.
- Made the stable visible-desktop capture path the default for both meter and prompt detection.
- Added a regression test preventing automatic capture from silently selecting the flaky application-window backend.

## 5.0.1 - UI rendering and DPI fix

- Fixed transparent custom-button repainting that left duplicated text and corrupted title-bar controls.
- Corrected 125% DPI sizing so the dashboard, action row, and footer remain fully visible.
- Rebalanced the collapsed window, removed redundant title-bar status text, and verified both dashboard states through live Win32 window capture.
- Replaced timing guesses for casting and collecting with stable image-verified prompts from real FiveM frames.
- Moved the fishing routine off the UI thread, preventing prompt detection from freezing or closing the console.
- Restricted prompt analysis to the actual lower-screen interaction region for faster scans and fewer false matches.
- Removed the superseded dashboard implementation, unused fishing frames, and obsolete documentation screenshots.

## 5.0.0 - Automation console rebuild

- Rebuilt the interface as a focused minigame automation dashboard with target, capture, input, detector, and loop state in one view.
- Moved fishing tuning into a collapsible advanced panel and reduced the primary workflow to Set Target, Start Automation, and Stop.
- Removed the obsolete workflow recorder, playback engine, pattern library, raw-event editor, action builder, timing normalizer, and their specialized tests and controls.
- Reduced settings to the emergency shortcut, selected profile, target identity, capture/input preferences, and fishing controller values; added automatic v1–v6 migration to v7.
- Removed unused recording hooks, high-resolution playback timer interop, legacy preview commands, and obsolete visual fixtures.
- Preserved the application-bound targeting/capture/input foundation, proven bounded fishing controller, and exact 10-second collection loop.

## Application-bound automation foundation

- Added persistent FiveM HWND resolution, target reacquisition, and explicit Arm preflight diagnostics.
- Added pluggable application-window/GDI frame sources and target-aware foreground/application input backends.
- Replaced competing watcher/caster tasks with one deterministic fishing state machine.
- Added reusable minigame profile/detector interfaces and managed vision primitives for future minigames.
- Added settings format v6, target/capture/input diagnostic commands, and live capture fallback validation.
- Kept the proven bounded fishing controller and exact `E collect · wait 10 sec · E cast` loop unchanged.

## 4.0.0 - 2026-08-10

- Added a high-level Action Builder for keyboard chords and five mouse buttons.
- Added per-action hold, press-to-press interval, repeat count, repeat duration, and transition delay controls.
- Added seconds/minutes duration editing with readable live action summaries.
- Added safe conversion from compatible recordings while retaining the complete original event timeline and its measured per-action rhythm.
- Added version 3 pattern persistence with automatic v1/v2 migration and unchanged atomic `.bak` protection.
- Added lazy precision playback for long repeated actions without expanding workflow files into thousands of events.
- Retained Advanced Events for raw timing edits and one-click switching back to the original recording.
- Expanded automated timing, conversion, migration, persistence, and UI self-test coverage.
- Reworked the editor into a responsive two-panel sequence and inspector layout.
- Simplified the application shell and typography, removed redundant branding and copy, and replaced mismatched native dropdowns with consistent dark controls.
- Corrected minimum-window clipping in navigation icons, routine status copy, action summaries, and settings actions.
- Reduced Studio to the core record/play workflow, moved secondary pattern and editor commands into compact menus, and relocated live status to the footer.
- Ignored unmatched boundary key releases created when a recording shortcut is released, allowing otherwise valid recordings to remain editable.
- Fixed an integer overflow in the global mouse-move hook that could terminate recording as soon as the pointer moved.
- Fixed dropdown menu disposal so menus can be opened and closed repeatedly without a WinForms `ObjectDisposedException`.
- Added debounced visual-cue start detection so an armed routine waits without clicking until a variable-timing minigame actually appears.
- Required the cue to disappear before automatic re-triggering while retaining physical start/finish controls and foreground safety checks.
- Replaced fixed fishing taps with a closed-loop controller that measures the inner tension ring and independently tracks the outer catch-progress arc.
- Added hysteresis thresholds, minimum input-state duration, automatic meter start, caught-check detection, and target-required arming.
- Added local numeric fishing diagnostics plus regression fixtures taken from the supplied successful catch recording.
- Replaced hold-until-threshold control with capture-independent 35–90 ms pulses, projected-tension braking, and a conservative 68% target after a live overshoot trace.
- Reduced meter-analysis latency to roughly 5 ms per recorded frame by using one locked pixel buffer instead of repeated `GetPixel` calls.
- Removed the obsolete fixed-tap fallback, physical rhythm calibration, captured grayscale cue, manual trigger path, and their unused monitor/hotkey code.
- Added version 3 settings migration that preserves the captured game target while resetting unsafe controller values.
- Added a cancellable AFK fishing loop: collect with `E`, wait a randomized 5–10 seconds, cast with `E`, settle for 2 seconds, hold LMB for 1 second, then return to meter detection.
- Added safe automatic recasting after failed attempts without sending the collection key, plus key-up and mouse-up guarantees for every cancellable action.
- Added version 4 settings for cast timing and randomized post-catch delays while retaining prior target and controller settings.
- Removed the unreliable Throw Line confirmation and automatic cast-charge branches after live regression evidence showed they blocked a proven circle minigame cycle.
- Simplified the complete loop to the exact game sequence: `E`, wait 1 second, watch the circle; after catch, `E`, wait 10 seconds, and repeat. LMB is now exclusive to the bounded circle controller.
- Added version 5 settings migration and a deterministic loop-sequence regression test.

## 3.1.0

- Added a custom Workflow Looper application icon and matching in-app brand mark.
- Added maximize/restore controls and title-bar double-click behavior.
- Refined page headers, window chrome, and product identity while preserving the compact layout.
- Reorganized application code into focused `src` folders and documented the project structure.

## 3.0.0

- Rebuilt the interface around responsive DPI-aware layouts and consistent Fluent-style icons.
- Removed built-in tap presets and the preset drawer.
- Added a precision event editor with timing analysis, normalization, undo/redo, duplication, deletion, and enable/disable controls.
- Added version 2 pattern migration, atomic saving, `.bak` protection, and per-pattern execution settings.
- Added target-window capture and foreground safety checks.
- Added a reusable triggered routine with physical left-click handoff, drift-free tapping, `E` collection, cooldown, and re-arming.
- Added physical rhythm calibration and optional local visual-cue change detection.
- Added a formal xUnit test project, richer self-test coverage, portable ZIP packaging, and SHA-256 release checksums.

All notable changes are documented here.

## 2.1.0

- Replaced native window chrome with a draggable, fully themed title bar.
- Replaced native numeric spinners, checkbox, preset menu, and library scrollbar with custom controls.
- Added a Settings page for capturing, validating, saving, and resetting global shortcuts.
- Added conflict detection and persistent local shortcut settings.
- Added a structured animated preset chooser with full preset details.
- Rebalanced the complete layout and removed clipped labels and path text.
- Added custom-control and shortcut-settings coverage to the self-test.

## 2.0.0

- Added a persistent pattern library with automatic most-recent loading.
- Added clear-current, confirmed delete, open, and Save As actions.
- Added Rapid Tap, Steady Tap, Balanced Hold, and Slow Hold presets.
- Added animated Studio/Guide page transitions and live status motion.
- Added a custom dark preset menu, refined typography, and cleaner layout.
- Kept cursor capture and playback disabled by default for click/key accuracy.
- Added preset validation to the deterministic self-test.
- Added GitHub build and tagged-release workflows.

## 1.1.0

- Added high-resolution waitable-timer playback.
- Preserved exact event spacing and variable click duration.
- Made mouse-movement capture optional.

## 1.0.0

- Initial local macro recording and playback release.
