# CuePilot product backlog

This is the maintained, actionable work list. The completed 5.3.2–5.3.3
acceptance history is preserved in
[history/product-backlog-2026-09-04.md](history/product-backlog-2026-09-04.md).
The current checkout, installed build, and package state are recorded in
[HANDOFF.md](../HANDOFF.md).

## Open issues

Concrete items left open by the 5.3.7 - 5.3.11 batches. Each names where the work
lands, so a session can start from the file rather than from a search.

| # | Issue | Where | Notes |
| --- | --- | --- | --- |
| 1 | Fishing fix not yet confirmed in live play | `src/Capture/DxgiFrameSource.cs`, `src/Automation/FishingMeterDetector.cs` (`FishingTensionController`) | The 150 ms+ capture stall was measured from a real session but not reproduced during the probes, because game load was light. Fish under normal load and read `captureMilliseconds` in the Fishing debug session (or the status line). Healthy is well under 40 ms; a controller `cadenceScale` above 1 means capture is still slow. Re-picking the FiveM target also refreshes the stale saved process id. |
| 2 | Mouse-hook shortcuts unverified in-game | `ui/src-tauri/src/mouse_shortcuts.rs` | Mouse 4 / Mouse 5 have never been pressed against the live hook (Playwright cannot send X buttons; middle click is covered). Behavior against FiveM is unknown, including whether the swallowed press stays swallowed. |
| 3 | Pickpocket shortcut confirmation unverified in-game | `ui/src-tauri/src/lib.rs` (`run_shortcut_command`), `notifications.rs` (`confirm_shortcut`) | Only the preview path was screenshot-checked. A real press with FiveM running has not been observed. |
| 4 | Notifications untested against a game window | `ui/src-tauri/src/notifications.rs` | Popups have not been shown over live gameplay or exclusive fullscreen presentation. Click-through and non-activating behavior are asserted by design, not observed in-game. |
| 5 | The ~5.3 s WebView gap is not explained | `ui/src-tauri/src/lib.rs` | Measurement narrowed it but did not close it. `builder_ready` lands at 1-2 ms, so everything CuePilot does itself costs ~2 ms and the whole delay is inside Tauri/WebView2 window creation. Ten launches spanned only 5275-5314 ms, which suggests a fixed timeout. A/B ruled out browser feature flags, background networking, and component update; the profile and runtime are both healthy. Next: read `plugins_ready` from `%LOCALAPPDATA%\CuePilot\diagnostics\shell.jsonl` to split plugin init from window creation, then test the surviving WPAD/proxy hypothesis with `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--no-proxy-server`. |
| 6 | Installed UI never inspected | - | Release builds have no CDP, so an installed build can only be launch-checked. Visual confirmation of the neutral theme, the activity rail, the dark launch background, and the new launch splash in the installed build is outstanding. |
| 7 | Portable build stops spawning its sidecar after repeated launches | `ui/src-tauri/src/engine_bridge.rs` | A portable `cuepilot-ui.exe` launched several times in a row eventually logged only `velopack_done` and never started `CuePilot.exe --ui-bridge` (30 s timeouts). Not reproduced on the installed build, and not root-caused. |
| 8 | Launch splash has never been executed | `ui/src-tauri/src/splash.rs` | The window is new Win32/GDI code written and reviewed without ever running it, because no window was allowed on screen. It compiles clean under `clippy -D warnings` and its close paths were traced by hand, but nothing has confirmed it paints, that DPI scaling is right on a non-96-DPI display, or that it never outlives the real window. The 30 s lifetime timer bounds the worst case. First launch after repackaging settles it. |
| 9 | Pickpocket detector misreads panels over grass | `src/Automation/PickpocketDetector.cs` (`ScanStems`) | Two retained frames fail: `%LOCALAPPDATA%\CuePilot\diagnostics\pickpocket\20260923-050613-*\frame-00077.png` is a live panel with the marker at the right edge over grass, read as Hidden; `20260923-084416-*\frame-00259.png` is grass with no panel, read as Missed. Stems are rejected when too long or when grass on both sides reads as marker at x±6. 5.3.11 made the tracker tolerate both (3 s disappearance, result needs Active), but the detector itself is unchanged. An unverified WIP diff (a `clippedAtTop` stem heuristic plus `PP_DEBUG` logging to strip) is parked at `tmp/pickpocket-detector-wip-2026-09-23.diff`. Add both frames as fixtures before touching thresholds. |
| 10 | Pickpocket 5.3.11 fixes not yet confirmed in live play | `src/Automation/PickpocketObserverEngine.cs`, `PickpocketAttemptTracker.cs` | Checks: fish, then pickpocket in the same app session (the report should show DXGI, not `Desktop GDI`); alt-tab mid-run and confirm it pauses and resumes; confirm no cooldown starts from scenery. If a run still fails, keep its diagnostic session. |
| 11 | Tray menu not proven by a real click | `ui/src-tauri/src/tray.rs` | Close-to-tray, relaunch-restore, and icon registration were checked live on 5.3.12. The menu itself was never observed: right-click the icon, confirm **Open CuePilot** and **Quit CuePilot** work, and that a right-click alone does not quit. |

## Current priorities

1. **Use new Pickpocket failures as evidence.** The locally installed build
   retains the 5.3.5 fix for the recorded active-grass false-Hidden classification.
   If another run fails, preserve its diagnostic session and inspect that
   evidence before changing timing or detector thresholds.
2. **Complete only evidence-backed Pickpocket calibration.** Automatic
   wide-target delivery has been verified. Narrow-target behaviour still needs
   fresh live evidence before any timing adjustment; do not treat historical
   17 ms/14 ms Yellow notes as a current setting. The verified 2026-09-07
   snapshot used Yellow 12 ms and Red 8 ms; confirm later saved values from the
   selected attempt or Timing controls. 5.3.8 added per-run adaptive drift on
   thin targets — read the applied advance from the history entry, not the
   default.
3. **Keep Lockpicking observe-only until its evidence bundle is complete.**
   Capture successful and failed full attempts across backgrounds, including
   input cadence, before proposing any automatic control. The checklist is in
   [activities.md](activities.md).
4. **Preserve release confidence.** Any behavior change should run the narrow
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
