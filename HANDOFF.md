# Handoff — CuePilot 5.3.20 — 2026-09-29

The snapshot is the current truth. Dated session records for 5.3.7 – 5.3.15 are
archived in [docs/history/handoff-batches-2026-09.md](docs/history/handoff-batches-2026-09.md);
user-facing changes are in [CHANGELOG.md](CHANGELOG.md).

## Snapshot

| | |
| --- | --- |
| Branch | `main` (pushed directly, no PR). The audit fixes were fast-forward merged from `audit/fix-now-5.3.18` (branch left in place, fully merged) |
| Source version | **5.3.20** in all six release files |
| Published | **v5.3.20** is the public release (2026-09-29, tag on `fb19e1d`; release run passed first try). 5.3.19 is the previous one |
| Installed locally | **5.3.20** from the official GitHub `CuePilotDesktop-win-Setup.exe` (sha256 `2637d382…fd28`, matched the published `.sha256`), installed `--silent` 2026-09-29 and launched; `shell.jsonl` shows `shellVersion` 5.3.20 |
| Package | `release/velopack/` holds 5.3.17 – 5.3.19 packages, Setup.exe, Portable.zip (local build; the official CI build differs byte-for-byte). Never delete the directory to clean up a build |
| Last gates | 2026-09-29 `verify.ps1 -All` on 5.3.20: dotnet 442, vitest, Playwright, cargo 52, svelte-check, clippy all green |
| Release CI | The tag run failed twice on runner flakes before passing on the third attempt: a 100 ms wall-clock assertion in `PickpocketPixelLatencyTests` (109.6 ms on the runner, now best-of-three) and `net::ERR_NO_BUFFER_SPACE` from Playwright's `page.goto`. Rerun with `gh run rerun <id> --failed` before assuming a real regression |

Orientation in one command: `pwsh -NoProfile -File scripts/project-status.ps1`.

## What 5.3.18 shipped

Pickpocket detection rework, worked entirely offline from saved sessions (no live play was available). Details in [CHANGELOG.md](CHANGELOG.md).

- **Detector fixes** (`PickpocketDetector.cs`): each stem pass gets its own header budget (a busy scene no longer starves the real marker); the scale search tolerates grass-inflated stems; one Hidden frame no longer wipes the tracked history (`PickpocketObserverEngine` keeps a 400 ms tracking hint).
- **Session analyzer** (`Diagnostics/PickpocketSessionAnalyzer.cs`): `--analyze-pickpocket <session|latest>` writes a plain-language `ANALYSIS.md` verdict; `--pickpocket-corpus <folder>` scores every saved session. See [docs/pickpocket-debugging.md](docs/pickpocket-debugging.md).
- **Offline result across 11,459 saved frames:** 43 of 44 lost frames recovered, 1 cold miss, 1 minor regression, 6 borderline false-positive candidates (header 0.81–0.85). Analysis p50 1.0 ms, p99 14.5 ms.

## What 5.3.20 shipped

Pickpocket wide-target timing. Shot history showed Purple/wide shots stopping +2 to +6 px past center all month, then +8 to +12 px on 09-28 and 09-29 (a 19 px target missed at +11.5): the game/system input delay grew (capture ruled out: steady 60 Hz, key delivery 0.2-2.6 ms, aim-to-stop rose from ~19 to ~27 ms), and only thin Red/Yellow had any correction. Now `PickpocketSessionState.CalibrateWide()` derives a 0-40 ms lead from the last ten wide shots (median of applied lead + offset/speed; needs 3), passed to `PickpocketTimingPredictor.Observe(wideLeadMs:)`, saved per shot as `appliedLeadMs`. Thin Red/Yellow shift cap raised 6 to 10 ms. **Not proven in game yet:** play a few pickpockets and read the Timing view line "wide targets N ms lead" plus offsets in history; they should center near 0. Suspected cause of the slowdown is environmental (capturex clipping software since 09-28, RTSS, NVIDIA Broadcast), unconfirmed.

