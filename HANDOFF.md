# Handoff — CuePilot 5.3.18 — 2026-09-29

The snapshot is the current truth. Dated session records for 5.3.7 – 5.3.15 are
archived in [docs/history/handoff-batches-2026-09.md](docs/history/handoff-batches-2026-09.md);
user-facing changes are in [CHANGELOG.md](CHANGELOG.md).

## Snapshot

| | |
| --- | --- |
| Branch | `main`, one 5.3.18 commit on top of `cc83253`. Pushed directly; no PR |
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

## Carried over

- **Not proven in game (top priority):** none of the 5.3.18 detector work has been seen in live play. First step next session: do one or two real pickpockets, run `--analyze-pickpocket latest`, read the verdict.
- **Thin Red/Yellow timing** (~77% hit rate, 0 of 3 since 5.3.15) is untouched. See backlog.
- **Attempt tracker:** the 3 s Hidden guard in `PickpocketAttemptTracker` changes only if the analyzer reports a "false attempt end" on live data.
- **5.3.17 popups** were never viewed on screen. If a card looks wrong, start with `overlay.css`.
- **Fishing park gate:** Phase A from 5.3.15 is still open. See [the perf plan](docs/history/perf-plan-2026-09-26.md).
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
