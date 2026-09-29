# Handoff — CuePilot 5.3.18 — 2026-09-29

The snapshot is the current truth. Dated session records for 5.3.7 – 5.3.15 are
archived in [docs/history/handoff-batches-2026-09.md](docs/history/handoff-batches-2026-09.md);
user-facing changes are in [CHANGELOG.md](CHANGELOG.md).

## Snapshot

| | |
| --- | --- |
| Branch | `main` holds the 5.3.18 commit on top of `cc83253` (pushed directly, no PR). Audit fixes are on `audit/fix-now-5.3.18`, unmerged (see below) |
| Source version | **5.3.18** in all six release files. **Not tagged** |
| Published | **v5.3.17 is still the public release.** 5.3.18 was built and installed locally only; the `v5.3.18` tag was deliberately not pushed (tags trigger CI + Velopack and ship to installed apps, so tag only on an explicit request) |
| Installed locally | **5.3.18**, installed 2026-09-29 from the local Setup.exe (`--silent`); shell and engine (`5.3.18+cc83253`) launch and stay up from `%LOCALAPPDATA%\CuePilotDesktop\current` |
| Package | `release/velopack/` holds 5.3.17 (delta base) and 5.3.18 full + delta, Setup.exe, Portable.zip. Never delete the directory to clean up a build |
| Last gates | 2026-09-29: dotnet 412, vitest 49, svelte-check 0, Playwright 28, rustfmt, clippy `-D warnings`, cargo 36 |

Orientation in one command: `pwsh -NoProfile -File scripts/project-status.ps1`.

## What 5.3.18 shipped

Pickpocket detection rework, worked entirely offline from saved sessions (no live play was available). Details in [CHANGELOG.md](CHANGELOG.md).

- **Detector fixes** (`PickpocketDetector.cs`): each stem pass gets its own header budget (a busy scene no longer starves the real marker); the scale search tolerates grass-inflated stems; one Hidden frame no longer wipes the tracked history (`PickpocketObserverEngine` keeps a 400 ms tracking hint).
- **Session analyzer** (`Diagnostics/PickpocketSessionAnalyzer.cs`): `--analyze-pickpocket <session|latest>` writes a plain-language `ANALYSIS.md` verdict; `--pickpocket-corpus <folder>` scores every saved session. See [docs/pickpocket-debugging.md](docs/pickpocket-debugging.md).
- **Offline result across 11,459 saved frames:** 43 of 44 lost frames recovered, 1 cold miss, 1 minor regression, 6 borderline false-positive candidates (header 0.81–0.85). Analysis p50 1.0 ms, p99 14.5 ms.

## Audit fix pass (branch `audit/fix-now-5.3.18`, unmerged)

A read-only audit of 5.3.18 was followed by fix batches on this branch. Nothing is committed yet: the changes sit in the working tree (about 30 modified files plus 4 new test files) and are listed under **Unreleased** in [CHANGELOG.md](CHANGELOG.md). No version bump, no tag. The batches each ran their own narrow tests; the full `verify.ps1 -All` gate has not been run over the combined branch, so run it before merging. The audit report is in the session scratchpad only and is not committed; findings it did not fix are recorded in backlog items 13 and 14.

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
