# CuePilot product backlog

This is the maintained, actionable work list. The completed 5.3.2–5.3.3
acceptance history is preserved in
[history/product-backlog-2026-09-04.md](history/product-backlog-2026-09-04.md).
The current checkout, installed build, and package state are recorded in
[HANDOFF.md](../HANDOFF.md).

## Open issues

Concrete items left open by the 5.3.7 - 5.3.18 batches and the 5.3.18 audit. Each names where the work
lands, so a session can start from the file rather than from a search.

| # | Issue | Where | Notes |
| --- | --- | --- | --- |
| 1 | Fishing tension sits low in the band (5.3.15 Phase A gate met; can close) | `src/Automation/AdaptiveRoutineEngine.cs` (`RegulateFishingMeter`), `src/Automation/FishingMeterDetector.cs` (`CaptureAndAnalyze`, `AnalyzeTrackedCrop`) | 5.3.15 was measured live on 2026-09-27 (5 sessions) against the Phase A gate in [history/perf-plan-2026-09-26.md](history/perf-plan-2026-09-26.md): capture ~1 ms (was 26.6 ms), tap→read 3 ms (was 75 ms), 12 catches to 1 failure. Caveats: most of the capture win is DXGI being healthy (full-frame captures cost 5-7 ms, against ~27 ms on GDI in the 5.3.14 session), and tap→read is optimistic as a control-latency number. Tension hugs the bottom of the band: 45-57% of fight samples sit below the 55% floor (median 55-56%). Open decision: the parked pulse-wait correction (about +8 ms retune) in that plan. Apply it only if long fights show trouble. |
| 2 | Mouse-hook shortcuts unverified in-game | `ui/src-tauri/src/mouse_shortcuts.rs` | Mouse 4 / Mouse 5 have never been pressed against the live hook (Playwright cannot send X buttons; middle click is covered). Behavior against FiveM is unknown, including whether the swallowed press stays swallowed. |
| 3 | Shortcut confirmations and 5.3.17 event popups unverified in-game | `ui/src-tauri/src/lib.rs` (`run_shortcut_command`), `notifications.rs` (`Tracker`, `confirm_shortcut`, `shortcut_failed`) | Tracker transitions are unit-tested; no real press, Pickpocket result, Fishing stop, focus-loss pause, or engine loss has been observed as a popup with FiveM running. The slide-out and in-place replacement animations have not been seen on screen either. |
| 4 | Notifications untested against a game window | `ui/src-tauri/src/notifications.rs` (`active_monitor`, `bounds`) | Popups have not been shown over live gameplay or exclusive fullscreen presentation. Click-through and non-activating behavior are asserted by design, not observed in-game. The 5.3.17 foreground-monitor placement and the four corners are unit-tested for geometry only; check them on a multi-monitor setup. |
| 5 | The ~5.3 s WebView gap is not explained | `ui/src-tauri/src/lib.rs` | Measurement narrowed it but did not close it. `builder_ready` lands at 1-2 ms, so everything CuePilot does itself costs ~2 ms and the whole delay is inside Tauri/WebView2 window creation. Ten launches spanned only 5275-5314 ms, which suggests a fixed timeout. A/B ruled out browser feature flags, background networking, and component update; the profile and runtime are both healthy. Next: read `plugins_ready` from `%LOCALAPPDATA%\CuePilot\diagnostics\shell.jsonl` to split plugin init from window creation, then test the surviving WPAD/proxy hypothesis with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--no-proxy-server`. |
| 6 | Installed UI never inspected | - | Release builds have no CDP, so an installed build can only be launch-checked. Visual confirmation of the neutral theme, the activity rail, the dark launch background, and the new launch splash in the installed build is outstanding. |
| 7 | Portable build stops spawning its sidecar after repeated launches | `ui/src-tauri/src/engine_bridge.rs` | A portable `cuepilot-ui.exe` launched several times in a row eventually logged only `velopack_done` and never started `CuePilot.exe --ui-bridge` (30 s timeouts). Not reproduced on the installed build, and not root-caused. |
| 8 | Launch splash has never been executed | `ui/src-tauri/src/splash.rs` | The window is new Win32/GDI code written and reviewed without ever running it, because no window was allowed on screen. It compiles clean under `clippy -D warnings` and its close paths were traced by hand, but nothing has confirmed it paints, that DPI scaling is right on a non-96-DPI display, or that it never outlives the real window. The 30 s lifetime timer bounds the worst case. First launch after repackaging settles it. |
| 9 | Pickpocket 5.3.18 busy-scene acquisition not confirmed in live play | `src/Automation/PickpocketDetector.cs` (`ScanStems`, `InspectStem`, `LocatePanel`), `PickpocketObserverEngine.cs` (`trackingHint`) | Fixed offline against 11,459 saved frames: per-pass header budget, wider and resolution-derived scale candidates for solid stems, tracked-row first, 400 ms tracking hint. Seen live once (one session, clean acquisition, 1 Missed on a thin target; not enough to close). It did not exercise the Hidden-frame history change. Live play must confirm it. Play a few pickpockets on grass and in pale clothing, then run `--analyze-pickpocket latest` (see [pickpocket-debugging.md](pickpocket-debugging.md)) and look for flicker, false attempt end, or never armed. One retained frame still reads Hidden cold (`20260925-010646-*rame-00220.png`). The old parked WIP diff `tmp/pickpocket-detector-wip-2026-09-23.diff` is superseded by this work and can be discarded when convenient. |
| 10 | Pickpocket 5.3.11 fixes not yet confirmed in live play | `src/Automation/PickpocketObserverEngine.cs`, `PickpocketAttemptTracker.cs` | Seen live once on 5.3.18 with no false attempt end or never-armed finding (n=1, stays open). Checks: fish, then pickpocket in the same app session (the report should show DXGI, not `Desktop GDI`); alt-tab mid-run and confirm it pauses and resumes; confirm no cooldown starts from scenery. The attempt tracker still treats 3 s of Hidden as the attempt ending; if the analyzer reports "False attempt end" after the 5.3.18 detector fixes, add a geometry guard there. If a run still fails, keep its diagnostic session. |
| 11 | Tray menu not proven by a real click | `ui/src-tauri/src/tray.rs` | Close-to-tray, relaunch-restore, and icon registration were checked live on 5.3.12. The menu itself was never observed: right-click the icon, confirm **Open CuePilot** and **Quit CuePilot** work, and that a right-click alone does not quit. |
| 12 | Thin Red/Yellow (4 px) targets miss | `src/Automation/PickpocketTimingPredictor.cs`, `PickpocketSweepTracker.cs` | About 77% overall on thin targets: end-to-end timing jitter of about 4-5 ms against a window of about 10-11 ms, most of it game-side and not reducible from the engine. Wide targets are reliable. The "0 of 4 since 5.3.15" split is not evidence of a regression: 5.3.15 changed no Pickpocket code (a rename only), the boundary was picked after seeing the data, and version labels span several binaries. A replay of 126 thin shots found no advance or calibrator change worth making. Next: collect 30-40 fresh thin-target sessions before touching advance constants, and record `appliedAdvanceMs` per shot (it is missing for most saved shots). |
| 13 | Test runs still write Pickpocket sessions into the real diagnostics folder | `tests/CuePilot.Tests/PickpocketRecordedEngineTests.cs`, `PickpocketPixelLatencyTests.cs`, `PickpocketInputTests.cs`, `PickpocketCaptureTimingTests.cs` | The observer tests in `PickpocketObserverTests.cs` now inject a temp evidence root, but these four still build `PickpocketObserverEngine` without `createDiagnostics`, so they fall through to `AppPaths.DiagnosticsDirectory`. `--analyze-pickpocket latest` can resolve to a test session after a `dotnet test`. Inject a temp root the same way. Live sessions have no retention limit; that is a separate decision. |
| 14 | Audit findings deliberately left unfixed | `AdaptiveRoutineEngine.cs`, `InputBackends.cs`, `ui/src-tauri/src/notifications.rs`, `update_service.rs`, `AppSettings.cs` | (a) After a Fishing Stop timeout the routine is Faulted while its worker may still hold the DXGI capture, so Pickpocket can start beside it: expose whether the worker is running and refuse starts while it is. (b) After the foreground wait the routine acts on a frame observed before the wait (up to ~10 s old): re-observe once the wait returns. (c) A notification shown before the overlay window is ready is lost. (d) Applying a downloaded update does not check that the engine is idle. (e) A backed-up or refused settings file gives the user no on-screen warning, only `settings.json.bak` and a save error. |

## Current priorities

1. **Confirm 5.3.18 Pickpocket acquisition in live play.** Run a few attempts,
   then `--analyze-pickpocket latest`. If a run still fails, keep its diagnostic
   session and read the analyzer verdict before changing detector thresholds.
2. **Complete only evidence-backed Pickpocket calibration.** Automatic
   wide-target delivery has been verified. Narrow-target behaviour still needs
   fresh live evidence before any timing adjustment; do not treat historical
   17 ms/14 ms Yellow notes as a current setting. The verified 2026-09-07
   snapshot used Yellow 12 ms and Red 8 ms; confirm later saved values from the
   selected attempt or Timing controls. 5.3.8 added per-run adaptive drift on
   thin targets — read the applied advance from the history entry, not the
   default.
3. **Preserve release confidence.** Any behavior change should run the narrow
   regression first, then the appropriate cross-layer checks described in
   [development.md](development.md). Packaging, publication, and update-feed
   changes remain separate work.

## Maintenance rules

- Keep current state in `HANDOFF.md` and intent here. The handoff records what
  is true; this file records what is next. Do not duplicate one into the other.
- Keep activity contracts in [activities.md](activities.md) and operational
  Pickpocket guidance in [pickpocket-debugging.md](pickpocket-debugging.md).
- Keep dated plans, completed acceptance criteria, and old measurements under
  `docs/history/`; link to them as context rather than presenting them as
  current work.
- Never delete diagnostic evidence through backlog work. Use the repository's
  explicit retention and cleanup workflow when deletion is authorized.
