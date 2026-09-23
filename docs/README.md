# Documentation index

Start here to find the maintained guide for a task. [Project overview](../README.md)
explains installation and everyday use.

Two files carry live state and are read before anything else:

- [HANDOFF.md](../HANDOFF.md) — **what is true now**: branch, version, installed
  build, package, working tree, and the last verification gate. Newest first.
- [Current backlog](product-backlog.md) — **what is next**: the open-issue table
  and standing priorities.

## Find your task

| Task | Start here |
| --- | --- |
| See the current state of the checkout | [HANDOFF.md](../HANDOFF.md) |
| Pick up an open issue or the next improvement | [Current backlog](product-backlog.md) |
| Understand the repository and locate code | [Code map](code-map.md) |
| Set up, run, test, debug, or package the app | [Development guide](development.md) |
| Understand activity boundaries and readiness | [Activity architecture](activities.md) |
| Change a keybind, mouse shortcut, or desktop popup | [Code map — shortcuts and notifications](code-map.md#shortcuts-and-desktop-notifications) |
| Diagnose a Pickpocket run or compare timing | [Pickpocket debugging](pickpocket-debugging.md) |
| Find recorded Pickpocket evidence and its limits | [Evidence catalog](pickpocket-evidence.md) |
| Work on the Svelte/Tauri shell | [UI entry point](../ui/README.md) |
| Inspect the development WebView | [CDP commands](../ui/scripts/cdp/README.md) |
| Understand Pickpocket regression images | [Fixture guide](../tests/CuePilot.Tests/Fixtures/Pickpocket/README.md) |
| Contribute or report a security issue | [Contributing](../CONTRIBUTING.md), [security policy](../SECURITY.md) |
| Review version history | [Changelog](../CHANGELOG.md) |

Run `pwsh -NoProfile -File scripts/project-status.ps1` from the repository root
for branch, source versions, local installation, package location, and worktree
changes. These are local observations; they do not verify the public release feed.

## Historical decisions

Completed plans and acceptance records live under `docs/history/`. They explain
why features were built, and may describe defects or proposed settings that have
since changed. Use the maintained guides and current handoff for present behavior.

- [App and debugging design for 5.3.2](history/app-debugging-roadmap-2026-09-04.md)
- [Original Pickpocket implementation plan](history/pickpocket-plan-2026-09-03.md)
- [Recorded Pickpocket calibration trials](history/pickpocket-calibration-2026-09-04.md)
- [Completed product backlog and acceptance history](history/product-backlog-2026-09-04.md)
- [Pickpocket clip and live-run evidence, run by run](history/pickpocket-evidence-2026-09-03.md)
- [Session handoffs for 5.3.7 – 5.3.9](history/handoff-batches-2026-09.md)

## Keep documentation organized

- Keep setup and executable commands in the development guide, task-to-source
  routes in the code map, and outstanding work in the backlog. Link to those
  pages instead of copying their instructions into several files.
- Keep current session facts in `HANDOFF.md` and outstanding work in the
  backlog's open-issue table; preserve replaced handoffs in the existing
  external handoff archive. Long-lived design history belongs here.
- Label evidence by date and distinguish recorded observations, proposed changes,
  automated verification, and live gameplay validation.
- Link source files by path and symbol rather than fragile line numbers. Update
  the code map and this index when adding or moving a maintained guide.
- Keep generated captures, build output, local settings, and release payloads out
  of the documentation tree. Their locations are described in the development guide.
- Run `pwsh -NoProfile -File scripts/verify.ps1 -Docs` after documentation edits.
  It checks repository-local Markdown links and developer-tool regressions without
  building or launching the app. External URLs are not fetched.
