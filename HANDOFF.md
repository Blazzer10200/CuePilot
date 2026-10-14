# Handoff — CuePilot 5.3.25 (local) — 2026-10-13

The snapshot is the current truth. Dated session records for 5.3.7 – 5.3.15 are
archived in [docs/history/handoff-batches-2026-09.md](docs/history/handoff-batches-2026-09.md);
user-facing changes are in [CHANGELOG.md](CHANGELOG.md).

## Session — 2026-10-13 (Pickpocket purple misses, auto-arm key; 5.3.24 + 5.3.25)

**Status: waiting on the user's live play.** 5.3.25 is installed locally (not pushed, not published). Branch `fix/pickpocket-arming` now carries 5.3.22 – 5.3.25.

### Diagnosis (live data, not assumed)
- 2026-10-13: 3 purples, 2 missed, all stopped +8.5 / +11.8 / +14.1 px late. Detection, arming and host timing were clean in all three (key-down 0.5-1.5 ms after plan, Windows saw Space 3.5-7.2 ms later, frame age 2.7-4 ms).
- Press -> marker-stop delay (computed per session from `trace.jsonl` + `REPORT.md`): ~13-14 ms on 10-11 17:04/17:17, **42-55 ms** tonight. Same pickpocket code (5.3.23 `src/` diff = `FishingMeterDetector.cs` only). The slowdown is game/system-side, same signature as 09-28/29. Running tonight: capturex, RTSS, MSI Afterburner, NVIDIA Broadcast, Discord — unconfirmed cause.
- CuePilot defect: `CalibrateWide` = median of last 10 wide shots, settled at ~11 ms from fast days, moved 11 -> 13 -> 17 ms across tonight's 3 shots (~5 more slow shots to catch up). Cap was 40 ms; tonight's last shot needed ~53.
- "Fix Fishing, break Pickpocket" was true once (09-28: `bbdbd78` removed the GPU boost for Fishing hitching, Pickpocket capture slowed, `dacc66a` re-added it Pickpocket-only). Not this week.
- Arming: 5.3.22 made the key an on/off switch with auto-arm on; a habit press at 21:29:55 left auto-arm off until 23:55. 23:55:43 rejection was FiveM still loading (title not set yet); resolved itself 14 s later, not a bug.

### Completed
- `189d671` 5.3.24: `PickpocketSessionState.CalibrateWide` — newest shot > `WideStepMs` (8 ms) off the median -> lead moves halfway; two in a row same side -> mean of the two. `WideMaximumLeadMs` 40 -> 60 (predictor uses the constant). `Describe()` names the step. Auto-arm key press while watching = keep (logged `kept`), Pause / Stop ends it; Observe still toggles. Replay on 68 wide shots since 09-30: 67 hits (old rule 66); tonight miss/hit/hit. New test `WideLeadReactsToAStepInGameDelayWithinOneShotButOnlyHalfwayForAOneOff`. `verify.ps1 -All` green: dotnet 459, vitest 54, Playwright 30, cargo 52.
- `18c2759` 5.3.25: that `kept` response carries `shortcutAction: "kept"` (`UiBridge.cs`); `notifications.rs` `Notice::shortcut` returns `None` for it, so repeated presses show no popup. Rust test added; dotnet pickpocket/bridge 250, cargo 52, clippy clean.
- Both packaged with `tauri:build` and installed `--silent`. 5.3.24 first launch hung at `plugins_ready` (backlog 5, second sighting); killed by PID, relaunch fine. 5.3.25 launched first time.

