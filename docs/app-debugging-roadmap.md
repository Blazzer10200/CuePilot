# App and debugging roadmap — 2026-09-04

## Scope and implementation status

The user requested further research and an expanded to-do list for both the
overall app and the developer's debugging workflow. This document records the
source-backed design and acceptance criteria implemented for 5.3.2. It remains
a design record rather than fresh live-game validation.

The highest-value change is to connect a persistent attempt record to its
settings, build, decision timeline and evidence. Existing capture recorders,
timing telemetry, replay tests, development CDP and release checks provide a
useful base. Extend those paths before introducing another diagnostics system.

The ranked completion list lives in [Product backlog](product-backlog.md).
The final UI was checked in deterministic Edge scenarios at 760×620 and
1180×760 and in the live Tauri development shell at 1180×760. No gameplay
input or saved timing was changed by those checks.

## Baseline findings before implementation

| Evidence | Finding and implication |
| --- | --- |
| `src/Automation/PickpocketSessionState.cs`: `PickpocketRecentAttempt`, `Complete`, constructor | Version 1 stores five results and cooldown. It uses a temporary file and replacement and surfaces read/write errors. Records lack item name, timing, build and diagnostic-session identity. Extend this model with migration rather than creating unrelated UI-only history. |
| `src/Diagnostics/PickpocketDiagnosticSession.cs`: constructor, `Snapshot`, `Report`, `WriteAsync` | Session schema 4 already records engine version, timing/capture data, drop counts and evidence limits. Each session caps trace/image bytes and reserves critical images. No cross-session pruning is present in this class; Fishing and Lockpicking have `PruneOldSessions`. Aggregate Pickpocket storage needs a policy. |
| `ui/src-tauri/src/lib.rs`: `diagnostics_snapshot`, `latest_debug_session` | General diagnostics reads Fishing CSV/images and selects the newest directory under `diagnostics/sessions`. It does not provide a paginated all-activity session browser. Some files are read whole before response limits are applied; future browsing should bound disk reads as well as response size. |
| `ui/src-tauri/src/engine_bridge.rs`: `ensure_started`, `spawn_output_reader`, `command` | Command IDs, timeouts, protocol checks and process-generation handling already exist. Sidecar stderr is forwarded with `eprintln!`; the output-reader path waits for the child but discards its exit status. Add durable failure context to these paths. |
| `src/Diagnostics/PickpocketReplay.cs`: `Run` | The CLI replays frames through the detector/predictor with a simulated 0–16 ms delay envelope, text output and detector mean/p95/max. It does not expose the full recorded-engine test harness or arbitrary saved timing comparisons as a supported command. |
| `tests/CuePilot.Tests/PickpocketRecordedEngineTests.cs`: `InlineData`, recorded source and virtual clock | Existing tests run recorded observations through the production observer/input path with intercepted input. They retain raw clock origin and distinguish observed results from changed plans. Reuse this proven boundary in a developer replay tool. |
| `ui/package.json`, `ui/scripts/cdp/README.md`, `serve.cjs` | Vitest runs current tests; CDP provides DOM/accessibility inspection, measurements, interaction and console capture. There is no Playwright runner declared in the package. Build repeatable scenarios on the existing development workflow. |
| `scripts/project-status.ps1`, `scripts/verify.ps1`, `scripts/package-velopack.ps1` | Status and verification are primarily console output. Packaging records versions and installer/package hashes, but the manifest does not attest to a source revision/content fingerprint or exact verification run. |
| `docs/development.md` versus `ui/package.json` | Development guide says the dev command stages Debug; `tauri:dev` actually invokes `engine:stage:release`. This matches the later detector-performance notes. Correct the guide when reconciling current instructions. |
| `docs/pickpocket-debugging.md` versus `HANDOFF.md` | The debugging guide still calls Yellow 20 ms current and contains an older “Next test” section with observe/manual instructions. The handoff records a later 17 ms trial and an unverified 14 ms suggestion. Clearly separate current instructions from dated evidence. |

These findings record the gaps that drove the 5.3.2 implementation. They are
retained for design context and no longer describe the current feature state.

## Implemented work and acceptance criteria

### 1. History as the common attempt record

Keep the user-facing History tab simple: date, item, rarity, outcome and timing;
All/Picked up/Missed filters, pagination and an explicit Unknown state. Persist
the effective settings at the attempt, not whatever is selected when viewed later.
Keep intended item and observed pickup separate. Include build identity and an
optional evidence-session ID so older entries can survive without recordings.

