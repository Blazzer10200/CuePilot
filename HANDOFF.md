# Handoff — CuePilot 5.3.15 — 2026-09-26

The snapshot is the current truth. Dated session records for 5.3.7 – 5.3.9 are
archived in [docs/history/handoff-batches-2026-09.md](docs/history/handoff-batches-2026-09.md);
user-facing changes are in [CHANGELOG.md](CHANGELOG.md).

## Snapshot

| | |
| --- | --- |
| Branch | `codex/release-complete` |
| Source version | **5.3.15**, synchronized across all six release files (`1ee5efd`). 5.3.13 and 5.3.14 were packaged locally and 5.3.14 installed; neither was tagged. 5.3.15 is the release candidate |
| Published | **v5.3.12 public**, 2026-09-25 00:11 UTC, marked Latest: [release](https://github.com/Blazzer10200/CuePilot/releases/tag/v5.3.12), run 36075163572 green (8 assets; `releases.win.json` lists 5.3.12). Previous public: v5.3.10 |
| Installed locally | **5.3.14** (Velopack, per `project-status.ps1` on 2026-09-26); it was fishing at the park while 5.3.15 was built, so 5.3.15 was **not** installed over it. The in-app updater will offer 5.3.15 once published. 5.3.12 notes: the first shortcut launch stalled before `setup_begin` (the 5.3.3 hidden-launch stall); stopping by exact install path and relaunching through Explorer recovered it; close-to-tray, tray registration, and relaunch-restore were checked on that build |
| Package | `release/velopack/` — 5.3.12 built 2026-09-24 18:53: Setup 45.7 MB, full 41.4 MB, delta from 5.3.11 |
| Last gates | 5.3.15 on 2026-09-26: docs gate green, dotnet **463**, vitest 51, svelte-check 0/0, clippy clean, cargo 30. **Playwright not run** (owner was in game; e2e would steal focus). 5.3.12 full gate on 2026-09-24 was the last `-All` run |

`release/velopack/` also holds `publication-receipt.json` and
`verification-receipt.json` from the published v5.3.4. Packaging does not
regenerate them, so never delete that directory to clean up a build.

Orientation in one command: `pwsh -NoProfile -File scripts/project-status.ps1`.

## 5.3.15 — Fishing performance plan executed (local build)

Plan, evidence, per-item status and the measurement correction live in
[docs/history/perf-plan-2026-09-26.md](docs/history/perf-plan-2026-09-26.md)
(execution log at the bottom). Commits: `63c0a73` (Phase A), `7f0074b`
(B1 + B7), `7b72f72` (C1–C6), `1ee5efd` (bump + CHANGELOG). 5.3.13 and 5.3.14
(DXGI readback bound, GPU-busy backoff) landed earlier today and are only in
CHANGELOG.

- **A — park fight loop.** Once the tracker is locked, `CaptureMeter(trackedRegionOnly: true)`
  captures only the inflated tracked region and `AnalyzeTrackedCrop` reads it in window
  coordinates; a miss falls back to the full frame so the LMB identity gate is untouched.
  Samples wait on `HighResolutionSampleClock.WaitUntil(deadline)` (renamed from
  `PickpocketSampleClock`, same class). Debug session logs `elevated`/pid, `gpuPriority`,
  a one-time `capture_ready`, per-sample `frameOrigin`; manifest rewrite throttled to 250 ms.
- **B1** early `Missing` in `AnalyzeCenter` when `darkness < 0.56 && diskContrast < 0.12`.
  **B7** `FishingMeterDetector.Analyze(bitmap, region, …)` reads the region via `LockBits`
  (row-by-row copy) instead of `Bitmap.Clone`; `RegionAnalysisMatchesTheClonedCropExactly`
  proves identical results.
- **Measurement correction (load-bearing for future perf work):** the "~40 ms per region"
  in the test host was GDI+ re-decoding the PNG fixture on every `Clone` of a file-backed
  bitmap. In-memory frames (what live capture makes): tracked hit 0.3–2 ms, full no-meter
  cascade 11–27 ms, prompt detector 6–19 ms. Per-sample compute in the fight loop is a few
  ms; the rest is capture plus the deliberate sample deadline. Do not chase detector CPU again
  without an in-memory probe.
- **Rejected:** B2 (bypasses LMB gate on fresh lock), B4 (observe-only, traces change,
  `WaitUntil` blocks async), B6 (prompt roll is intentionally continuous). **Deferred:** B3
  (prompt scale cache; not worth the stability-gate risk at 6–19 ms), B5 (needs prompt-location
  logging), C7 (pump coupled to `visible_until`).
- **Pulse wait** (`AdaptiveRoutineEngine.cs` ~line 730 `WaitOne(decision.PulseMilliseconds)`)
  deliberately still uses the coarse timer: switching it shortens real pulses by ~8 ms and
  needs the owner to re-tune the pulse range. Parked with the HAGS experiment.

**Next:** one live fish at the park on a 5.3.15 build, then the three `jq` lines in the
plan's Phase A gate (capture median < 12 ms, tap→read median < 40 ms, catches ≥ failures).
Then package/tag only when the owner asks.

## 5.3.12 — Runs in the background from the tray

The owner asked for CuePilot to behave like their other apps: closing the
window keeps it running in the tray.

- `lib.rs` `on_window_event`: `CloseRequested` on `main` calls
  `prevent_close()` and hides. This covers the title-bar X (JS
  `getCurrentWindow().close()`), Alt+F4, and the taskbar. The engine, running
  activity, global shortcuts, mouse hook, and notification pump all keep
  going because the `main` window still exists.
- `tray.rs`: icon with the window icon, tooltip `CuePilot` (`CuePilot Dev`
  for the dev identifier). Left click or **Open CuePilot** →
  `focus_main_window`; **Quit CuePilot** → `quit_application` (engine
  `shutdown`, then `app.exit(0)`, the same order as the updater). The existing
  single-instance handler already restores a hidden window on relaunch.
- `notifications.rs`: `announce_background` queues one silent "Still running
  in the tray" popup on the first hide per launch (`Notice.silent` skips the
  chime and is not serialized). The overlay shows a `Minimize2` icon for it.
