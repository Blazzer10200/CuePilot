# Handoff batch history — 5.3.7 to 5.3.9

> Archived 2026-09-22 from `HANDOFF.md`. Dated session records of how 5.3.7
> through 5.3.9 were built, verified, and installed. Current state is in
> [HANDOFF.md](../../HANDOFF.md); user-facing changes are in the
> [changelog](../../CHANGELOG.md). File paths and states describe their date.

## 5.3.9 Launch speed and Pickpocket tracking — 2026-09-20

Driven by two reported problems: a white screen for roughly ten seconds at
launch, and Pickpocket still missing too often.

**Launch.** Instrumented the shell first rather than guessing. The window
appears at 160-260 ms but stays blank until 5.6-6.3 s, measured at both
elevated and medium integrity. The frontend is not the cause: mount to first
engine command is 68 ms, and the notification window costs 96 ms. About 5.35 s
is spent inside Tauri/WebView2 before `setup()` runs. Ruled out the JS bundle
size, the second WebView, and `tauri_plugin_single_instance` (disabled, rebuilt,
re-measured, no change, then restored).

Instrumenting an installed build settled where the time goes: `builder_ready`
lands at 1-2 ms, so every piece of work the shell does itself — Velopack, the
panic hook, `UpdateService`, the whole builder chain — accounts for about two
milliseconds. The lazy-updater change was correct but saved nothing measurable.
The full ~5.3 s is inside Tauri/WebView2 window creation. Ten launches fell in a
40 ms band (5275-5314 ms), which reads like a fixed timeout rather than variable
work. Ruled out by A/B: WebView2 browser feature flags, background networking,
and component update, all zero difference. Also ruled out a corrupt or missing
profile (the 69 MB persisted profile is healthy) and an outdated runtime
(153.0.4234.48 is current). A WPAD/proxy-resolution timeout is the surviving
hypothesis and is **untested** — the measurement run was stopped at the owner's
request.

Three things landed. `UpdateService::new()` no longer constructs Velopack's
`UpdateManager` during `.manage()`; it is resolved on first use through
`Inner::ready()`. The window declares `backgroundColor` `#1f1f1d`. And, since
the gap cannot currently be removed, `ui/src-tauri/src/splash.rs` covers it: a
plain Win32/GDI window, about 300 lines, put up on its own thread immediately
after Velopack and torn down on the first command from the webview.

Web content cannot do this job. During the blank period WebView2 is precisely
what is still starting, so an HTML splash has no renderer — and Vite emits a
render-blocking stylesheet, so nothing paints before CSS loads even once it
does. Tauri's documented splashscreen pattern has the same flaw, since it uses a
second WebView. GDI has no such dependency.