### Next (when the user returns)
1. Read new sessions: `shortcuts.jsonl` (expect `kept` lines, no `stopped` from habit presses), REPORT `Timing from history` line ("halfway to the last shot" / "following the last 2 shots"), and wide-shot offsets. Re-run the press->stop script idea (keyDownMs vs final stop) if purples still miss. Backlog 15 / 16.
2. If it holds: merge `fix/pickpocket-arming` -> `main`, push, tag v5.3.25 (user's OK).

### Key decisions
- Halfway on one outlier, full follow on two: a one-off hitch costs at most half its size (stays inside a 19 px purple), a real shift is followed by the second shot.
- Thin Red/Yellow calibration untouched (±10 ms clamp means it can't follow a 30 ms shift either; backlog 12).
- No CPU/GPU/RAM pre-check: the delay is inside the game and not visible in system stats; only the shot result measures it.

## Session — 2026-10-02 (Pickpocket arming fix, 5.3.21)

**Status: superseded.** 5.3.22 (auto-arm, bright-header detector) and 5.3.23 (Fishing tension-ring flicker) shipped locally after this without a HANDOFF update; see CHANGELOG.

### Completed (part 2)
- Flight recorder `e7c5749`: `src/Diagnostics/PickpocketFlightRecorder.cs` — `UiBridge` logs every shortcut press + what it did to `shortcuts.jsonl`; a run that ends without tapping writes a categorized record to `incidents.jsonl`; Hidden frames saved every 1 s for the first 30 s of a run (cap 30); `--analyze-pickpocket` shows the incident first. Both files rotate at 1 MiB.
- Release commit `0d2c374`: version 5.3.21 in all six files, CHANGELOG entry. Gates green except one known-flaky Fishing timing test under full-suite load (passes 3/3 alone).
- Packaged + installed `release/velopack/CuePilotDesktop-win-Setup.exe` `--silent`. Single installed entry 5.3.21; old 5.3.20 replaced. First launch hung in WebView creation (backlog 5); relaunch from the shortcut started fine (setup_begin 5337 ms, engine sidecar up).
- Dev background cleaned: .NET build servers shut down; no dev servers running.

### Next (when the user returns)
1. Read `%LOCALAPPDATA%\CuePilot\diagnostics\shortcuts.jsonl` + `incidents.jsonl`, then `--analyze-pickpocket` on each new session. Backlog 15.
2. If it holds up: merge `fix/pickpocket-arming` → `main`, push, tag v5.3.21 (needs the user's OK).

### Completed (part 1)
- Analyzed 26 live 5.3.20 sessions (copies in `tmp/corpus-live/`, originals untouched) + full history scan (`tmp/all-active-sessions.txt`). **Detection 23/23** on panels that appeared; 18 Grabbed / 5 Missed, all misses thin Red (4 px), 4 of 5 landed 3-7 px late. Full history: 20 of 335 automatic runs never tapped, 0 since 5.3.18; causes = manual Space (11), never saw Preparing, GDI fallback (pre-5.3.11).
- Root cause of "pressed F7, it never tried": F7 toggled (`UiBridge.cs` toggle case) and one-tap runs never ended after their attempt, so the next press stopped the run; plus input required the ~250 ms Preparing fade-in (sampled every ~67 ms), so late arming or alt-tab forfeited the minigame.
- Fix committed `186c5ff` on `fix/pickpocket-arming` (branched from `main`): one-tap runs end after attempt completion (`PickpocketObserverEngine.cs`, `attemptFinished`), F7 in one-tap modes always (re)starts an armed run (Observe still toggles; Pause stops), 3 header-verified Active frames (`HeaderlessFrames == 0`) arm like Preparing. Popup/Settings/docs text updated. `verify.ps1 -All` green (dotnet 444, vitest 54, Playwright 30, cargo 52, clippy/fmt).

### Key Decisions
- F7 = always arm, not toggle, in one-tap modes — user's 5.3.20 habit is F7 start → F7 stop seconds after result; auto-end alone would just shift the toggle by one press.
- Header-less continuation frames never arm — keeps false-positive protection the Preparing gate gave.

- No "mark failure" hotkey: runs that never fire write an incident automatically instead (no new cross-layer command, lower load).
- Local install instead of publishing: publishing needs the user's explicit OK.

### Notes
- Thin Red timing (backlog 12) stays separate; 9 fresh shots is too few to retune.

### Gotchas
- `chore/harness-audit-20260929` holds unmerged `759947b` (Cargo.toml only) — the fix branch does not include it.
- Run the analyzer via `dotnet run --project CuePilot.csproj -c Release --no-build -- --analyze-pickpocket <dir>`; the dll path under `bin/Release/net8.0-windows/` does not exist (real TFM is `net8.0-windows10.0.19041.0/win-x64`).

---

## Snapshot

| | |
| --- | --- |
| Branch | `fix/pickpocket-arming` (5.3.21 – 5.3.25, all unpushed) off `main`; `main` is the released 5.3.20 line |
| Source version | **5.3.25** in all six release files on the fix branch; `main` is still 5.3.20 |
| Installed locally (current) | **5.3.25** local build from `release/velopack/CuePilotDesktop-win-Setup.exe`, installed `--silent` 2026-10-13; replaced 5.3.24 (same evening), which replaced 5.3.23. Not the CI build |
| Published | **v5.3.20** is the public release (2026-09-29, tag on `fb19e1d`; release run passed first try). 5.3.19 is the previous one |
| Previously installed | **5.3.20** (replaced 2026-10-02) from the official GitHub `CuePilotDesktop-win-Setup.exe` (sha256 `2637d382…fd28`, matched the published `.sha256`), installed `--silent` 2026-09-29 and launched; `shell.jsonl` shows `shellVersion` 5.3.20 |
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
