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

Capture, presentation age, analysis timing, bounded evidence, and an engine-owned three-minute cooldown are connected through the bridge. A recognized grab/miss after Active, or a panel missing for three seconds after Active (three frames once the run's tap is spent), starts cooldown; a result with no Active before it is ignored. The persisted cooldown survives app restart when its state can be saved and restored. Focus loss or minimizing pauses the run and requires a fresh Preparing state on return; a ten-minute limit ends it. Two manual live grabs verified the detector, 16.8 ms sampling cadence, and complete result capture.

Observation remains the startup default. The selected automatic mode requires a
fresh Preparing state, verified DXGI prediction, unchanged foreground window
and bounds, no physical Space press, and no cooldown. It performs a final gate
check before one owned 35 ms Space tap; Stop or cancellation closes the gate and
releases that key, and focus loss releases it and pauses until a new Preparing state. Stop preserves the selected mode, but only a
new explicit F7 start arms another attempt. Use
[Pickpocket debugging](pickpocket-debugging.md) for current operating guidance;
the historical plan and evidence remain under `docs/history/` and
[pickpocket-evidence.md](pickpocket-evidence.md).

### Fishing — Ready

Fishing retains the existing .NET prompt detector, circular-meter tracker, feedback controller, settings drawer, and Detection Review timeline. The activity picker does not change its engine timing or detector behavior.