- `Cargo.toml`: `tauri` gains the `tray-icon` feature.

Verified live in the dev app (2026-09-24): Close hid the main window while the
shell and engine stayed alive and the engine stayed connected; the
notification window became visible; a second launch restored the window and
exited; Windows registered the icon (`NotifyIconSettings` → tooltip
`CuePilot Dev`). **The tray menu items were not individually proven**: a
synthetic right-click through UI Automation ended with the dev app exiting
cleanly (engine stopped, no panic), which points to Quit firing, but the menu
was never observed and the owner stopped the check. Try a real right-click →
Quit on the installed 5.3.12, and confirm a plain right-click alone does not
quit.

## 5.3.11 — Pickpocket no longer silently skips attempts

Committed as `5dfa378` but never tagged; it ships inside 5.3.12.

The owner reported Pickpocket "sometimes just not register at all", suspecting
other hotkeys or alt-tabbing. The retained sessions under
`%LOCALAPPDATA%\CuePilot\diagnostics\pickpocket\` showed four separate causes:

- **Capture held by Fishing (the 09:40Z session, also 2026-09-07).** DXGI
  allows one duplication per output per process. A Fishing run earlier in the
  same app process kept its `DxgiFrameSource` alive, so every Pickpocket frame
  failed `DuplicateOutput` with `E_INVALIDARG` and fell back to GDI. GDI frames
  have no presentation timestamp, so no tap could ever be scheduled. Fix:
  `AdaptiveRoutineEngine` and `LockpickingObserverEngine` dispose their frame
  source when a run ends.
- **Scenery read as a result (08:44Z).** Grass beside a car classified as
  Missed with no minigame on screen and started a false 3-minute cooldown,
  which then blocked real attempts (and persisted across restart). Fix:
  `PickpocketAttemptTracker` only completes after an Active frame was seen.
- **Brief dropout ended the attempt (05:06Z).** A few Hidden frames mid-minigame
  completed the attempt and started cooldown. Fix: the panel must be missing
  for 3 s, except after the run's tap is spent, where the old three-frame rule
  still applies (the recorded `third-attempt.json` replay depends on it).
- **Alt-tab killed the run (05:06, 05:11, 08:59).** Foreground loss threw
  "lost focus". Now the observer pauses: it releases owned input, clears
  prediction and tracking, shows "FiveM is not in front", and resumes on
  return, requiring a fresh Preparing state before input can arm.

Seen but deliberately **not** changed: at 05:06 and 05:11 capture was 33–48 ms
with frames 50–60 ms old under GPU load, so the ≤40 ms age and ≤60 ms
contiguity gates correctly refused to predict. At 05:11 a manual Space press
disarmed the run, which is intended safety.

**Not yet proven live.** Unit and recorded-engine tests cover each fix; no
in-game Pickpocket run has been made on 5.3.11. The grass detector
misclassification is still open (backlog issue 9).

## 5.3.10 — Fishing rhythm restored

The owner reported Fishing "just doing a tap ever so often" and losing fish.
Root cause, measured from the Fishing debug session: the time from the end of
one pulse to the next meter read had grown from about 54 ms to about 194 ms,
while pulses stayed 35–90 ms, so tension starved between taps. The controller
tuning was never the problem and was not changed.

What was slow, and the fix for each:

- **Capture.** `DxgiFrameSource` copied the whole desktop into a staging texture
  every sample. Under a GPU-bound game that copy queued behind the game's frames
  (steady-state probes ranged from 6 ms to 95 ms median with FiveM at 88% GPU).
  It now copies only the region (`CopySubresourceRegion`) into a cached staging
  texture, and raises GPU scheduling priority on device creation
  (`SetGPUThreadPriority(7)` + `D3DKMTSetProcessSchedulingPriorityClass` HIGH,
  as OBS does; needs elevation, best-effort).
- **Window lookup.** `WindowTargetService.TryResolve` enumerated every window and
  opened each one's process on every sample and every input edge. The saved
  settings still hold a stale FiveM PID, which forced the slowest path. Strong
  matches are now cached and revalidated with cheap window calls plus one process
  name check (guards PID reuse).
- **Safety net.** `FishingTensionController` tracks an EMA of the sample
  interval and scales pulse length and velocity projection by it (nominal
  0.16 s, cap 2.5×, so pulses reach at most 225 ms). At a healthy cadence the
  scale is 1 and the tuned envelope is untouched.

Diagnostics: every `meter/sample` and `left_down` record now carries
`captureMilliseconds` (and `cadenceScale` on pulses), and `--capture-probe`
reports cold, steady median, and steady max capture time plus `gpu_priority`.

The same pass also buffered the per-sample Fishing CSV, ported the Pickpocket
item reader off `GetPixel`, cached Lockpicking ring directions, throttled
Lockpicking UI publishes to 50 ms (changes still immediate), paused looping UI
animations while unfocused, moved two pulses off `box-shadow`, fixed an engine
child-process leak on pipe failure, and removed two unused window permissions and
one dead export.

**Not yet proven live:** the slow capture was measured in a real session but not
reproduced during probes. See backlog issue 1 for the check.

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
