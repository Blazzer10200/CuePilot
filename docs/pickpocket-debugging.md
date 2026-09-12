# Pickpocket debugging

This is the maintained operating guide for Pickpocket. The engine records
bounded local diagnostics whenever an F7 run starts. Observe is the default;
automatic modes are explicit, one-tap calibration tools. A sent key is not by
itself proof that the game accepted the action.

The 2026-09-07 verified snapshot used Yellow 12 ms and Red 8 ms. Those values
describe that checked build and session, not permanent preferences: confirm the
saved Timing values and the selected attempt's report before drawing conclusions
from a later run. The 5.3.5 detector recovery, retained in 5.3.6, fixes the recorded
active-grass false-Hidden case. Check `HANDOFF.md` for the build and the latest
investigation before changing settings.

## Run controls

- **Run mode:** `Manual · observe only` never sends Space. `Automatic · wide
  targets` and `Precision · all target sizes` can arm at most one owned Space
  tap per new run. Selecting a mode or policy alone does not start capture or
  arm input.
- **Target policy:** `Widest` chooses by width. Color and priority policies
  choose the best detected color first, then use the applicable ranking and
  width. Unknown card names are not invented.
- **Timing:** Red and Yellow accept independent values from 0 through 20 ms.
  A larger advance schedules the planned press earlier. Change timing only
  against a retained diagnostic session; do not infer game-receipt latency from
  a result-frame offset.
- **Safety:** F7 starts or stops a Pickpocket run. A new run must see Preparing
  before input can arm. Focus loss, a manual Space press, Stop, an error, or a
  cooldown closes the input gate. Pause / Break is the emergency stop and
  releases a Space key CuePilot owns.

## History and diagnostics

The **History** tab retains up to 1,000 completed attempts across restarts and
shows five attempts per page. Filter by outcome, select an attempt, then copy
its report or open its linked evidence when available. It preserves the timing,
mode, target policy, build identity, and result information recorded for that
attempt. The compact result panel in Live is only a recent-attempt view; use
History for the retained record.

The Diagnostics view identifies observer state, capture and analysis duration,
image age, marker speed, candidate count, manual Space observations, and the
one-tap counter. Its `Saved`, `Limited`, and `Error` states matter: preserve a
limited or failed session before rerunning, because missing frames cannot be
reconstructed later. Use **Open logs** to open the local evidence directory.

## Failure triage

1. Stop the run with F7 or Pause / Break. Do not change timing while a run is
   active.
2. Open the saved evidence and establish the observed sequence: Preparing,
   Active, predicted candidate or rejection reason, key count, and final
   Grabbed/Missed/Ended state.
3. For a classification failure, retain one decisive active frame and a nearby
   no-minigame frame. For an input or timing failure, retain the trace, selected
   attempt report, and result frame. Treat a result-frame marker position as
   visual evidence only.
4. Replay the saved fixture before modifying detector thresholds or timing. Add
   a regression only when the retained evidence gives a stable expected state.
5. Read `HANDOFF.md` before a new live calibration run. It distinguishes the
   latest verified result from historical proposals and records applicable
   release checks.

## Offline replay

Replay reads images and reports hypothetical scheduling only; it never creates
an input sender or changes an observed result.

```powershell
dotnet run --project .\CuePilot.csproj -- --replay-pickpocket <manifest.json> --target-color Yellow --advance-ms 12
pwsh -NoProfile -File .\scripts\benchmark-pickpocket.ps1 -Manifest <manifest.json>
```

`--target-color` accepts `White`, `Purple`, `Red`, `PaleGreen`, `Blue`, or
`Yellow`; `--advance-ms` accepts 0 through 20. The manifest must have strictly
increasing presentation timestamps. Replay output labels its 0–16 ms simulated
delivery envelope and reports detector timing, state mismatches, and at most one
hypothetical press. It is evidence for detector and prediction behavior, not
proof of live input acceptance.

## Historical material

The detailed 2026-09-04 calibration chronology, including session identifiers,
older timing proposals, and contemporaneous test counts, is preserved in
[history/pickpocket-calibration-2026-09-04.md](history/pickpocket-calibration-2026-09-04.md).
Read [pickpocket-evidence.md](pickpocket-evidence.md) for the dated clip and
live-session archive, and
[history/pickpocket-plan-2026-09-03.md](history/pickpocket-plan-2026-09-03.md)
for the original implementation plan.
