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
- Use `pwsh -NoProfile -File .\scripts\clean-workspace.ps1` to preview disposable build directories. Add `-Apply` only when those exact paths are safe to remove. Dependency caches are preserved unless `-Dependencies` is explicitly supplied; use `-Captures` to select generated CDP screenshots.

### Instrumented Fishing sessions

Every Fishing start attempt from the configured global shortcut creates a bounded session under `%LOCALAPPDATA%\CuePilot\diagnostics\sessions\`. The engine records preflight, capture health, state transitions, prompt and meter decisions, named rejection gates, and intended input transitions. It reuses the frames already captured by the routine and saves only the strongest near-misses plus confirmed prompt/meter frames; the recorder never captures independently or sends input.

The newest five sessions are retained within an approximate 250 MB ceiling. Review the latest session in the Detection Review drawer or replay its decisive frames through the current detector build:

```powershell
dotnet run --project .\CuePilot.csproj -- --replay-session "$env:LOCALAPPDATA\CuePilot\diagnostics\sessions\<session-id>"
```

Replay reports each frame's current decision and named gate. A confirmed frame that the current detector rejects returns a nonzero exit code, providing a deterministic regression loop without reopening FiveM.

### Vehicle Lockpicking observation and Class C run mode

Start Lockpicking from its workspace. `Observe only` reuses the selected FiveM window and shared capture backend without sending input. `Run Class C` explicitly arms the Class C controller: each numbered click requires a stable target and a one-shot temporal READY prediction; SPIN requires two matching frames and uses the live-recorded 1,140°/s clockwise cadence at 0.61× HUD radius for no more than 2.8 seconds. The workspace reports the HUD boundary, target/ring evidence, confidence, predicted or executed action, capture timing, action count, and SPIN state.

Starting Observe arms a waiting state and resumes capture when FiveM is foreground. Starting Class C arms normal physical mouse input only after the selected FiveM window is foreground. Pause / Break, focus loss, capture failure, HUD disappearance after input, repeated unexpected states, OPEN confirmation, or the SPIN time limit stops input rather than guessing. Class C is the only automatic lockpicking class; do not map its timing to A, B, or D.

Evidence is bounded to 72 frames per session under `%LOCALAPPDATA%\CuePilot\diagnostics\lockpicking\<session-id>`. Normal state transitions retain their full-frame context. SPIN additionally retains at most 30 HUD crops at approximately 12 Hz; each JSONL entry includes the source frame and crop bounds, capture timing, frame age/batch, and cursor-derived spin telemetry. Replay any saved or fixture frame through the same production detector:

```powershell
dotnet run --project .\CuePilot.csproj -- --analyze-lockpicking C:\path\to\full-frame.jpg
dotnet run --project .\CuePilot.csproj -- --replay-lockpicking C:\path\to\ordered-frames --fps 30
```

Before promoting another vehicle class, capture its own complete numbered and SPIN evidence, add regression fixtures, and calibrate its cadence independently. OPEN remains a terminal visual state unless direct evidence proves another required action.

## Release gate

Keep the version synchronized in `CuePilot.csproj`, `ui/package.json`, `ui/src-tauri/Cargo.toml`, and `ui/src-tauri/tauri.conf.json`. After the full automated gate and the activity-specific live smoke test pass, build the NSIS installer with:

```powershell
npm --prefix ui run tauri:build
```

The installer build stages the Release sidecar automatically. Do not replace the last known-good installed build solely because compilation succeeded.

For a portable release executable without creating an installer, run `npm --prefix ui run tauri:build:portable`. After producing a verified release executable, install or refresh the two desktop shortcuts with:

```powershell
pwsh -NoProfile -File .\scripts\install-desktop-shortcuts.ps1 -OfficialExecutable C:\path\to\cuepilot-ui.exe
```

`CuePilot` opens that fixed release build. `CuePilot Dev` runs `scripts/launch-cuepilot-dev.ps1`, stages the current Debug engine, and launches the inspectable development shell. Their stable shortcut icons are copied under `%LOCALAPPDATA%\CuePilot\branding` so checkout moves do not break icon rendering.