Use a versioned migration from the existing five entries: missing facts stay
Unknown, IDs remain stable, and existing cooldown semantics are preserved.
Choose a documented bound before implementing (initial proposal: 1,000 compact
attempts, 20 per page). Heavy screenshots use a separate retention policy.
Reject malformed entries with a visible recovery explanation; never restore an
armed state. Avoid a database dependency until the bounded JSON model proves
insufficient.

Done when two TNT attempts can be compared after a restart, old records load,
and selecting an attempt copies its own report or clearly says that report is
unavailable. Test migration, settings snapshots, persistence failure and identity
mapping as part of this feature.

### 2. Consistent app shell and settings

Implement the existing compact-layout, F8, target-picker and misleading-copy
fixes. Give each activity the same target/reconnect affordance and clear
Ready/Waiting/Running/Stopped/Error status. Put secondary technical details in
Diagnostics. Preserve keyboard focus across drawers, validation and close;
keep save feedback and actions visible. Restore navigation without restoring
armed input. Extract the Fishing workspace from `App.svelte` only as needed
for these changes, with its accepted automation behavior preserved.

Done when the main paths work at 760×620 and 1180×760, keyboard users can reach
and apply settings, and changing the target retains the originating activity.
Include Windows scaling in the eventual native smoke matrix; browser viewport
tests alone do not establish native DPI behavior.

### 3. Repeatable UI scenarios

Create committed, deterministic development/test scenarios for connected idle,
missing target, running, cooldown, history with unknown names, storage failure,
engine disconnect and update failure. Fix scenario clocks and IDs. The scenario
adapter must intercept commands and isolate persistence; a preview must not
forward Start, native input or settings writes to the installed app.

Recommended approach: a small Playwright suite for browser scenarios, plus a
narrow development WebView2 smoke using the existing CDP launcher. Keep the
current compact inspection commands for interactive debugging. Use accessible
roles/names and stable test IDs where needed. Assert focus, visible actions,
settings rollback, history selection and geometry; reserve screenshot baselines
for layout-sensitive screens. Retain a trace on failure with scenario/build IDs.

Done when a failed layout or state transition can be reopened from its scenario
and trace, without reconstructing a mock in `tmp/` or starting FiveM.

### 4. Session browser, timeline and support bundle

Index Fishing, Lockpicking and Pickpocket sessions by stable ID, activity, time,
build, result, completion and evidence health. Read metadata first and load
bounded pages/thumbnails on demand. Handle an interrupted JSONL tail or missing
summary as incomplete evidence; do not silently present it as a clean finish.

Show a short decision sequence: target found, item selected, return pass ready,
press planned, host key-down/up, observed outcome, stop reason. Explain the last
blocker in plain language. Advanced details retain frame IDs and timestamps.
Reuse the existing event meanings; add correlation IDs where absent rather than
replacing the capture instrumentation.

Add Copy selected report and Export selected evidence bundle with a manifest
listing included/omitted files. Text summaries should omit user paths/window
titles by default; screenshots are explicitly selected. Exports remain local.
Resolve sessions through known IDs under the diagnostics root, not arbitrary
paths supplied by the UI.

Done when a History row opens exactly its session, an incomplete session remains
inspectable, and one local bundle contains enough context to reproduce a report.

### 5. Durable failures and read-only health checks

Add bounded rotating local shell logs for startup identity, sidecar stderr,
exit status, command timeouts, protocol mismatch and reconnect attempts. Carry
command/session/build IDs through the report. Add bounded frontend error and
unhandled-rejection capture with deduplication; avoid recording complete bridge
payloads or screenshot data in ordinary logs. Keep log writing off timing paths.

Extend existing project status/CDP doctor and the product's health presentation
with structured results: installed/dev identity, shell/engine versions, protocol,
process ownership, persistence availability and latest known capture health.
Fresh capture verification should remain an explicit setup action; a passive
health report must label old readings with their age.

Done when a controlled development sidecar exit or command timeout produces a
useful persistent report even without a gameplay recording. A failed health
check reports the cause and next action without auto-starting a routine.

### 6. Full recorded-engine replay and timing comparison

Extract the test harness's recorded source, virtual clock and intercepted input
into a reusable replay-only boundary. Accept a selected recording/manifest and
explicit settings overrides; output JSON plus a concise Markdown report. Distinguish
observation replay from pixel replay so neither claims coverage it did not run.

