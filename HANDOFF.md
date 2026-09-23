# Handoff — CuePilot 5.3.10 — 2026-09-22

The snapshot is the current truth. Dated session records for 5.3.7 – 5.3.9 are
archived in [docs/history/handoff-batches-2026-09.md](docs/history/handoff-batches-2026-09.md);
user-facing changes are in [CHANGELOG.md](CHANGELOG.md).

## Snapshot

| | |
| --- | --- |
| Branch | `codex/release-complete` |
| Source version | 5.3.10, synchronized across all six release files |
| Published | **v5.3.10 public**, 2026-09-23 03:12 UTC, marked Latest: [release](https://github.com/Blazzer10200/CuePilot/releases/tag/v5.3.10), run 35812371777 green; Setup, Portable, full + delta nupkg, feed, manifest, sha256 all present |
| Installed locally | **5.3.10**, installed 2026-09-22 with Setup `--silent` over a running 5.3.9 (stopped by exact install path), then relaunched at medium integrity; shell and sidecar came up |
| Package | `release/velopack/` — 5.3.10 built 2026-09-22 21:55: Setup 45.6 MB, full 41.3 MB, delta from 5.3.9 1.9 MB |
| Last full gate | `verify.ps1 -All` green 2026-09-22: docs 23 files / 163 links, dotnet 448, vitest 51, svelte-check 0/0, Playwright 23, clippy `-D warnings`, cargo 28 |

`release/velopack/` also holds `publication-receipt.json` and
`verification-receipt.json` from the published v5.3.4. Packaging does not
regenerate them, so never delete that directory to clean up a build.

Orientation in one command: `pwsh -NoProfile -File scripts/project-status.ps1`.

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
