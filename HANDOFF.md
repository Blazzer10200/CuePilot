# Handoff — CuePilot 5.3.11 — 2026-09-23

The snapshot is the current truth. Dated session records for 5.3.7 – 5.3.9 are
archived in [docs/history/handoff-batches-2026-09.md](docs/history/handoff-batches-2026-09.md);
user-facing changes are in [CHANGELOG.md](CHANGELOG.md).

## Snapshot

| | |
| --- | --- |
| Branch | `codex/release-complete` |
| Source version | 5.3.11, synchronized across all six release files. **Uncommitted**: 15 modified files (engine, tests, docs, version bump) |
| Published | **v5.3.10 public**, 2026-09-23 03:12 UTC, marked Latest: [release](https://github.com/Blazzer10200/CuePilot/releases/tag/v5.3.10), run 35812371777 green. 5.3.11 is local only, not tagged or published |
| Installed locally | **5.3.11**, installed 2026-09-23 with Setup `--silent` over a running 5.3.10 (stopped by exact install path), relaunched through the Start Menu shortcut; shell and sidecar came up, `project-status.ps1` reports `installed: v5.3.11` |
| Package | `release/velopack/` — 5.3.11 built 2026-09-23 05:08: Setup 45.6 MB, full 41.3 MB, delta from 5.3.10 31.2 MB (unusually large next to 5.3.10's 1.9 MB; not investigated) |
| Last gates | 5.3.11: `dotnet build` + `dotnet test` 449/449 and `verify.ps1 -Docs` green 2026-09-23. UI and Rust untouched except the version bump; `verify.ps1 -All` not rerun. Last full gate: 2026-09-22 at 5.3.10 (docs 23 / 163 links, dotnet 448, vitest 51, svelte-check 0/0, Playwright 23, clippy, cargo 28) |

`release/velopack/` also holds `publication-receipt.json` and
`verification-receipt.json` from the published v5.3.4. Packaging does not
regenerate them, so never delete that directory to clean up a build.

Orientation in one command: `pwsh -NoProfile -File scripts/project-status.ps1`.

## 5.3.11 — Pickpocket no longer silently skips attempts

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
