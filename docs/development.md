# Development guide

CuePilot has three runtime layers. Keep their responsibilities separate so UI work cannot bypass the safety checks around capture and input.

| Layer | Owns | Does not own |
| --- | --- | --- |
| Svelte (`ui/src`) | activity selection, presentation, settings forms, and local diagnostics views | game capture or input delivery |
| Tauri (`ui/src-tauri`) | desktop windowing, sidecar lifecycle, global emergency shortcut, and narrow local-file access | detection or automation decisions |
| .NET (`src`) | target validation, capture, detection, bounded input, routine state, and persisted settings | product UI |

The layers communicate through the versioned newline-JSON bridge. A bridge change is complete only when its .NET command/response, Rust transport, Svelte client, and focused tests agree.

Use the task-oriented [code map](code-map.md) before broad searches. It lists the Fishing, Lockpicking, bridge, UI, and packaging call paths plus their matching tests.

## Prerequisites

- Windows 11 with WebView2
- .NET 8 SDK
- Node.js 22 and npm
- stable Rust toolchain
- Velopack CLI 1.2.0 for release packaging (`dotnet tool install -g vpk --version 1.2.0`)

Install frontend dependencies once from the repository root:

```powershell
npm --prefix ui install
```

## Run the app

```powershell
npm --prefix ui run tauri:dev
```

The development command stages the current Debug .NET sidecar before Tauri starts. Do not launch an old staged engine manually.

## Verification

Run the complete local gate before handing off a batch:

```powershell
pwsh -NoProfile -File .\scripts\verify.ps1 -All
```

For a focused edit, use the matching gate first:

```powershell
# .NET engine and bridge
dotnet build .\CuePilot.sln -c Release
dotnet test .\tests\CuePilot.Tests\CuePilot.Tests.csproj -c Release --no-build
& '.\bin\Release\net8.0-windows10.0.19041.0\win-x64\CuePilot.exe' --self-test

# Svelte client
npm --prefix ui test
npm --prefix ui run check
npm --prefix ui run build

# Rust/Tauri bridge
cargo fmt --manifest-path .\ui\src-tauri\Cargo.toml -- --check
cargo clippy --manifest-path .\ui\src-tauri\Cargo.toml --all-targets -- -D warnings
cargo test --manifest-path .\ui\src-tauri\Cargo.toml
```

Detector changes also need representative positive and negative fixtures. A passing replay is not a substitute for the live minigame smoke test described in [Activity architecture](activities.md).

## Inspect the running UI

The development-only WebView2 bridge inspects the Svelte DOM without taking control of FiveM. Start these in separate terminals:

```powershell
npm --prefix ui run cdp:dev
npm --prefix ui run cdp:serve
```

Then use Git Bash from the repository root:

```bash
bash ui/scripts/cdp/c.sh doctor
bash ui/scripts/cdp/c.sh inspect
bash ui/scripts/cdp/c.sh map
bash ui/scripts/cdp/c.sh look
```

Use `map` or `find` before interacting. Do not exercise the global Start / Stop shortcut or target capture during a visual-only review. Generated captures stay under `ui/scripts/cdp/.tmp/` and are ignored by Git.

## Brand assets

`assets/branding/cuepilot-icon-source.png` and `cuepilot-dev-icon-source.png` are the canonical, untouched production and development artwork. Regenerate either cropped application PNG, multi-resolution Windows ICO, and lightweight UI icon by naming the asset explicitly:

```powershell
pwsh -NoProfile -File .\scripts\build-brand-assets.ps1 -Source .\assets\branding\cuepilot-icon-source.png -AssetName cuepilot
pwsh -NoProfile -File .\scripts\build-brand-assets.ps1 -Source .\assets\branding\cuepilot-dev-icon-source.png -AssetName cuepilot-dev
```

The script uses deterministic local resizing and rounded-corner masking; it does not redraw or reinterpret either logo. Tauri development builds use the separate `CuePilot Dev` product identity, application identifier, title, WebView profile, and circuit-mark icon so they remain visually distinct from the official build in the titlebar and Windows taskbar.

## Fixtures and diagnostics

