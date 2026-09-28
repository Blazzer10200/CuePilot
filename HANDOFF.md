# Handoff — CuePilot 5.3.16 — 2026-09-28

The snapshot is the current truth. Dated session records for 5.3.7 – 5.3.15 are
archived in [docs/history/handoff-batches-2026-09.md](docs/history/handoff-batches-2026-09.md);
user-facing changes are in [CHANGELOG.md](CHANGELOG.md).

## Snapshot

| | |
| --- | --- |
| Branch | `main` at the PR #8 merge (`2efa9eb`); `feat/ui-polish` is merged |
| Source version | **5.3.16** in all six release files (`9b07a4f`, tagged `v5.3.16`) |
| Published | **v5.3.16 public**, 2026-09-28 16:23 UTC, marked Latest: [release](https://github.com/Blazzer10200/CuePilot/releases/tag/v5.3.16), run 36448677746 green in 16m19s (8 assets; `releases.win.json` lists 5.3.16 full + delta from 5.3.15). Tags ship to installed apps through CI + Velopack, so tag only on an explicit request |
| Installed locally | **5.3.16**, installed 2026-09-28 from the local Setup.exe (same commit); shell and engine started, window title `CuePilot` |
| Package | `release/velopack/` holds only 5.3.16 (older 5.3.10 – 5.3.15 packages deleted at the owner's request and pruned from the local `releases.win.json` / `RELEASES`), plus the v5.3.4 `publication-receipt.json` / `verification-receipt.json`. Packaging does not regenerate those receipts, so never delete the directory to clean up a build |
| Last gates | Full `-All` gate 2026-09-28: docs 24 / 163 links, dotnet 404, vitest 49, svelte-check 0, Playwright 28, rustfmt, clippy, cargo 30. After the bump: `-Docs -Rust` green; PR `build` and tag `release` workflows green |

Orientation in one command: `pwsh -NoProfile -File scripts/project-status.ps1`.

## What 5.3.16 shipped

- **Pickpocket never pressed Space** after the game started animating the "TIME LEFT" header; tracking now tolerates a bounded 30-frame continuation (`bbdbd78`).
- **GPU priority raise removed** from capture; it preempted the game.
- **Vehicle Lockpicking removed** everywhere (`425c102`). Old settings with `lockpickingStartStop` still load.
- **Mouse hook** installs only when a mouse button is bound.
- **UI polish** from the "UI improvements for codebase" design handoff: design and motion tokens, new shell, home, fishing, pickpocket (header Live/History/Reference tabs, window-sized live view) and settings drawer layouts, one focus ring, idle pause limited to looping animations. The owner reviewed it in the dev app before release.

## Carried over

- **Not proven in game:** the Pickpocket header fix and the UI polish were verified with tests, fixtures and scenario screenshots only. First in-game Pickpocket attempt and Fishing run on 5.3.16 are the check.
- The save-failure Playwright test failed once under a slow full run and passed on every rerun; watch it for flakiness.
- 5.3.15 Phase A park gate: fish once at the park on 5.3.15+, then run the three `jq` lines in [the perf plan](docs/history/perf-plan-2026-09-26.md) (capture median < 12 ms, tap→read median < 40 ms, catches ≥ failures).
- Tray: confirm a real right-click → Quit, and that a plain right-click alone does not quit.

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
