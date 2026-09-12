# CuePilot product backlog

This is the maintained, actionable work list. The completed 5.3.2–5.3.3
acceptance history is preserved in
[history/product-backlog-2026-09-04.md](history/product-backlog-2026-09-04.md).
The current release and investigation state are recorded in `HANDOFF.md`.

## Current priorities

1. **Use new Pickpocket failures as evidence.** The locally installed 5.3.6 build
   retains the 5.3.5 fix for the recorded active-grass false-Hidden classification.
   If another run fails, preserve its diagnostic session and inspect that
   evidence before changing timing or detector thresholds.
2. **Complete only evidence-backed Pickpocket calibration.** Automatic
   wide-target delivery has been verified. Narrow-target behaviour still needs
   fresh live evidence before any timing adjustment; do not treat historical
   17 ms/14 ms Yellow notes as a current setting. The verified 2026-09-07
   snapshot used Yellow 12 ms and Red 8 ms; confirm later saved values from the
   selected attempt or Timing controls.
3. **Keep Lockpicking observe-only until its evidence bundle is complete.**
   Capture successful and failed full attempts across backgrounds, including
   input cadence, before proposing any automatic control.
4. **Preserve release confidence.** Any behavior change should run the narrow
   regression first, then the appropriate cross-layer checks described in
   [development.md](development.md). Packaging, publication, and update-feed
   changes remain separate work.

## Maintenance rules

- Keep current instructions in `HANDOFF.md`, activity contracts in
  [activities.md](activities.md), and operational Pickpocket guidance in
  [pickpocket-debugging.md](pickpocket-debugging.md).
- Keep dated plans, completed acceptance criteria, and old measurements under
  `docs/history/`; link to them as context rather than presenting them as
  current work.
- Never delete diagnostic evidence through backlog work. Use the repository's
  explicit retention and cleanup workflow when deletion is authorized.
