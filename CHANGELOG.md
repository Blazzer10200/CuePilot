# Changelog

## 5.1.1 - 2026-08-15 - Fishing release

- Ships the verified Fishing automation, local evidence review, and CuePilot desktop installer for general use.
- Keeps vehicle lockpicking in safe Observe-only calibration mode while concurrent-target literal-label recognition is validated; Class C automation cannot be started by the UI or shortcut.

## 5.1.0 - 2026-08-14 - Complete CuePilot desktop

- Makes Fishing and Vehicle Lockpicking use one centered, height-scaled FiveM safe viewport across 16:9, 16:10, 5:4/windowed-style, 21:9, and 32:9 layouts while retaining the recorded detector fast paths and false-positive gates.
- Makes the headless engine Per-Monitor-V2 DPI aware and keeps capture, target, virtual-desktop, and cursor coordinates physical and normalized across Windows scaling and mixed-monitor layouts.
- Adds cross-resolution Fishing prompt/meter regressions, Lockpicking HUD/controller regressions, minimum/default/wide desktop UI validation, and an installer-first GitHub release path with checksums.
- Completes the WorkflowLooper-to-CuePilot migration, removes the obsolete WinForms surface and prior project/test assets, and publishes the Svelte/Tauri shell with its packaged self-contained .NET engine as the only supported product.

- Adds a HUD-relative .NET lockpicking detector trained against both supplied 1920×1080 live attempts, with numbered-target, empty-transition, disappearance, and SPIN replay fixtures.
- Adds an input-free observer that reuses FiveM targeting and DXGI/GDI capture, requires a stable target plus measured inward ring motion or the verified bright-green fill before reporting READY, edge-triggers that prediction once per target, stops on capture faults, and stores bounded local transition evidence for calibration.
- Uses DXGI presentation timestamps and accumulated-frame counts to reject genuinely stale or extreme capture stalls without mistaking normal 120 Hz frame batches for stale images, and adds a deterministic full-sequence replay probe for recorded attempts.
- Fixes numbered-target handoff from live evidence so a bright READY circle remains selected through the click transition and the detector then commits to the newly active outlined circle instead of falling back to the previous target.
- Adds a bounded 12 Hz SPIN calibration burst with HUD-cropped frames, cursor angle/radius, clockwise travel and speed, elapsed time, frame age, capture latency, and frame-batch metadata; the live workspace surfaces the same telemetry while input stays gated.
- Makes non-32-bit detector input explicitly pixel-scaled so 120-DPI live JPEGs replay identically outside the DPI-aware app, and recognizes the early SPIN transition before leftover numbered geometry can win.
- Removes the observer's fixed 35 ms post-frame delay, reuses the acquired HUD location on following frames, and rejects most target-search positions with a cheap ring prefilter; the supplied 1080p 30 FPS sequence now averages 23.7 ms per detector frame while preserving state transitions.
- Replaces the preparation-only Lockpicking page with a polished live debug workspace showing HUD boundary, target center, approach ring/ratio, READY ETA, confidence, capture latency, frame batch, sample count, state, and predicted action.
- Adds a separate, explicitly armed Class C controller based on live session `20260814-133216`: it clicks each temporally verified READY target once, requires two SPIN frames, then follows a bounded clockwise orbit at the recorded 1,140°/s and 0.61× HUD radius calibration.
- Adds absolute virtual-desktop cursor delivery, action/input telemetry, and fail-safe stops for Pause / Break, focus loss, capture faults, HUD disappearance, uncertain states, OPEN confirmation, and a 2.8-second SPIN limit. Observe mode remains input-free; classes A, B, and D remain gated.
- Splits reusable Lockpicking class profiles from the generic controller, adds a task-oriented code map, and refreshes repository routing documentation for faster feature work.
- Removes obsolete preparation-workspace CSS and the unused Tauri dialog plugin, and makes workspace cleanup selective, path-validated, and dependency-cache preserving by default.

## 5.0.15 - Reliable shortcut ownership

- Adds a persisted Fishing Start / Stop toggle selectable from `F6` through `F12`, removes the dashboard-only automation button, and keeps `Pause / Break` as a separate emergency release.
- Makes the development profile passive for global shortcuts so CuePilot Dev can remain open while the official build owns the live FiveM controls.
- Treats unavailable Windows hotkeys as a nonfatal warning instead of crashing the second CuePilot profile during startup.

## 5.0.14 - Reliable live meter identity

- Requires signed local contrast between the white `LMB` letter strokes and the dark keycap background, preventing bright water over dark clothing from acquiring a false fishing-meter lock.
- Adds paired 2560×1440 live regressions from the intermittent run: the exact player/water false confirmation is rejected while the real meter at the observed position and scale still acquires.

