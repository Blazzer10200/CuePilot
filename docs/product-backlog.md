# CuePilot product backlog

This is a ranked later-work list based on the 5.2.0 release code, local
Fishing/Lockpicking diagnostics, automated gates, and a live UI inspection.
It is intentionally not an authorization to enable unverified automation.

## Next up

### Completed — safety and reliability blockers from the 2026-08-25 audit

1. **Owned-input release and stop gate:** Fishing Stop/fault/disposal bypasses foreground validation only for a key or mouse button CuePilot actually owns. Stop closes the input gate before publishing completion, so a late detector result cannot begin another press; idle and observe-only stops emit no synthetic events into FiveM's NUI. Class C applies the same owned-button rule.
2. **Owned Stop → Start lifecycle:** Fishing and Lockpicking share `OwnedRoutineWorker`, reject a restart until prior cleanup returns, cancel and wait up to three seconds, and fail closed on timeout. Class C cursor motion is also cancelled and awaited before replacement.
3. **Atomic sidecar lifecycle:** cloned Rust bridges share a start/stop lifecycle gate spanning process check, spawn, pipe ownership, and shutdown. Shutdown atomically takes the owned child and invalidates its reader generation.
4. **Bound asynchronous Lockpicking diagnostics:** image encoding, JSONL writes, and second-pass target tracing moved to a bounded writer. Trace sampling is capped at 10 Hz/900 entries, and retention keeps eight sessions within 500 MB.
5. **No focus stealing:** CuePilot no longer imports or calls `SetForegroundWindow`/`ShowWindow`. Automatic input waits up to ten seconds for the user to return to FiveM; Foreground-only mode still fails immediately.

The focused regressions and complete local release gate pass. The supervised exactly-one-click Fishing smoke and FiveM F1/NUI confirmation remain explicitly tracked live follow-ups; they were not represented as automated proof.

### Completed — installed Velopack apply/relaunch proof

`scripts/test-velopack-update.ps1` now creates a disposable `CuePilotUpdaterSmoke` installation and proves a real 5.2.0 → 5.2.1 delta download, packaged engine-sidecar shutdown, apply, relaunch, version/payload replacement, and clean uninstall. It never uses the production `CuePilotDesktop` identity or legacy `%LOCALAPPDATA%\CuePilot` data.

### Implemented; live validation still pending — one-click cast acceleration

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

The repository contains automated coverage, but there is still no recorded live proof that the accelerator sends exactly one click at the intended in-game phase. Keep that smoke test on the release gate.

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

1. During the next supervised FiveM session, confirm F1/NUI camera isolation and record the exactly-one-click cast validation.
2. Add UI resize/interaction coverage, then split the Fishing workspace.
3. Finish the Detection Review timeline and release-readiness/About panel.
4. Add guarded presets and the lockpicking evidence checklist.
5. Class C only after the evidence gate passes.
6. Add shared bridge fixtures; decide separately on Authenticode signing.

## Guardrails

- Keep all capture, recognition, and input decisions in the .NET engine.
- Any new detector needs representative positive and negative fixtures at
  multiple scales/backgrounds plus a live smoke test.
- Do not enable additional lockpicking classes or Class C merely because a UI
  control exists; literal-label replay evidence is the gate.
