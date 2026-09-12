# Activity architecture

CuePilot is an activity-oriented minigame assistant. The desktop shell owns shared Windows integration and each activity owns only the detection and control flow unique to its minigame.

## Shared shell

- FiveM target discovery and validation
- visible-desktop capture and foreground validation without programmatic focus changes
- bounded input delivery and emergency release
- engine connection, notices, window controls, and local storage
- launch-time activity selection and safe return to the activity library

Returning to the activity library must stop any active routine and release held input before the workspace changes.

## Activity boundary

The frontend catalog in `ui/src/lib/activities.ts` is the current source of truth for activity identity, readiness, capabilities, and preparation requirements. Activity workspaces live under `ui/src/lib/activities/`.

An activity becomes **Ready for automatic input** only after it has:

1. representative success, failure, and transition references;
2. detector regressions covering different backgrounds and UI scales;
3. a bounded input cadence with foreground and emergency-stop behavior;
4. activity-specific settings and local diagnostics where needed;
5. a complete live minigame smoke test.

Until those conditions pass, do not label the activity Ready. Pickpocket remains
calibration-only: it defaults to observation and its explicitly selected
one-attempt modes require a new Preparing state, send at most one bounded tap,
and disarm on completion, manual input, Stop, or error. Successful automatic
wide-target delivery is evidenced, but that does not make narrow-target timing
generally calibrated. Other activities retain their existing input gates.

## Current activities

### Pickpocket — One-tap calibration

The live workspace reads the current regions and marker through the .NET observer. Default selection is the widest upcoming detected region; an optional preferred color selects a region without claiming an item identity. Layouts are reacquired every popup, including uneven spacing and repeated known colors. The reference tab retains the explicitly labeled Luxury Watch recording and its local saved selection.

Capture, presentation age, analysis timing, bounded evidence, and an engine-owned three-minute cooldown are connected through the bridge. A recognized grab/miss or three missing frames after Active starts cooldown; the persisted cooldown survives app restart when its state can be saved and restored. Focus loss or a ten-minute limit ends the run. Two manual live grabs verified the detector, 16.8 ms sampling cadence, and complete result capture.

Observation remains the startup default. The selected automatic mode requires a
fresh Preparing state, verified DXGI prediction, unchanged foreground window
and bounds, no physical Space press, and no cooldown. It performs a final gate
check before one owned 35 ms Space tap; Stop, cancellation, or focus loss closes
the gate and releases that key. Stop preserves the selected mode, but only a
new explicit F7 start arms another attempt. Use
[Pickpocket debugging](pickpocket-debugging.md) for current operating guidance;
the historical plan and evidence remain under `docs/history/` and
[pickpocket-evidence.md](pickpocket-evidence.md).

### Fishing — Ready

Fishing retains the existing .NET prompt detector, circular-meter tracker, feedback controller, settings drawer, and Detection Review timeline. The activity picker does not change its engine timing or detector behavior.

### Vehicle Lockpicking — Observe-only calibration

The .NET observer locates the right-side circular HUD relative to the selected FiveM frame, classifies numbered, transition, SPIN, OPEN, and disappearance states, and tracks the same active circle through its outlined, shrinking, and bright-green READY states. READY requires a verified target plus either measured inward ring motion or the observed bright fill and is reported once for that target. Stale presentation timestamps, slow analysis, and extreme capture stalls withhold the prediction; normal accumulated batches on a high-refresh display do not.

Observe mode remains input-free. Automated Class C input is unavailable in the current release while concurrent target-label recognition is validated from saved evidence. Classes A, B, and D remain unavailable until their own live evidence is recorded.

Fishing defaults to `F10`; Pause / Break remains the unconditional emergency stop. Observe mode never sends input.

After initial acquisition, HUD search is localized around the prior frame and the capture loop runs without an artificial post-analysis delay. The first full search remains available immediately when the HUD appears or tracking confidence is lost.

Each observe session stores bounded state-transition frames and JSONL metadata under `%LOCALAPPDATA%\CuePilot\diagnostics\lockpicking`. While SPIN is visible, the debugger also saves a bounded 12 Hz HUD crop burst and records cursor angle, radius, clockwise travel, angular velocity, elapsed time, and capture freshness. Use `CuePilot.exe --analyze-lockpicking <image>` to inspect one full frame, or `CuePilot.exe --replay-lockpicking <frame-directory> --fps <rate>` to replay an ordered sequence through the production detector and temporal tracker.

The remaining live evidence bundle should include:

- one complete successful Observe-mode attempt from the first prompt through completion;
- one failed attempt and any retry or timeout state;
- still frames for every distinct stage, marker, target, and prompt;
- bright, dark, moving, and partially obstructed backgrounds;
- the exact keyboard or mouse input used at each stage;
- approximate safe timing windows and whether presses are taps, holds, or directional motion.

SPIN is recognized as a clockwise cursor orbit around the HUD center, with speed varying by server vehicle class. Class C uses the recorded calibration above. OPEN is used as a terminal confirmation only; CuePilot does not guess any unobserved OPEN action. Do not reuse the Class C cadence for another vehicle class.
