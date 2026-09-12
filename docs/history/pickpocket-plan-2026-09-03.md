# Pickpocket implementation plan — 2026-09-03 (historical)

> Historical implementation plan and calibration record. Subsequent live
> automatic grabs and the 5.3.5 detector recovery supersede its outcome and
> next-test language. Use [Pickpocket debugging](../pickpocket-debugging.md)
> for the maintained operational guide and `HANDOFF.md` for the latest result.

## Outcome at plan date

The live observation path is implemented in .NET and connected to the Svelte/Tauri workspace. The user confirmed that items and positions randomize each attempt and that a completed attempt has a three-minute cooldown. Fishing is accepted and parked.

Automatic chooses the widest upcoming detected region. Preferred-color options are available, but colors are not claimed to identify an item. Four references retain ten observed item names. Two manual live tests verified sampling and result recording; the user then authorized a controlled one-tap automatic test. Observe remains the default. SingleAttempt mode is implemented, with explicit per-run selection, bounded timing, owned Space release, and no automatic rearming. Actual automatic game acceptance remains unverified.

## Detector and timing

- `PickpocketDetector` finds the observed panel from header masks and marker geometry. It extracts 1–16 known-color regions without depending on their count, order, spacing, or uniqueness. Previous geometry accelerates tracking but is validated against the current header.
- Known region colors are white, purple, red, pale green, blue, and yellow. Header states are Preparing, Active, Grabbed, and Missed. The second clip establishes a right-edge bounce and a missed result; prediction now handles either direction after fresh motion samples. Other themes/colors and timeout outcomes need clips. Synthetic transforms and shuffled regions are regression coverage, not proof of new live layouts.
- `PickpocketTimingPredictor` fits up to eight presentation-timestamped samples over 140 ms, estimates residual/localization uncertainty, and subtracts a delivery-delay envelope. It rejects stale/nonmonotonic images, reversed/stopped motion, layout changes, ambiguous targets, and insufficient usable width.
- Selection refers to validated current geometry/color, allowing duplicate colors and preventing an occluded region from transferring selection to the next list slot. A hypothetical candidate latches the attempt; missing frames or a direction reversal do not rearm it.
- `--replay-pickpocket <manifest> --target-color Purple` remains a no-input offline path.

## Live observer and cooldown

- `PickpocketObserverEngine` owns capture through `OwnedRoutineWorker`. It waits for the selected FiveM window to become foreground, then stops on focus loss/minimization, cancellation, capture failure, or a ten-minute limit.
- Capture is restricted to the lower center viewport. Active observation targets a 16 ms period; waiting/cooldown targets 67 ms. UI/evidence updates target 100 ms, with state transitions and candidates published immediately.
- DXGI presentation age plus analysis duration feeds the predictor. GDI can display detections but cannot generate timing candidates because its presentation timestamp is unverified. The 0–16 ms delivery envelope remains a simulation assumption.
- `PickpocketAttemptTracker` starts a monotonic 180,000 ms cooldown on Grabbed/Missed, or conservatively after three consecutive missing frames following Active. During cooldown no predictions are generated; repeated result frames do not extend it. Stop/restart retains the deadline in the same engine process. Closing/restarting the engine loses that session state.
- `PickpocketDiagnosticSession` writes active-frame JSONL, lossless PNGs, startup metadata, and an independent final report beneath the normal local diagnostics directory. Per run: queue capacity 128/four pending images, 12,000 trace records/32 MiB, 128 images/64 MiB with sixteen critical image slots reserved. A full queue drops evidence instead of waiting on disk; the UI and report show skipped entries and write errors. No automatic deletion was introduced. See `docs/pickpocket-debugging.md`.
- .NET bridge commands configure/start/stop the observer. Activity starts and target/settings changes enforce engine lifecycle exclusions; emergency Stop and shell shutdown stop observation. Tauri only forwards the allowlisted commands.
- F7 toggles observation globally by default; configure it through Pickpocket Settings. F8 is reserved for FiveM's console and excluded/rejected for this activity. Saved settings add the binding without replacing other shortcuts; a free fallback is chosen if an older profile already uses F7. Policy changes update the engine before shortcut activation. Pause / Break remains emergency stop.
- The UI shows current regions, selected target, marker, capture/analysis/frame-age timing, hypothetical candidate count, observer status, evidence path, and cooldown countdown. A recorded trace is never substituted for live data.

