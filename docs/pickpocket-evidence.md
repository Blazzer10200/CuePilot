# Pickpocket evidence catalog

What has been recorded, where it lives, and what it does and does not prove.
Current operating guidance is in [pickpocket-debugging.md](pickpocket-debugging.md).
The full dated run-by-run record, with every measurement and validation count, is
archived in [history/pickpocket-evidence-2026-09-03.md](history/pickpocket-evidence-2026-09-03.md).

## Durable facts

- Item identities, colors, order, and spacing are randomized on every attempt.
  Never treat one clip's layout or coordinates as fixed.
- The moving marker is a lime-green stem with arrowheads above and below the bar;
  its arrowheads distinguish it from the pale-green item band.
- The marker bounces at the bar edge instead of wrapping. A return pass is part
  of the same attempt, not a new one.
- Observed speeds range from about 280 to 400 px/s across clips and live runs.
  Speed is measured per attempt, never assumed.
- Narrow bands (Red, Yellow) can be 3–5 px, roughly 10–14 ms at observed speeds,
  which is shorter than one 60 fps frame.
- Visible end states: `GRABBED / <ITEM>`, `MISSED` (bar turns red), and
  `TOO SLOW`. The Space prompt stays visible during the result, so it is not an
  active-state signal. Grabbed and Missed start the engine's 180-second cooldown.
- Preparing-phase pixels over bright scenery can look like a white band. Targets
  are confirmed only during Active play, across three fresh samples.

## Recorded sources

| Date | Source | What it shows |
| --- | --- | --- |
| 2026-09-03 | Clip 1 (`clip-1788472296181…`) | Manual Luxury Watch grab |
| 2026-09-03 | Clip 2 (`clip-1788474408979…`) | Missed attempt, edge bounce |
| 2026-09-03 | Clip 3 (`clip-1788476264336…`) | Broken Electronic Part grab, marker fringe split |
| 2026-09-03 | Live runs 1–4 | Manual grabs, Too Slow, a Preparing-phase false target |
| 2026-09-04 | Live run 5 | Detector stall over bright scenery (fixed) |
| 2026-09-04 | Session `20260904-024532…` | First verified automatic grab (Rope, one native Space down/up) |

Clips live under `C:/Users/BLAZZER/Videos/Clips/FiveM_b3258_GTAProcess/`.
Regression frames and manifests are under
`tests/CuePilot.Tests/Fixtures/Pickpocket/` (`clip2*`, `clip3*`, `Live/`); see the
[fixture guide](../tests/CuePilot.Tests/Fixtures/Pickpocket/README.md). Local
analysis output under `tmp/pickpocket-*` is not committed.

## Limits

- Replays and synthetic sequences exercise the real detector, predictor, and
  input path, but they do not measure game-side input acceptance.
- The wide Rope grab proves automatic delivery works. It does not bound game
  input latency tightly enough to guarantee the narrow Red or Yellow bands.
- Named item recognition only covers the labels that have been recorded; unknown
  labels stay unknown.