The window is `WS_POPUP` with `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, topmost and
shown with `SW_SHOWNOACTIVATE`, so it never steals focus from a game. It must be
topmost or the main window would activate above it. It closes three ways: the
webview's first command, a 3 s backstop thread after `setup_end` for a frontend
that never loads, and a 30 s `KillTimer` ceiling. `hide()` posts `WM_CLOSE`, not
`WM_DESTROY` — posting the latter would run the destroy handler and end the
message loop while leaving the window on screen. A `CANCELLED` flag plus a
re-check after the handle is published closes the two races where `hide()` and
window creation interleave; without it a `hide()` during the 150 ms pre-show
delay would be silently dropped and strand the splash for the full 30 s.

**None of this has been seen running** — see the deferred section above.

**Pickpocket.** Confirmed the bands were already sub-pixel, so the gain had to
come from analysis cost and fit robustness, not from precision of the geometry.
The detector re-scanned the whole region every frame, twice on failure, at a
16 ms cadence; it now scans only the columns the tracked marker can reach and
falls back to the full scan whenever the result is not a clean continuation of
the panel it was following. Measured against `HEAD` over three runs each:
tracked-frame mean 0.96 ms to 0.50 ms.

Hoisting `HeaderSearch` out of `ScanStems` into `Analyze` fixed a regression the
localized pass introduced — each pass was allocating its own bounded budget, so
a missing-header frame spent it twice and broke the 100 ms bound at 111.7 ms.
One budget per frame is what the class comment always asked for, and it also
lets the recovery pass reuse the first pass's header results: worst measured
erased-header frame 37.4 ms to 20.5 ms.

In the predictor, the overloaded reset gate was split into distinct cases
(out-of-order timestamp, capture gap, stopped or repeated frame, genuine
reversal) with a sub-pixel jitter floor, so centroid noise can no longer fake a
reversal and discard a good sweep. The residual is now the second-worst frame
once six samples exist, so a single bad frame no longer both vetoes the shot and
inflates the uncertainty that sizes the target window. A first attempt also
relaxed the duplicate-frame rule; that broke
`FrozenReversedAndRepeatedFramesInvalidateMotion` and was reverted, because a
frozen or repeated capture must never be able to schedule a press. **No timing
constant was touched** — `nominalDelay`, the Red/Yellow advances,
`MaximumFrameAgeMs` and `MaximumHorizonMs` all require live evidence.

**Tests.** `PickpocketPixelLatencyTests` asserts a 100 ms wall-clock bound but
was running in parallel with unrelated image suites, which is the same mistake
the Fishing collection already existed to prevent. That collection is renamed
`Detector timing` and now also holds the Pickpocket latency, input, and
live-evidence suites — every suite that reads a real clock. Suites that inject
their own timestamps stay parallel: `LockpickingDetectorTests`' apparently
fragile `InRange(..., 49, 51)` is arithmetic on supplied values, not wall clock.
This closes the flaky-test issue the backlog carried. Suite time 13 s to 22 s;
4 consecutive full runs clean.

## 5.3.8 Closeout — 2026-09-18

The user asked to wrap up: stop the dev stack, clean up, update docs, install an official build, and delete old builds. Done in this order.

- **Version 5.3.8** across the six release files (`CuePilot.csproj`, `ui/package.json`, both `ui/package-lock.json` root fields, `ui/src-tauri/Cargo.toml`, the `cuepilot-ui` entry in `Cargo.lock`, `ui/src-tauri/tauri.conf.json`). The CHANGELOG "Unreleased" entry became `## 5.3.8 - 2026-09-18`.
- **Gate.** `verify.ps1 -All` green: docs 21 files / 129 links / 0 errors, dotnet 444/444, vitest 8 files (51), `npm run build` ok, Playwright 23/23, rustfmt clean, clippy clean, cargo test 28/28. Two things surfaced on the way: (1) `PickpocketInputTests.EngineRequiresNewPreparationAndRecordsOneTapThenDisarms(SingleAttempt, true, 1)` failed once under the full parallel run and passed 3/3 alone; its synthetic pipeline flips to Grabbed after a real 1000 ms, so it is wall-clock sensitive like the Fishing timing tests (not changed; candidate for the nonparallel collection). (2) The gate runs `cargo fmt --check`, which the new Rust files failed; `cargo fmt` was applied (formatting only).
- **Package.** `scripts/package-velopack.ps1 -Version 5.3.8 -OutputDirectory release/velopack-5.3.8` → `CuePilotDesktop-win-Setup.exe` (45.6 MB, sha256 `cc73819f…`), `CuePilotDesktop-5.3.8-full.nupkg` (41.3 MB), portable zip, feed files, `release-manifest.json`. Unsigned as always.
- **Install.** Applied through the installed updater from a medium-integrity one-shot scheduled task (Claude runs elevated): `Update.exe apply --silent --norestart --package …full.nupkg`. `install.log` ends with "Package version 5.3.8 applied successfully"; Desktop and Start Menu shortcuts were refreshed by Velopack. Installed `current\cuepilot-ui.exe` and `resources\engine\CuePilot.exe` SHA-256 match the built binaries. A normal-session launch check started the shell and the `--ui-bridge` sidecar, showed the "CuePilot" window, and closed cleanly with no leftover processes. `%LOCALAPPDATA%\CuePilot\settings.json` and `pickpocket-state.json` are byte-identical to the backup in `tmp/userdata-before-538/`. Receipt: `release/velopack-5.3.8/installed-receipt.json`.
- **Cleanup.** Dev app, sidecar, dev WebViews, `cdp:serve`, and the Vite preview on 1430 were stopped. `release/velopack-5.3.5`, `-5.3.6`, `-5.3.7`, and `velopack-next` were deleted at the user's request (the 5.3.4 publication receipts in `release/velopack/` were kept as the public-release record). CDP captures cleared via `clean-workspace.ps1 -Captures -Apply`, which exposed and fixed a script bug: with a single target `$summary` was a scalar and `+=` threw `op_Addition`; it is now built with `@(...)`. `tmp/` (5.7 GB Cargo dev cache, dev WebView profiles, user-data backups) was left alone per policy.
- **Docs.** `docs/code-map.md` gained a "Shortcuts and desktop notifications" route; `CLAUDE.md` release note updated; README already described the capture field.
- **Not done on purpose.** No commit, tag, push, or GitHub release (not requested). The installed 5.3.8 UI was not inspected beyond the launch check (release builds have no CDP). In-game behaviour of the new shortcuts, mouse hook, and pickpocket confirmation is untested.

