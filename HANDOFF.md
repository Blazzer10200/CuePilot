# Handoff — CuePilot 5.3.17 — 2026-09-28

The snapshot is the current truth. Dated session records for 5.3.7 – 5.3.15 are
archived in [docs/history/handoff-batches-2026-09.md](docs/history/handoff-batches-2026-09.md);
user-facing changes are in [CHANGELOG.md](CHANGELOG.md).

## Snapshot

| | |
| --- | --- |
| Branch | `main` at `06995c3` (notification overhaul + 5.3.17 bump), on top of `dacc66a` (Pickpocket GPU priority). Pushed directly; no PR this time |
| Source version | **5.3.17** in all six release files, tagged `v5.3.17` |
| Published | **v5.3.17 public**, 2026-09-28 17:43 UTC, marked Latest: [release](https://github.com/Blazzer10200/CuePilot/releases/tag/v5.3.17), run 36458242289 green in 16m16s (8 assets incl. 5.3.17 full + delta). `main` Build run 36458231951 green. Tags ship to installed apps through CI + Velopack, so tag only on an explicit request |
| Installed locally | **5.3.17**, installed 2026-09-28 from the local Setup.exe (`--silent`, same commit); shell and engine relaunched |
| Package | `release/velopack/` holds 5.3.16 and 5.3.17 (the 5.3.16 package is kept as the delta base), plus the v5.3.4 receipts. Never delete the directory to clean up a build |
| Last gates | 2026-09-28 before tagging: dotnet 404, vitest 49, svelte-check 0, Playwright 28, rustfmt, clippy `-D warnings`, cargo 36 |

Orientation in one command: `pwsh -NoProfile -File scripts/project-status.ps1`.

## What 5.3.17 shipped

- **Pickpocket GPU priority** is raised only while the minigame is on screen and a shot is still possible (`dacc66a`). This restores capture latency lost in 5.3.16 without bringing back Fishing hitching.
- **Notifications rebuilt** (`notifications.rs`, `Overlay.svelte`, `overlay.css`, `NotificationSettings.svelte`):
  - The pump thread is event-driven. It wakes on enqueue instead of a 200 ms poll.
  - Delivery has a 120 ms settle window. A same-activity notice replaces the card in place, and critical notices interrupt.
  - Cards now have a slide-out exit (`motion.ts` `notice`).
  - New events: Pickpocket result, paused, and faulted; Fishing stopped and stopped on a problem; emergency stop; shortcut failures; engine disconnected and reconnected.
  - New popup corner preference. Placement follows the monitor of the foreground window.
- **Dead CSS removed** from `app.css`: `.system-status`, `.card-kicker`, `.target-button`, `.drag-hint`.

## Carried over

- **Not proven on screen:** the 5.3.17 popups were never viewed. At Blazzer's request the release shipped on unit and e2e tests alone, with no dev-app visual check. The first real popup is the check; if the card looks wrong, start with `overlay.css` (`.overlay-card` is now `position: absolute; inset: var(--overlay-gutter)`).
- **Pickpocket (not proven in game):** the 5.3.16 header fix and the 5.3.17 GPU-priority change still need a real Pickpocket attempt.
- **Fishing park gate:** Phase A from 5.3.15 is still open. See [the perf plan](docs/history/perf-plan-2026-09-26.md).
- **Tray:** confirm that a real right-click then Quit works, and that a plain right-click alone does not quit.
- **Manual scripts:** `scripts/build-brand-assets.ps1`, `test-velopack-update.ps1` and `benchmark-pickpocket.ps1` are not referenced by other files. They were kept as manual tools, not deleted as dead code.

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