## Original next step: first controlled automatic live test

The automatic debugger now includes actual host key-down/up timestamps and automated tap count, alongside active-frame decisions and saved results. Choose One-tap test in CuePilot Dev, target Automatic, then F7 in FiveM before starting the minigame. Let the app press Space, and stop with F7 after the result. See `docs/pickpocket-debugging.md` for the current workflow and limits. The prior implementation stages below are historical context; observation evidence and bounded delivery implementation are complete, while automatic live acceptance is still pending.

1. When the user is ready, select Pickpocket → Live observation → Start observing, return to FiveM, and perform an attempt manually. Inspect saved evidence and capture/frame-age measurements.
2. Three supplied clips now cover two successful items, a missed press, a bounce, and randomized layouts. They are sufficient to begin the live observation trial. Unknown colors and full timeouts remain future evidence gaps. Verify item-to-region correspondence before offering named live item selection.
3. Measure a conservative capture/input scheduling envelope on this machine. Cropped detector speed alone does not measure GPU readback, key delivery, or game acceptance.
4. Implement a single bounded Space tap with owned-input cleanup and a monotonic deadline. Revalidate foreground, Stop, fresh capture, stable target, cooldown, and expiry immediately before delivery. Do not queue a long blind timer.
5. Complete the activity readiness requirements in `docs/activities.md` before marking automatic input ready. The clip's ~53 ms purple interval and ~14 ms red interval are visual estimates, not verified acceptance windows.

## Verification — 2026-09-03

- Third-clip follow-up: three selectable reference recordings, eight known items, and separate saved choices fit at both tested sizes. A marker-fringe fix preserves the white region during crossing. 63 focused pickpocket tests, 28 UI tests, Svelte check/build, and all three full clip state replays pass. The third clip has 430 native frames/401 stable labels with no state mismatch; the live geometry-selection path has a retained 19-frame white-crossing regression. Details and color-only replay limitations are in `docs/pickpocket-evidence.md`.
- 65 focused .NET pickpocket/bridge tests passed, including both clips, leftward prediction, missed-result suppression/cooldown, randomized counts/spacing, repeated colors, selection under occlusion, attempt latching, three-minute boundary, cooldown restart retention, evidence output, and foreground loss.
- UI: 24 tests passed; Svelte check found no errors or warnings; production build passed.
- CuePilot Dev was visually checked through CDP at 1180×760 and 760×620. Following the user's request for a fitted UI, both live and reference views now fit these sizes without page scrolling: combined navigation/tabs, compact controls and reference cards, side-by-side timing/cooldown, and shorter explanatory copy. No current console errors. Reference/live navigation and keyboard selection work; default restored to Widest. Start is enabled with the selected FiveM target, but was not clicked. Development app and CDP wrapper remain running.
- Rust: Clippy with warnings denied and all 11 tests passed after closing the development app that held the staged sidecar file open.
- Broad .NET run: 278 passed, two existing Fishing timing-budget tests failed under concurrent compilation. Both passed in isolated follow-up runs; Fishing code was not changed by this batch. The later focused run includes the additional foreground-loss test.
- Clip one: 146 frames, zero labeled mismatches, one hypothetical purple press. Clip two: 547 frames, zero labeled mismatches, one white candidate, no yellow candidate because the narrow interval fails the uncertainty envelope. Latest white replay detector mean 1.060 ms, p95 2.761 ms. These exclude decoding, live capture and input.
- First user-run live capture is analyzed in `docs/pickpocket-evidence.md`: manual blue grab, absent Purple policy, slow sampling and full-image budget exhaustion. Timer/crop fixes are in the running Dev build; follow-up report improvements await restart while the user is busy. Post-fix live cadence and automatic key-delivery accuracy remain unmeasured.
- Shortcut/polish follow-up: 77 focused .NET persistence/bridge/pickpocket tests, 25 UI tests, and 12 Rust tests passed; Svelte check/build and Clippy passed. F7 binding was saved through the live Settings drawer (temporary F6 round-trip, restored to F7), with F8 absent from the picker. The fitted 760×620 view/drawer and restored 1180×760 view were checked through CDP. Closing Settings restores focus to its opener. No physical in-game shortcut/capture trial was run.

Retained crossing replay:

```powershell
dotnet run --project CuePilot.csproj -c Release -- --replay-pickpocket tests/CuePilot.Tests/Fixtures/Pickpocket/crossing.json --target-color Purple
```