## Neutral theme, rail, and cleanup — 2026-09-18

Uncommitted on top of the 5.3.7 batch. Recorded in `CHANGELOG.md` (originally "Unreleased", now the 5.3.8 entry).

- **Theme.** `ui/src/app.css` and the scoped component styles now use a warm neutral dark-gray palette (`--bg #1f1f1d`, `--panel #262624`, white-alpha lines, `--text #ececea`) with one terracotta accent (`--accent #d97757`). The teal/navy palette, the `main::before` ambient glow, the `@property` accent transition, and the per-activity violet/blue accent blocks are gone (the user rejected the purple). Semantic colors kept: amber warning, red danger, soft green success, and the Pickpocket item colors (Purple `#ac88ef`, Blue `#7cd5ee`) which are game-item identities, not theme. Accent CTAs use light text. Scrollbars are neutral with arrow buttons hidden globally. `ui/src/overlay.css` follows the same tokens.
- **Rail.** `App.svelte` wraps the content in `.app-body` (sticky 60 px `.rail` + `.app-content`). Rail buttons carry `data-rail`, `main` carries `data-workspace`; only Home cards carry `data-activity`, which the Playwright specs rely on.
- **Fishing rebalance.** The user found the hero band (eyebrow, title, detail, orbit) too open against the dense stack below it. `App.svelte` now wraps the Fishing body in `.fishing-body` (two columns: `.instrument` with the target card over telemetry, and `aside.fishing-run` holding the `.cycle` stepper plus `.actions`). In `app.css` the hero keeps `flex: 1` while the body takes `flex: 2`, the stepper runs vertically with a left accent bar, and the h1/orbit shrank slightly. `(max-width: 900px)` stacks the columns; the `(max-width: 900px) and (max-height: 720px)` compact set restores the single-row stepper and lets the body absorb spare height. Selectors, aria labels, and the e2e contract are unchanged. Notification popups (`Preview fishing` / `Preview pickpocket`) were screenshot-checked over CDP in the new palette and look right.
- **Any-key / mouse shortcuts.** The user wanted to bind "any keybind on my mouse, keyboard, anywhere". The Settings `<select>`s (F6–F12) are gone. `ui/src/lib/hotkeys.ts` (pure, vitest-covered) owns key naming (`describeKey`, `hotkeyDisplay`, `sameHotkey`), the supported-code table (mirrors global-hotkey's parser), and `bindingFromKeyboardEvent` / `bindingFromMouseEvent` / `conflictFor`. `ui/src/lib/HotkeyCapture.svelte` is the field: click → listening state (dashed accent border, pulsing dot, held modifiers previewed as caps) → capture-phase window listeners with `preventDefault`/`stopPropagation` so Escape and the focus trap don't fire → commit or inline error (taken by X / reserved / unsupported). Left/right click cancel (and the follow-up click is skipped once), Escape cancels, window blur cancels. It calls `invoke("shortcut_capture", { active })` around the listening window; `scenarios.ts` mocks it. `App.svelte` passes `bind:value`, `defaultBinding`, and `taken={takenShortcuts(...)}` (other drafts + emergency stop) to three instances. Rust: `mouse_shortcuts.rs` installs a `WH_MOUSE_LL` hook on its own thread (windows-sys 0.59, Windows-only), maps middle/X1/X2 down events plus `GetAsyncKeyState` modifiers to `Ctrl+Shift+Alt+MouseX1` text, asks `dispatch_mouse_shortcut` (lib.rs) and swallows the down and matching up when bound. `EngineBridge` gained `capture_suspended` (gates both routers), `command_for_mouse`, `registered_shortcut` (overlay label), `set_capture_suspended` (unregisters/re-registers keyboard slots), and `is_mouse_shortcut`; `sync_registered_shortcut` stores mouse bindings without touching the plugin. `run_shortcut_command` is shared by the plugin handler and the hook. Engine: `SettingsStore.IsValid` also rejects `Escape`, `MouseLeft`, `MouseRight`, and W3C modifier codes; `HotkeyBinding.DisplayText` prettifies `KeyA`/`Digit1`/`Numpad7`/`MouseX1`. Tests: 9 vitest, 4 Rust, 3 xUnit, 1 Playwright (`e2e/hotkeys.spec.ts`). Not tested: a real Mouse 4/5 press against the running hook (Playwright cannot send X buttons; middle click is covered) and the hook against FiveM.
- **Pickpocket shortcut confirmation.** The user wanted proof from in-game that the keybind did something. `run_shortcut_command` (lib.rs) now keeps the engine's response and hands it to `notifications::confirm_shortcut`, which for `toggle_pickpocket_observe` enqueues `Notice::pickpocket_shortcut(snapshot, label)`: "Pickpocket started" + "Armed for one precision tap. Press Mouse 4 again to stop." (mode text from `pickpocket.inputMode`), or "Pickpocket stopped". `Notice.detail` became a `String`; the pump now honors each notice's own `duration_ms` (confirmations run 3.2 s, readiness stays 4.5 s). `Preferences` gained `shortcuts: bool` (serde default true, so old `notifications.json` files keep it on); `NotificationSettings.svelte` has the "Shortcut confirmations" checkbox and a "Preview shortcut" button (`preview_notification("shortcut")` uses the real bound label via the new `engine_bridge::shortcut_label`). Errors on the toggle still go through the existing fault toast, so a press with FiveM closed shows the fault, not a confirmation. Fishing was deliberately left without a confirmation (not asked). Not tested: a real in-game press (needs FiveM); the preview path was screenshot-checked.
- **Layout fix.** Pickpocket Timing view overflowed 760×620 by 19 px; `PickpocketLiveWorkspace.svelte` now hides `.legend` under the `(max-height:650px)` rule. `ActivityPicker.svelte` card refs are `$state` (removes the Svelte non-reactive binding warning).
- **CDP bridge.** `c.sh` gained `state`, `nav`, `tour`, `console`, `reload`; `tour` presses Escape before every stop so an open drawer never blocks the next click.
- **Engine.** Bounds guards added to `PickpocketDetector.Pixels.MarkerAt` and `FishingPromptDetector.PixelBuffer.IsNeutralAt/IsDarkAt` (callers already pre-validated; this matches the other detectors). Lockpicking distance helpers use `dx*dx + dy*dy` instead of `Math.Pow`. Detection thresholds and behavior unchanged.
- **Docs.** Removed the redirect-only `docs/pickpocket-plan.md` and `docs/app-debugging-roadmap.md`; `docs/pickpocket-evidence.md` links the dated history record directly.
- **Audited, left alone on purpose.** The Class C lockpicking cluster (`LockpickingClassProfiles.cs`, `LockpickingClassController.cs`) is reachable only from tests because `UiBridge` gates Class C input until its evidence gate passes; it is a deliberate gate, not dead code. `scripts/write-verification-receipt.ps1` is a documented manual release tool. `AGENTS.md`/`CLAUDE.md` remain parallel by design (Codex vs Claude).

Verification this session: svelte-check 0/0, vitest 51/51, Playwright 23/23, `npm run build` ok, clippy clean, cargo test 28/28, `dotnet build` 0 warnings, `dotnet test` 444/444, `verify.ps1 -Docs` green, `c.sh errors` 0 current. Surfaces screenshot-checked: Home, Fishing, Pickpocket (Live/Timing), Lockpicking, Settings, Diagnostics. Not tested: live gameplay, notification popups against a game window.

## Repository relocation — 2026-09-18

- The repository moved to `C:/AI Workflow/projects/cuepilot/`, alongside the other active projects.
- It previously lived inside a Codex session sandbox at `C:/Users/BLAZZER/Documents/Codex/2026-08-10/c-users-blazzer-videos-snipping-tool/work/WorkflowLooper/`, in a folder still named for the app's pre-rename identity.
- The move was a same-volume rename. Git history, the `origin` remote, branch `codex/release-complete`, and every uncommitted 5.3.7 change were preserved and verified afterward.
- No source, script, or configuration file referenced the old absolute path. The Cargo dev cache did: `tmp/cargo-dev/debug/build/*/output` for tauri, tauri-plugin-global-shortcut, webview2-com-sys, vswhom-sys, ring, and cuepilot-ui embedded the old `WorkflowLooper\tmp\cargo-dev` OUT_DIR, and tauri-build failed with "failed to read plugin permissions … os error 3" on the first `cdp:dev` after the move. Fixed 2026-09-18 with a package-scoped `cargo clean -p` of those six crates (6.6 GiB, regenerable); the dev app then built and launched normally.
- Claude-side harness added 2026-09-18 and gitignored (matches rift-tauri): `CLAUDE.md`, `.claude/launch.json`, `.claude/skills/cuepilot-ui/`. `AGENTS.md` stays tracked for Codex.
- Left behind in the old sandbox folder: `WorkflowLooper-GitHub-v2/` (an August 10 copy), the three `Workflow Looper` backup executables, and that session's scratch media. None were moved or deleted.

## 5.3.7 Readiness notifications — 2026-09-12

The desktop readiness notifications were completed, visually approved by the user, packaged, and installed. The user explicitly requested no further testing before that release build.

**State at the time**

- Source version was 5.3.7 across the six release files, including both npm lockfile root fields and the Cargo application entry.
- The optimized release and Velopack installer/full package were built under `release/velopack-5.3.7/` (since deleted during the 5.3.8 cleanup).
- The normal Windows user session applied the full package through the existing installed updater. Updater exit code was 0; installed shell and engine versions were 5.3.7.
- The official desktop shortcut was launched after updating. The release UI was not inspected or behavior-tested after the user requested testing stop.
- Saved settings and pickpocket history were backed up and remained unchanged during installation.
- The development app, inspection wrapper, and temporary installation task were stopped/removed.

**Changes**

- Adapted Clipping Software's separate non-activating, click-through notification window and Windows system sound pattern.
- Popups appear at the primary display's top right, with display scaling, a 4.5-second duration, activity icon, and draining progress line.
- Pickpocket readiness follows the engine's cooldown deadline, including restored snapshots and stopped observation. Duplicate timestamp drift is suppressed; a bridge disconnect cancels pending readiness.
- Fishing alerts fire once when Casting transitions to Armed: a new cast has started and the engine is watching for the meter.
- Rust owns delivery, sound, and expiry so minimized WebViews cannot throttle timing. A bounded queue and listener registration replace the old lossy raw-event poll.
- Settings includes immediately saved popup/sound toggles and preview buttons. Preferences are stored separately in the Tauri app configuration directory.
- Closing the main window exits the shell, preventing the hidden notification window from keeping CuePilot alive.
- Engine detection, gameplay input, and safety logic are unchanged.

**Verification** (all of it preceded the user's request to stop further testing)

- 21 Rust tests passed, including cooldown expiry, duplicate suppression, disconnect handling, fishing transitions, and monitor scaling.
- 42 frontend unit tests passed.
- Five existing workspace browser scenarios and two new notification settings scenarios passed. The save-failure test exposed and verified a checkbox rollback fix.
- Svelte check: zero errors or warnings. Rust Clippy with warnings denied passed. Frontend production build passed.
- Native development previews displayed both activities at 125% scaling, 16 logical pixels from the primary monitor's top/right work-area edges. Both screenshots were visually checked; no current frontend console errors.
- The user approved the displayed notification design.
- After that approval, only release construction and installation completion/data-preservation checks ran.

**Relevant files**

- `ui/src-tauri/src/notifications.rs`: readiness tracking, native delivery, settings, sound, placement, and regression tests.
- `ui/src-tauri/src/engine_bridge.rs`: engine-event and snapshot integration.
- `ui/src-tauri/src/lib.rs`: notification setup, commands, and shutdown.
- `ui/src/Overlay.svelte`, `ui/src/overlay.css`: notification presentation.
- `ui/src/lib/NotificationSettings.svelte`: preferences and previews.
- `ui/e2e/notifications.spec.ts`: settings persistence and save-failure coverage.
- `tmp/notification-pickpocket.png`, `tmp/notification-fishing.png`: approved native previews.
- `tmp/userdata-before-537/`: pre-update settings/history backups.

**Decisions**

- The user accepted the preview and explicitly waived further testing for that local build/install.
- Release notification defaults are popups on and sound on; sound follows the Windows sound scheme and volume.
- Development and production notification preferences use their respective Tauri application identities.
- Previous snapshot archived at `C:/Users/BLAZZER/.codex/archive/handoffs/WorkflowLooper/HANDOFF-20260912-065500-before-notifications537.md`.