## What 5.3.19 shipped

A read-only audit of 5.3.18 followed by 17 fix batches, all gated together and independently reviewed before release. User-facing detail is in [CHANGELOG.md](CHANGELOG.md).

- **Engine:** settings saves validate and write from a copy (a rejected save no longer half-applies), a corrupt or newer `settings.json` is backed up instead of overwritten, arming Fishing is exception-safe, and Fishing diagnostic logs are size-capped and fail soft.
- **Shell:** hotkey capture times out after 30 s so an interrupted capture cannot leave Pause released; the updater has check (90 s) and no-progress (180 s) timeouts; support reports redact the profile path and `shell.jsonl` rotates at 1 MiB; notification settings write atomically.
- **UI:** the Pickpocket target selection effect no longer loops, hotkey capture rejects Tab/Enter/arrows, and there is one focus ring.
- **Tests and tooling:** bridge-contract test cross-checks command names against `UiBridge.cs`; `verify.ps1 -All` now checks version sync.
- **Not fixed, on purpose:** items needing live play or a decision are recorded in backlog items 13 and 14. The audit report itself lived only in the session scratchpad and is not committed.
- **Known low-severity edge:** if a hotkey field sits in capture for over 30 s while Settings stays focused, the current Start/Stop key re-arms and fires instead of being captured. Pause stays registered, and the alternative (hotkeys released forever) was worse. Fix later by having the UI cancel capture when the shell reports the timeout.
- **Untracked placeholder:** an empty gitignored `ui/src-tauri/resources/engine/CuePilot.exe` exists in the working tree (created to satisfy `tauri-build`; staging overwrites it). Safe to delete when convenient.

## Carried over

- **Not proven in game (top priority):** the 5.3.18 detector work has been seen in one live session (`20260929-103311`, PrecisionAttempt): acquisition, flicker, false attempt end and never-armed all passed; the shot Missed by about 2 px on a 4 px sliver, which is timing noise. That session did not exercise the Hidden-frame history change. Next: a few more real pickpockets, `--analyze-pickpocket latest`, read the verdict.
- **Thin Red/Yellow timing** (~77% hit rate, 0 of 4 since 5.3.15) is untouched. 5.3.15 contained no Pickpocket code change (a rename of `PickpocketSampleClock` only), so that split is not a regression signal. See backlog.
- **Diagnostics folder:** 301 of 662 saved Pickpocket sessions are test output (about 130 MB of 8.77 GB; live play is the rest and has no retention). Two observer tests were the cause and now inject a temp root; four other test classes still write there (backlog 13).
- **Attempt tracker:** the 3 s Hidden guard in `PickpocketAttemptTracker` changes only if the analyzer reports a "false attempt end" on live data.
- **5.3.17 popups** were never viewed on screen. If a card looks wrong, start with `overlay.css`.
- **Fishing park gate:** Phase A from 5.3.15 was measured live on 2026-09-27 and met (12 catches to 1 failure, capture about 1 ms, tap to read 3 ms); most of the capture win is DXGI health. See backlog 1 and [the perf plan](docs/history/perf-plan-2026-09-26.md).
- **Tray:** confirm a real right-click then Quit works, and a plain right-click alone does not quit.
- **Manual scripts:** `scripts/build-brand-assets.ps1`, `test-velopack-update.ps1` and `benchmark-pickpocket.ps1` are kept as manual tools.

## What is open

Actionable work lives in [docs/product-backlog.md](docs/product-backlog.md)
under **Open issues**. This file records state; the backlog records intent.

## Canonical commands

```powershell
pwsh -NoProfile -File scripts/project-status.ps1
pwsh -NoProfile -File scripts/verify.ps1 -All
npm --prefix ui run tauri:build          # Velopack package into release/velopack/
```

```bash
npm --prefix ui run cdp:dev      # inspectable dev app (long-lived)
npm --prefix ui run cdp:serve    # CDP HTTP wrapper (long-lived)
bash ui/scripts/cdp/c.sh state   # engine + UI state as text
```
