# CuePilot product backlog — 2026-09-04 (historical)

> Historical backlog and acceptance record for the 5.3.2–5.3.3 work batches.
> It intentionally retains completed work and dated calibration notes. For
> active priorities, read the current [product backlog](../product-backlog.md).

This is the current product status and ranked later-work list. The app/debugging
batch shipped in 5.3.2; 5.3.3 adds the workspace polish described below.
Older review measurements remain historical evidence, not current defects.
Neither release enables unverified automation.

## Workspace polish — 5.3.3

- Consistent FiveM window, Settings, and Diagnostics tools across activities, with active-run guards and focus restoration.
- Readable compact Home rows, restored amber calibration styling, and distinct disconnected feedback.
- Explicit Pickpocket live/input states, dated saved results, save feedback across controls, and secondary technical measurements.
- Activity-specific diagnostics and corrected Lockpicking setup guidance; shared navigation safely stops an active observer.
- Responsive checks include intermediate window sizes as well as the supported minimum. See the [Development guide](../development.md#workspace-ui-checks) for the UI regression scope.

The requested UI polish is complete in source. Release/install verification and cleanup are recorded in the current [handoff](../../HANDOFF.md). Gameplay timing and calibration remain separate work.

## Expanded app and debugging priorities — 2026-09-04

Requested follow-up research covers the whole app and the local debugging
workflow. Detailed source evidence, implementation boundaries, acceptance
criteria, and external references are in the historical
[App and debugging roadmap](app-debugging-roadmap-2026-09-04.md).
The implementation and official 5.3.2 release were completed on 2026-09-04;
live Yellow calibration remains separate work.

| Order | Work | Result |
| --- | --- | --- |
| 1 | Correct misleading controls and stale current instructions | **Done in source.** Mode, shortcuts, saved timing and help agree. |
| 2 | Persistent item History and explicit earlier/later tuning | **Done in source.** Bounded dated history and 1 ms controls are available. |
| 3 | Shared session identity and selected-attempt reports | **Done in source.** Rows copy their own report and open their evidence session. |
| 4 | Compact shared shell, target selection and accessible settings | **Done in source.** Verified at 760×620 and 1180×760. |
| 5 | Repeatable UI scenarios and interaction/resize tests | **Done in source.** Expanded Playwright scenarios cover layout, state, shared tools, and focus without FiveM. |
| 6 | All-activity diagnostics browser and decision timeline | **Done in source.** Support Center pages all activity sessions. |
| 7 | Durable shell/engine errors and a read-only health report | **Done in source.** Logs are bounded and health is passive. |
| 8 | Recorded-engine replay and attempt comparison tool | **Done in source.** Replay accepts timing plans and benchmark receipts preserve outcomes. |
| 9 | Aggregate evidence storage and incomplete-session handling | **Done in source.** Disk use and incomplete sessions are visible; cleanup awaits explicit deletion authorization. |
| 10 | Shared bridge fixtures and persistence migration coverage | **Done in source.** .NET/Rust/TypeScript share a fixture; v1 history migration is covered. |
| 11 | Build identity and machine-readable verification receipts | **Done in source.** Status and dirty-source receipts are JSON. |
| 12 | Performance baselines, evidence catalog and code-map upkeep | **Done in source.** Machine-local replay benchmark and code-map commands are present. |

Implementation completed in four batches: **1–3**, **4–5**, **6–9**, and
**10–12**. Persistence and contract coverage landed with History, and build
identity is stored with new attempts and release verification receipts.

Yellow calibration remains a separate supervised live task: last recorded
17 ms, proposed 14 ms unverified. Confirm the saved value before another trial.
Fishing remains parked and Lockpicking remains observe-only.

## Priorities at snapshot date

### Pickpocket priority at snapshot date

As of 2026-09-04, 5.3.2 supports one automatic Space pair, return-pass timing for tiny regions, color/item priorities, persistent History, local evidence, and the all-activity Support Center. Red, purple and white successes are recorded; Yellow TNT timing remains unresolved. The last 17 ms trial was 5 px before center; 14 ms is suggested next and remains unverified. Fishing is user-tested and parked. Lockpicking remains observe-only. See `HANDOFF.md` for current calibration evidence.

### Implemented UI review — 2026-09-04

The following requirements were implemented and retained here as acceptance
history. Home, Fishing, Lockpicking, Pickpocket, settings, History, and failure
states were verified in isolated Edge scenarios. No gameplay input,
installed-app settings change, release installation, or public-feed check was
part of this batch.

**1. Correct misleading Pickpocket controls — first fix.**

- The shortcut drawer says “Space input stays off” even with Precision selected. Make the description follow the selected mode: observe only, wide-target tap, or precision tap.
- The yellow dropdown permanently marks 20 ms “current trial,” even when another value is saved. Derive that marker from saved state or remove it.
- “Shortcuts & window” opens a shortcut-only drawer. Either include the window control or name the button “Shortcut settings.”
- Done when the button, F7 description, run mode, saved value and input-armed status agree in all three modes. Source: `ui/src/App.svelte`, `PickpocketLiveWorkspace.svelte`.

**2. Make earlier/later tuning and results obvious.**

- **Completed request: a dedicated History tab listing what items were picked up and what items were missed.** It persists dated attempts with recognized item name, rarity, Picked up/Missed/Unknown outcome and the timing setting used. All/Picked up/Missed filters and page controls keep history bounded. Unrecognized names/results remain Unknown, and planned targets are distinct from observed outcomes; a Space press does not imply inventory acquisition.
- Add explicit 1 ms “Press later” / “Press earlier” controls and a plain explanation of the change: 17 → 14 ms means pressing 3 ms later. Keep independent yellow/red values and visible save feedback.
- Result history should retain item name, timing used and build version for each attempt. It currently stores color, width, outcome, offset and press count, so comparing trials requires logs.
- Show “Before target center” / “Past target center” prominently, with early/late interpretation only where travel direction and result evidence support it. Frozen-frame offset is not measured input latency and must not silently become an automatic correction.
- Keep historical report selection explicit: current Copy report copies the current session, even while an older result is selected. Its tooltip explains this; the visible label should too, or support copying the selected attempt.
- Done when two TNT attempts can be compared in the UI without opening JSONL. Sources: `PickpocketLiveWorkspace.svelte`, `src/Automation/PickpocketSessionState.cs`, bridge result models.

**3. Fit the other pages and keep actions visible.**

Measured at the supported 760×620 minimum viewport: Home document height
1307 px, Fishing 817 px, Lockpicking 1106 px, recorded Pickpocket reference
626 px. Pickpocket live fits at 620 px. Home/Fishing/Lockpicking fit at
1180×760, but Fishing settings still contain 1093 px in a 760 px drawer;
at minimum size the drawer contains 1351 px in 620 px. Apply is below the fold.

- Use compact activity cards at smaller sizes; reduce oversized introductory areas and place secondary telemetry in tabs or disclosure panels.
- Make Basic/Advanced separate settings views, with Cancel/Apply always visible. Preserve current Fishing values and engine behavior.
- Remove the reference page's small overflow without clipping content. Retain deliberate scrolling for long evidence lists rather than hiding overflow globally.
- Done when primary pages and ordinary settings fit at both sizes, with keyboard focus and actions visible. Sources: `ActivityPicker.svelte`, `App.svelte`, `app.css`, `LockpickingWorkspace.svelte`, `PickpocketWorkspace.svelte`.

**4. Keep game-window selection in the current activity.**

- Pickpocket's setup callback calls `selectActivity("fishing")` before `findTarget()`. A shared target picker should return to the page the user was using.
- Show selected FiveM window plus a clear change/reconnect action consistently across activities.
- Done when Pickpocket can select/reselect its target without navigating into Fishing. Source: `App.svelte`, Pickpocket `onsetup` callback. Source-confirmed; native target selection was not exercised in this review.

**5. Reserve F8 consistently throughout settings.**

- Pickpocket filters out F8; Fishing and the reserved Lockpicking shortcut still offer it. Apply the user's FiveM-console exclusion to all selectors and validate it consistently in settings handling.
- Explain a conflict with another activity's shortcut before saving, while preserving existing backend validation and user bindings. Do not silently remap an existing preference.
- Done when F8 cannot be newly assigned and conflicting bindings have useful feedback. Source: `App.svelte`, shared `shortcutOptions` and activity selectors.

**6. Make build identity and the installer easy to find.**

- Add a clear About/Build entry with installed version, local build/public channel distinction, and a reliable path to the current installer or release folder when available.
- Updater says the installed version “is the newest public release” whenever no newer update is returned. Use “No newer public update available”; absence of an update does not establish that a local build was publicly published.
- Done when users can distinguish installed app, desktop shortcut and installer, and copy build details for a report. Sources: `UpdateCenter.svelte`, `updates.svelte.ts` (`refresh`). Installed updater behavior reviewed in source; only development-disabled state was rendered.

**7. Clarify item selection and recognition.**

- Add a compact summary of color order and same-color item order so the effective selection rule is visible from Run. Clearly state that Widest ignores item rank.
- Label unrecognized names explicitly instead of implying every colored region has a catalogued item. Preserve color fallback and show what won selection.
- Review the newly seen blue Wallet label against saved evidence before adding a verified catalog entry/template. Do not invent a name from color alone.
- Done when two same-color regions make the chosen item and fallback understandable. Sources: `PickpocketLiveWorkspace.svelte`, `pickpocket-catalog.ts`, `PickpocketItemReader.cs`.

**8. Lower priority: simplify navigation and evidence summaries.**

- Optionally restore the last activity/tab on launch, without restoring an armed state. Currently `selectedActivity` initializes to null.
- Reduce Home jargon and hide the empty “0 preview” count. Keep honest readiness labels, especially observe-only Lockpicking and uncalibrated yellow timing.
- Continue the existing Detection Review timeline proposal below: explain the last decision/blocker before raw event details. For Lockpicking, replace the idle footer “None” with a meaningful state and describe what evidence is still needed.
- Fishing automation remains parked; any future Fishing work here is presentation only unless separately requested.

**Review evidence:** `tmp/ui-audit-*.png` and `tmp/ui-audit-initial.json`
contain isolated fixture screenshots/measurements, not live gameplay proof.
Source findings are distinguished above from rendered observations. These
requirements ship in 5.3.2; the bullets remain as acceptance history.

### Completed — safety and reliability blockers from the 2026-08-25 audit

1. **Owned-input release and stop gate:** Fishing Stop/fault/disposal bypasses foreground validation only for a key or mouse button CuePilot actually owns. Stop closes the input gate before publishing completion, so a late detector result cannot begin another press; idle and observe-only stops emit no synthetic events into FiveM's NUI. Class C applies the same owned-button rule.
2. **Owned Stop → Start lifecycle:** Fishing and Lockpicking share `OwnedRoutineWorker`, reject a restart until prior cleanup returns, cancel and wait up to three seconds, and fail closed on timeout. Class C cursor motion is also cancelled and awaited before replacement.
3. **Atomic sidecar lifecycle:** cloned Rust bridges share a start/stop lifecycle gate spanning process check, spawn, pipe ownership, and shutdown. Shutdown atomically takes the owned child and invalidates its reader generation.
4. **Bound asynchronous Lockpicking diagnostics:** image encoding, JSONL writes, and second-pass target tracing moved to a bounded writer. Trace sampling is capped at 10 Hz/900 entries, and retention keeps eight sessions within 500 MB.
5. **No focus stealing:** CuePilot no longer imports or calls `SetForegroundWindow`/`ShowWindow`. Automatic input waits up to ten seconds for the user to return to FiveM; Foreground-only mode still fails immediately.

The prior focused regressions and complete local release gate passed. Fishing was accepted by the user on 2026-09-03 and its live follow-ups are parked; separate exactly-one-click and F1/NUI recordings were not supplied.

### Completed — installed Velopack apply/relaunch proof

`scripts/test-velopack-update.ps1` now creates a disposable `CuePilotUpdaterSmoke` installation and proves a real 5.2.0 → 5.2.1 delta download, packaged engine-sidecar shutdown, apply, relaunch, version/payload replacement, and clean uninstall. It never uses the production `CuePilotDesktop` identity or legacy `%LOCALAPPDATA%\CuePilot` data.

### Implemented; Fishing accepted by user — one-click cast acceleration

**Files:** `src/Automation/AdaptiveRoutineEngine.cs`, fishing routine settings
and persistence, and focused engine tests.

- After a verified `E` cast action clears, CuePilot now waits about five
  seconds and sends exactly one short LMB click to advance the non-timing
  casting bar.
- It revalidates FiveM, capture, and input immediately before the click and
  skips it if the circular tension meter or another actionable prompt appears
  first.
- It never retries the click during the same cast, keeps `Pause / Break`
  release behavior intact, and records the action in local diagnostics.
- Live-test the delay separately from the existing 35–90 ms circular-meter
  tension controller.

**Why:** Advancing the casting bar as soon as it appears gets the line into
the water sooner without changing the later tension minigame behavior.

The repository contains automated coverage. The user accepted Fishing overall on 2026-09-03; no separate exactly-one-click recording was supplied. Further Fishing validation is parked at the user's request.

The following items were completed in 5.1.3 and are retained as short records:

- **Verify setup:** read-only target, input-capability, and capture-health
  verification is available before Fishing starts and is reused by preflight.
- **Fishing replay:** `--replay-fishing` replays ordered image frames through
  the production prompt/meter detectors with no routine or input path.
- **Bounded diagnostics:** Detection Review limits embedded decisive frames to
  6 MB each and 12 MB per response; omitted local frames remain in the folder.
- **Offline policy:** unused external Google Fonts CSP sources were removed.

### 1. Completed — no-input "Verify setup" check

**Files:** `src/Application/UiBridge.cs` (commands around 66-155),
`src/Automation/AdaptiveRoutineEngine.cs` (preflight around 135-183),
`src/Capture/FrameSources.cs`, `ui/src/lib/engine.svelte.ts`, and
`ui/src/App.svelte` (target panel around 686-764).

- Expose a read-only preflight command that reports FiveM target validity,
  foreground state, capture backend/latency, selected resolution/viewport,
  input-backend readiness, and registered shortcut state.
- Show one clear pass/fail card before the user presses F10, with the exact
  fix for any failed item.
- Never capture independently for long periods or send a key/mouse input.

**Why:** It would have made the Dev-F10 shortcut and unavailable-target
problems obvious before a fishing run.

### 2. Turn Detection Review into a decision timeline

**Files:** `ui/src-tauri/src/lib.rs` (diagnostic snapshot around 45-171),
`ui/src/App.svelte` (drawer around 948-1069),
`src/Diagnostics/FishingDebugSession.cs`, and tests beside
`tests/CuePilot.Tests/UiBridgeTests.cs`.

- Group the existing events into Cast, Meter, Tension, Catch, Collect, and
  Stop phases instead of showing primarily a raw 60-entry feed.
- Surface the first blocker, the last accepted detector decision, and the
  exact input sequence as a short plain-English summary.
- Add a copyable, privacy-safe diagnostic summary that excludes screenshots
  and window titles by default; full local evidence stays opt-in.

**Why:** A local session already holds high-quality evidence, but the useful
answer takes too much manual log reading when something goes wrong.

### 3. Completed — ordered Fishing replay command

**Files:** `src/Application/Program.cs` (analysis commands around 41-65 and
232-284), `src/Automation/FishingMeterDetector.cs`,
`src/Automation/FishingPromptDetector.cs`, and
`tests/CuePilot.Tests/FishingMeterTests.cs` / `FishingPromptTests.cs`.

- Replay an ordered directory of captured frames through the production meter
  tracker and prompt-state reader, with no input path available.
- Report state transitions, meter-lock losses, prompt suppression decisions,
  and per-frame timing in a compact summary.
- Use it to convert future user videos into repeatable fixture manifests.

**Why:** Current one-frame benchmarks prove recognition but cannot explain a
timing or tracker failure across a full minigame.

## Product improvements after that

### 4. Separate the Fishing workspace from the application shell

**Files:** split `ui/src/App.svelte` (currently more than 1,100 lines) into a new
`ui/src/lib/activities/FishingWorkspace.svelte`; keep app-wide windows,
shortcuts, connection state, and drawers in `App.svelte`.

- Mirror the existing `LockpickingWorkspace.svelte` activity boundary.
- Move fishing target/telemetry/cycle presentation into its own component.
- Keep the .NET engine authoritative; this is a UI maintainability change,
  not a detector rewrite.

**Why:** It makes future Fishing features safer to add and prevents one
component from owning every activity's presentation logic.

### 5. Add guarded Fishing tuning presets

**Files:** `src/Domain/RoutineModels.cs` (settings around 25-63),
`src/Application/AppSettings.cs`, `src/Application/UiBridge.cs`,
`ui/src/lib/engine.svelte.ts`, and the Fishing settings drawer in
`ui/src/App.svelte` (around 834-942).

- Offer named local presets for recognized server/minigame variations.
- Show the values that change and require an explicit apply action.
- Keep conservative bounds and a one-click restore-to-recommended default.
- Do not silently learn or alter clicking behavior while a routine is active.

**Why:** A transparent preset is safer than repeatedly hand-adjusting tension
timing when a server's minigame cadence changes.

### 6. Build a lockpicking evidence-capture checklist

**Files:** `ui/src/lib/activities/LockpickingWorkspace.svelte`,
`src/Automation/LockpickingObserverEngine.cs`,
`src/Diagnostics/`, and `docs/activities.md`.

- Present the exact missing evidence states in the Observe-only workspace.
- Mark a capture bundle complete only when it contains the required success,
  failure, transition, lighting, and obstruction examples.
- Make saved session review/replay one click from the workspace.

**Why:** Lockpicking's current fail-closed stance is correct; this would make
the work required to validate it obvious without prematurely enabling input.

### 7. Unblock Class C only through evidence, not a UI switch

**Files:** `src/Automation/LockpickingDetector.cs`,
`LockpickingObservationTracker.cs`, `LockpickingClassController.cs`,
`src/Application/UiBridge.cs` (currently rejects the start command around
104-105), and `tests/CuePilot.Tests/LockpickingDetectorTests.cs`.

- First validate literal labels 1, 2, 3, and 4 across the saved concurrent
  sequences and improve the replay report where evidence fails.
- Then run a single supervised Class C smoke test with all existing safety
  stops intact.
- Keep Classes A, B, and D observation-only until they have their own data.

**Why:** The controller exists, but the handoff evidence says concurrent label
recognition is not yet trustworthy enough for automated input.

## Release and quality improvements

### 8. Partially completed — release-readiness panel and artifact manifest

**Files:** `scripts/verify.ps1`, `ui/scripts/build-engine.ps1`,
`ui/src-tauri/tauri.conf.json`, and release documentation.

- `scripts/package-velopack.ps1` now emits a release manifest with app/shell/
  engine versions, Velopack version, artifact sizes, and SHA-256 values.
- Show that information in an About/Support panel and write it alongside
  local diagnostics.
- Add automated validation results to the manifest once the workflow can
  attest to the exact gate run rather than merely the packaged versions.

**Why:** It makes it much easier to tell which build a buddy is running and
which diagnostics belong to it.

### 9. Completed — make diagnostics size-bounded

**Files:** `ui/src-tauri/src/lib.rs` (`latest_debug_session` around 108-171),
`ui/src/App.svelte` (Detection Review around 948-1069), and the Rust
diagnostics tests in `ui/src-tauri/src/lib.rs`.

- Return session metadata and small thumbnails first, then load a full frame
  only when the user opens it.
- Keep a hard total response-size budget in addition to the existing per-image
  8 MB cap.
- Show when a frame is intentionally unavailable because it exceeds the local
  review budget, with an Open folder option still available.
- Lockpicking now also uses a bounded asynchronous writer and cross-session
  retention of eight sessions / 500 MB.

**Why:** The current bridge can base64 every decisive frame from a manifest in
one response. A busy session can make the diagnostic drawer slower than the
automation it is meant to explain.

### 10. Add UI resize and interaction regression coverage

**Files:** `ui/src/App.svelte`, `ui/src/app.css` (responsive rules around
2960-3130), `ui/src/lib/activities/*.svelte`, `ui/package.json`, and a new
browser/component test harness.

- Test activity selection, drawers, Escape/focus restoration, disabled target
states, and diagnostics loading without invoking game capture or automation.
- Exercise the supported 760px minimum window, normal 1180px layout, and wide
desktop widths; retain screenshot evidence for genuine visual changes.
- Keep reduced-motion behavior covered as part of the interaction suite.

**Why:** The CSS has thoughtful breakpoints and accessibility handling, but
the current UI test suite covers typed engine/activity data rather than the
actual interactive workspaces.

### 11. Make the bridge contract a shared fixture

**Files:** `src/Application/UiBridge.cs` (`ProtocolVersion` around 10),
`ui/src-tauri/src/engine_bridge.rs`, `ui/src/lib/engine.svelte.ts`, and the
existing .NET/Rust bridge tests.

- Define canonical ready/status/fault snapshots as versioned JSON fixtures.
- Verify each layer can emit or consume the same fixture, including missing
optional diagnostics fields and old hotkey shapes.
- Bump the protocol deliberately when a breaking bridge change is required.

**Why:** Three layers presently maintain compatible but separate JSON shapes;
focused tests exist, but shared fixtures would catch a compatibility mistake
before a packaged build reaches other people.

### 12. Partially completed — updater trust documented; code signing open

**Files:** `ui/src-tauri/tauri.conf.json`, `README.md`, `SECURITY.md`, and
the GitHub release workflow under `.github/workflows/release.yml`.

- Google Fonts CSP allowances are removed; the UI uses installed system fonts.
- Velopack now checks the public GitHub release feed, requires confirmation,
  verifies package hashes, and publishes a checked feed/manifest from CI.
- Keep screenshot/evidence sharing opt-in and clearly state what a copied
  diagnostic summary omits.
- Add Authenticode signing only with explicit certificate, identity,
  timestamping, renewal, revocation, and secret-ownership decisions. Until
  then GitHub/repository security and HTTPS are the update-channel trust root.

**Why:** The product correctly claims local-only operation, but the release
configuration should be as narrowly offline as the implementation.

## Suggested order

Use the expanded ranked list at the top of this document. The older numbered
items above remain detail/reference; their numbering is not today's execution
order. Fishing presets and Class C input are deferred. Authenticode signing
requires a separate identity/certificate decision.

## Guardrails

- Keep all capture, recognition, and input decisions in the .NET engine.
- Any new detector needs representative positive and negative fixtures at
  multiple scales/backgrounds plus a live smoke test.
- Do not enable additional lockpicking classes or Class C merely because a UI
  control exists; literal-label replay evidence is the gate.