Compare saved configuration against proposed 8/14/17/20 ms yellow values, showing
selected item, planned time, direction, uncertainty and rejection gates. Preserve
monotonic time origin and mark dropped/missing input evidence. Validate bounded
manifests and frame paths. Provide a route to retain a minimal regression fixture
with provenance and observed expected outcome.

Done when a named TNT recording can reproduce its original decision and explain
a timing-plan change through one command. A captured Missed result must remain
Missed under every counterfactual; simulated delivery is never a live hit.

### 7. Evidence storage and performance budgets

Show total diagnostic size, per-activity size and incomplete/limited sessions.
Propose an explicit future retention setting for Pickpocket (starting point:
newest 20 sessions within 1 GB). Pinned investigations, active sessions and
exported evidence need explicit handling; when protected content exceeds the
budget, report the condition rather than silently removing it. Preview exact
cleanup targets and sizes. This research does not authorize deleting evidence.

Retain small History entries independently when bulky evidence expires, and
show “Evidence no longer available.” Document separate history/log/image limits.

Extend live reports with bounded histograms for capture/analysis/sample interval,
timer lateness and dropped/stale frames. Preserve the existing counters and
replay p95. Use difficult recorded headers as a performance baseline; record
hardware/build, warm-up and sample count so ordinary machine jitter is not
mistaken for a regression. Measure instrumentation overhead before adding a gate.

Done when storage growth is explainable and a replay comparison identifies the
stage that slowed down. Do not derive physical game-receipt latency from a frozen
result offset or combine unsynchronized process clocks as though they were one.

### 8. Contract fixtures, build receipts and maintainable notes

Add shared versioned JSON fixtures for ready/status/fault, optional fields,
history migration and unknown future values across .NET, Rust and TypeScript.
Exercise reconnect and invalid-state behavior using controlled fixtures. Keep
current correlation/protocol checks and add focused coverage as contracts change.

Make `project-status` optionally emit JSON and `verify` write a receipt containing
command outcomes, durations, tool versions and source identity, including dirty
content. Link receipts to packaged shell/engine hashes and display installed
build details in About and diagnostic manifests. Mark a receipt stale after its
inputs change; a matching version string or Git HEAD alone is insufficient for
this dirty worktree. Preserve the existing installed updater smoke test.

Update `docs/code-map.md` with Pickpocket history, replay and diagnostics routes.
Keep current controls/next trial in one clearly dated place, with earlier trials
labeled historical. Add an evidence index of fixture ID, activity, item/result,
build, clock basis and matching replay/test command.

Done when another debugging session can identify the running build, locate the
relevant fixture, run its narrow check and tell whether a prior gate still applies.

## External research and tool choice

Official references consulted on 2026-09-04:

- [Playwright WebView2](https://playwright.dev/docs/webview2): supports connection
  through CDP and recommends isolated WebView user-data directories for tests.
  This supports reusing CuePilot's existing development launcher. It is not a
  reason to enable remote debugging in installed releases.
- [Playwright Trace Viewer](https://playwright.dev/docs/trace-viewer): action
  traces include before/action/after DOM snapshots. Use these for reproducible
  UI failures; they do not record or prove game input behavior.
- [Playwright assertions](https://playwright.dev/docs/test-assertions): retrying
  assertions cover focus, visibility, values and accessible names. These fit the
  current settings/history risks better than screenshots alone.
- [Playwright visual comparisons](https://playwright.dev/docs/test-snapshots):
  screenshot results vary with environment. Generate and compare baselines under
  a controlled Windows/browser setup rather than treating every pixel change as
  a product regression.
- [Tauri WebDriver guidance](https://v2.tauri.app/develop/tests/webdriver/): the
  current recommended WebdriverIO service offers Tauri IPC mocking and log
  capture, with embedded or external driver choices. This is a viable fallback
  if the narrow CDP smoke cannot cover required native behavior. Defer adding a
  second test stack until a concrete CDP limitation warrants it.

Tool selection is a recommendation based on those capabilities and the existing
repository investment; compatibility has not been prototyped in this pass. No
dependency upgrade or new runner was installed. Full tracing infrastructure is
unnecessary for the first iteration: extend the existing local structured records.

## Verification of the implementation

The 5.3.2 source passed the complete repository gate, five deterministic
Playwright scenarios, a live Tauri layout/console inspection, and replay timing
comparisons at 8/14/17/20 ms. The last live Yellow evidence remains 17 ms;
14 ms remains an unvalidated proposal and was not applied by this work.