## 5.0.13 - CuePilot desktop and live fishing reliability

- Renames the product to CuePilot across the desktop UI, engine executable, installer metadata, internal projects, scripts, tests, documentation, and local data paths, then removes the transitional old-name migration and development aliases after preserving the user's data.
- Integrates the approved CuePilot mark across the titlebar, favicon, executable, and installer, with deterministic PNG and multi-resolution ICO generation from one untouched source asset.
- Adds visually distinct `CuePilot` and `CuePilot Dev` desktop launchers, executable identities, WebView profiles, titlebar marks, taskbar icons, and stable local shortcut assets.
- Refines the activity library, truthful Fishing status hero, Settings save feedback, Vehicle Lockpicking readiness workspace, typography, focus states, and restrained motion without changing automation behavior.
- Makes the Svelte/Tauri application the only product UI and removes the legacy WinForms presentation layer without changing detector or controller timing.
- Adds correlated .NET bridge responses, authoritative live snapshots, FiveM target validation, WebView reload recovery, sidecar health reporting, and bounded shutdown behavior.
- Preserves emergency-shortcut configuration in Tauri, adds the missing collect-on-timeout setting, and surfaces complete pending/offline/error states in the desktop UI.
- Adds .NET bridge, Rust protocol, and Svelte engine-client tests; stages the engine automatically for development and release builds; and changes CI/release artifacts to the NSIS installer.
- Tracks the confirmed fishing-meter position and scale across frames, uses local contrast for exposure tolerance, and validates the real LMB glyph during the zero-progress startup state.
- Arbitrates prompt and meter evidence on the same frame before sending `E`, suppressing input when the meter remains visible.
- Saves bounded, annotated exact-frame lock/loss evidence with paired JSON metrics and presents it in the polished Detection Review panel.
- Turns the local fishing trace into a compact activity timeline, keeps existing evidence visible during refresh, traps keyboard focus inside both drawers, and removes unused migration-era capture and styling leftovers.
- Adds a launch-time activity library, preserves Fishing as the ready module, and introduces an input-disabled Vehicle Lockpicking preparation workspace with safe in/out navigation and a tested typed catalog.
- Matches prompt lettering through its local white-stroke/dark-outline contrast instead of requiring a dark game background, covering bright water, shoreline, and sky while retaining strict `E` keycap validation.
- Adds file-based prompt analysis/benchmark probes and live-background cast, meter-start, and failure regression fixtures.
- Removes avoidable prompt-selection allocations and repeated snapshot validation while preserving detector thresholds, controller timing, and the bridge schema.
- Hardens frontend/sidecar lifecycle cleanup against stale connection failures and shutdown-only sidecar launches, with a focused regression test.
- Adds one canonical development guide and narrows default repository searches around generated schemas, lockfiles, build output, and inspection captures.
- Adds bounded, replayable Fishing debug sessions that begin before preflight, preserve decisive near-miss/confirmed frames, expose named detector rejection gates in Detection Review, and never perform additional capture or input.
- Adds a bounded second-scale meter search at measured daylight HUD positions, making all three supplied startup-meter frames pass through the production full-frame locator while preserving full-frame character/reel rejection guards and reporting detector timing in the offline probe.
- Keeps a periodically replaced latest full-frame prompt sample beside the strongest near-miss, so a zero-score Cast/Keep Fish investigation can distinguish an absent prompt from a later unrecognized prompt.
- Densifies the prompt scale pyramid through common fractional UI sizes and lets anti-aliased keycap candidates reach the strict glyph/text scorer, restoring Cast and Keep Fish recognition from 75%–135% without weakening the final confidence, stability, meter-suppression, or foreground-input gates.
- Adds `F10` as a fixed one-key in-game start shortcut so the fishing HUD can remain visible and FiveM can remain foreground, while preserving the configurable emergency-stop shortcut.
- Records a bounded 120-frame rolling sequence of the bottom 35% of every prompt scan and reports when no fishing HUD is actually present, closing the evidence gap between intermittent prompts and periodic diagnostic snapshots.
- Replaces the GDI-only game-frame path with DXGI Desktop Duplication for prompt and meter detection after live evidence proved GDI omitted the visible FiveM action HUD; keeps GDI only as a clearly reported fallback.
- Requires a verified current LMB prompt identity before acquiring a live fishing-meter lock, preventing character, rod, and water geometry from starting false regulation while preserving tracked active-meter performance.
- Replays five manually captured active meters across rock, hillside, sky, sunset water, and evening backgrounds through the production acquisition path.
- Uses the verified live LMB prompt to disambiguate orange scenery from the red failure mark during fresh acquisition while preserving failure detection after lock.

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

- Added a custom application icon and matching in-app brand mark.
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
