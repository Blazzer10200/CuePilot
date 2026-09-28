# Handoff — CuePilot 5.3.15 + unreleased UI polish — 2026-09-28

The snapshot is the current truth. Dated session records for 5.3.7 – 5.3.15 are
archived in [docs/history/handoff-batches-2026-09.md](docs/history/handoff-batches-2026-09.md);
user-facing changes are in [CHANGELOG.md](CHANGELOG.md).

## Snapshot

| | |
| --- | --- |
| Branch | `feat/ui-polish`, pushed with a PR into `main`. It carries the fix commits (`bbdbd78`, `425c102`, `61db8f9`) plus the UI polish phases 1–5 |
| Source version | **5.3.15** in all six release files. Everything under CHANGELOG **Unreleased** is untagged; the next release is 5.3.16 |
| Published | **v5.3.15 public** (2026-09-27, [release](https://github.com/Blazzer10200/CuePilot/releases/tag/v5.3.15)). Tags ship to installed apps through CI + Velopack, so tag only on an explicit request |
| Installed locally | Last recorded **5.3.14** (2026-09-26); run `project-status.ps1` for the current install |
| Package | `release/velopack/` still holds the 5.3.13 – 5.3.15 local packages plus the v5.3.4 `publication-receipt.json` / `verification-receipt.json`. Packaging does not regenerate those receipts, so never delete the directory to clean up a build |
| Last gates | Full `-All` gate 2026-09-28 on `feat/ui-polish`: docs 24 / 163 links, dotnet 404, vitest 49, svelte-check 0, Playwright 28, rustfmt, clippy, cargo 30 |

Orientation in one command: `pwsh -NoProfile -File scripts/project-status.ps1`.

## What changed since 5.3.15

- **Pickpocket never pressed Space** after the game started animating the "TIME LEFT" header; tracking now tolerates a bounded 30-frame continuation (`bbdbd78`).
- **GPU priority raise removed** from capture; it preempted the game.
- **Vehicle Lockpicking removed** everywhere (`425c102`). Old settings with `lockpickingStartStop` still load.
- **Mouse hook** installs only when a mouse button is bound.
- **UI polish** from the "UI improvements for codebase" design handoff: design and motion tokens, new shell, home, fishing, pickpocket (header Live/History/Reference tabs, window-sized live view) and settings drawer layouts, one focus ring, idle pause limited to looping animations.

## Carried over

- **Not proven in game:** the Pickpocket header fix and the UI polish were verified with tests, fixtures and scenario screenshots only.
- The save-failure Playwright test failed once under a slow full run and passed on every rerun; watch it for flakiness.
- 5.3.15 Phase A park gate: fish once at the park on 5.3.15+, then run the three `jq` lines in [the perf plan](docs/history/perf-plan-2026-09-26.md) (capture median < 12 ms, tap→read median < 40 ms, catches ≥ failures).
- Tray: confirm a real right-click → Quit, and that a plain right-click alone does not quit.

## Release 5.3.16 when ready

Bump the version in the six files listed in `CLAUDE.md` / `AGENTS.md`, check them with
`pwsh -NoProfile -File scripts/project-version.ps1`, date the CHANGELOG heading, merge to
`main`, then push the `v5.3.16` tag. CI builds and publishes the Velopack release.

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