- Regression fixtures belong under `tests/CuePilot.Tests/Fixtures/` and should be limited to the visual evidence required by the test.
- Review new fixtures for personal information, chat text, identifiers, and unrelated overlays before committing them.
- Runtime traces and annotated evidence under `%LOCALAPPDATA%\CuePilot\diagnostics\` are local artifacts, not repository content.
- Use `pwsh -NoProfile -File .\scripts\clean-workspace.ps1` to preview disposable build directories. Add `-Apply` only when those exact paths are safe to remove. The current release is preserved by default; `-StaleReleaseArtifacts` selects updater smoke/audit output and packaging staging while `-ReleaseArtifacts` selects the entire release directory. Dependency caches are preserved unless `-Dependencies` is supplied, and `-Captures` selects generated CDP screenshots.

### Instrumented Fishing sessions

Every Fishing start attempt from the configured global shortcut creates a bounded session under `%LOCALAPPDATA%\CuePilot\diagnostics\sessions\`. The engine records preflight, capture health, state transitions, prompt and meter decisions, named rejection gates, and intended input transitions. It reuses the frames already captured by the routine and saves only the strongest near-misses plus confirmed prompt/meter frames; the recorder never captures independently or sends input.

The newest five sessions are retained within an approximate 250 MB ceiling. Review the latest session in the Detection Review drawer or replay its decisive frames through the current detector build:

```powershell
dotnet run --project .\CuePilot.csproj -- --replay-session "$env:LOCALAPPDATA\CuePilot\diagnostics\sessions\<session-id>"
dotnet run --project .\CuePilot.csproj -- --replay-fishing .\tests\CuePilot.Tests\Fixtures\Fishing
```

Replay reports each frame's current decision and named gate. A confirmed frame that the current detector rejects returns a nonzero exit code, providing a deterministic regression loop without reopening FiveM.

`--replay-fishing` accepts an ordered directory of PNG/JPEG frames and reuses the live prompt-state and meter tracker without creating a routine or input router. It prints only state changes plus a summary, making it appropriate for daylight/video regression checks.

### Vehicle Lockpicking observation and Class C evidence gate

Start Lockpicking from its workspace. `Observe only` reuses the selected FiveM window and shared capture backend without sending input. Class C input is intentionally unavailable while saved concurrent-target evidence cannot prove every literal label. The workspace reports the HUD boundary, target/ring evidence, confidence, predicted action, capture timing, and observation state.

Starting Observe arms a waiting state and resumes capture when FiveM is foreground. Pause / Break, focus loss, capture failure, HUD disappearance, and uncertain states stop observation rather than guessing. Do not map the saved Class C timing evidence to A, B, or D.

Per-session saves cap non-numbered transitions at 72 frames, numbered evidence at 180 frames, and SPIN crops at 30 frames under `%LOCALAPPDATA%\CuePilot\diagnostics\lockpicking\<session-id>`. Image encoding, JSONL appends, and the optional target-trace detector run on a bounded background writer instead of the capture loop. Target traces are sampled at no more than 10 Hz and 900 frames per session. Cross-session retention keeps the newest eight sessions within a 500 MB total ceiling; cleanup is best-effort and runs when a new session starts. Each JSONL entry includes the source frame and crop bounds, capture timing, frame age/batch, and cursor-derived spin telemetry. Replay any saved or fixture frame through the same production detector:

```powershell
dotnet run --project .\CuePilot.csproj -- --analyze-lockpicking C:\path\to\full-frame.jpg
dotnet run --project .\CuePilot.csproj -- --replay-lockpicking C:\path\to\ordered-frames --fps 30
```

Before promoting another vehicle class, capture its own complete numbered and SPIN evidence, add regression fixtures, and calibrate its cadence independently. OPEN remains a terminal visual state unless direct evidence proves another required action.

## Release gate

Keep the version synchronized in `CuePilot.csproj`, `ui/package.json`, `ui/package-lock.json`, `ui/src-tauri/Cargo.toml`, and `ui/src-tauri/tauri.conf.json`. The Velopack Rust crate and `vpk` CLI must both remain exactly 1.2.0.

After the full automated gate and activity-specific live smoke test pass, build the same artifacts used by CI:

```powershell
npm --prefix ui run tauri:build
```

That command stages an allowlisted Release shell, self-contained .NET sidecar, icon, and license; it then writes `CuePilotDesktop-win-Setup.exe`, the full/delta package feed, portable zip, SHA-256, and `release-manifest.json` under `release/velopack/`. WebView2 is an explicit Velopack bootstrap requirement. Tauri's former NSIS bundler is disabled.

`npm --prefix ui run tauri:build:portable` builds only the raw shell and staged resource directory; it is an input to packaging, not the supported end-user installer. Do not replace the last known-good installed build solely because compilation or packaging succeeded. A tagged release must also verify the public GitHub assets and `releases.win.json` entry.

The first Velopack-enabled release is a one-time manual migration from the legacy Tauri NSIS install. Use pack ID `CuePilotDesktop`: the old `%LOCALAPPDATA%\CuePilot` install root also stores settings/diagnostics, so reusing it would put persistent data inside Velopack's replace/uninstall boundary. After the 5.2.0 Setup.exe is installed, later releases can check, download, apply, and relaunch in-app. For local feed/apply testing, build with the `update-test-feed` Cargo feature and point `CUEPILOT_UPDATE_FEED` at a generated feed; production builds ignore that local override.

Run the repeatable installed update test before tagging an updater change:

```powershell
pwsh -NoProfile -File .\scripts\test-velopack-update.ps1
```

It builds a feature-gated release shell, packages isolated `CuePilotUpdaterSmoke` 5.2.0 and 5.2.1 feeds, silently installs under ignored `release/velopack-smoke/`, downloads/applies the delta through the real Rust `UpdateManager`, starts and safely stops the packaged .NET sidecar, verifies relaunch plus the replaced payload marker, and uninstalls the test identity. The script fails if an installed process, registry entry, shortcut, or `current` directory remains. Use `-SkipBuild` only when the release binary was just built with `update-test-feed` and only packaging/install repetition is needed.

Velopack exclusively owns the installed `CuePilot` Start-menu and desktop shortcuts. Development runs through `npm --prefix ui run cdp:dev` or `scripts/launch-cuepilot-dev.ps1`; repository tools must never repoint the official shortcut at a raw build.
