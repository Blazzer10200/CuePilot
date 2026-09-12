# CuePilot (root)

## Current direction
- Svelte/Tauri is the only product UI; the packaged .NET executable is a headless local engine sidecar.
- Capture, detection, input control, routine orchestration, and safety remain authoritative in .NET.
- Keep migration in sync with [`HANDOFF.md`](HANDOFF.md).

## Fast repository map
- `src/` = headless .NET engine and runtime logic
  - `Automation/` = detector/routine engine orchestration (`AdaptiveRoutineEngine`, detector implementations)
  - `Diagnostics/` = bounded Fishing and Lockpicking evidence writers plus headless self-test
  - `Capture/`, `Input/`, `Platform/` = frame source + input action + host platform hooks
  - `Application/` = engine startup, settings, and the versioned local bridge
- `ui/` = Svelte + Tauri front-end
  - `ui/src/` = app shell and state wiring
  - `ui/src/lib/activities.ts` = typed activity registry and readiness metadata
  - `ui/src/lib/activities/` = activity picker and activity-specific workspaces
  - `ui/src-tauri/` = Rust bridge shell
- `tests/` = xUnit engine/bridge test suite and fixture assets
- `assets/` = model/vision helper assets
- `docs/development.md` = canonical setup, verification, inspection, and release workflow
- `docs/README.md` = documentation index and maintenance conventions
- `docs/history/` = completed plans and acceptance records, not current instructions
- `docs/activities.md` = activity boundary and readiness contract
- `docs/code-map.md` = task-oriented call paths, edit points, and search recipes

## High-signal edit points
- Start with:
  - `src/Automation/AdaptiveRoutineEngine.cs`
  - `src/Automation/RoutineWorker.cs`
  - `src/Automation/*Detector*.cs`
  - `src/Automation/LockpickingClassProfiles.cs`
  - `src/Automation/LockpickingClassController.cs`
  - `src/Automation/LockpickingObserverEngine.cs`
  - `src/Application/UiBridge.cs`
  - `src/Diagnostics/LockpickingDiagnosticSession.cs`
  - `ui/src/App.svelte`
  - `ui/src/lib/activities.ts`
  - `ui/src/lib/engine.svelte.ts`
  - `ui/src/lib/updates.svelte.ts`
  - `ui/src-tauri/src/update_service.rs`
  - `ui/src-tauri/src/engine_bridge.rs`
  - `scripts/package-velopack.ps1`

## Runtime boundaries
- Keep the .NET side authoritative for capture + detection + safety checks.
- Keep UI calls side-channeled through the bridge and commands.
- Avoid changing transport/security assumptions without matching updates on both sides of the bridge.

## Local validation
- Full local gate: `pwsh -NoProfile -File scripts/verify.ps1 -All`
- `.NET`: `dotnet build CuePilot.sln -c Release`, `dotnet test tests/CuePilot.Tests/CuePilot.Tests.csproj -c Release`
- `UI`: `npm test --prefix ui`, `npm run check --prefix ui`, `npm run build --prefix ui`
- `Rust`: `cargo clippy --manifest-path ui/src-tauri/Cargo.toml --all-targets -- -D warnings`, `cargo test --manifest-path ui/src-tauri/Cargo.toml`
- `Package`: `pwsh -NoProfile -File scripts/package-velopack.ps1`

## Search and navigation defaults
- Use `rg` for code search.
- Start unfamiliar or resumed work with `pwsh -NoProfile -File scripts/project-status.ps1`, then follow `HANDOFF.md` and `docs/code-map.md`.
- Use `docs/README.md` to find maintained guides; run `pwsh -NoProfile -File scripts/verify.ps1 -Docs` after documentation/tooling edits.
- `tmp/` is noise output from sessions and should be treated as non-source.
- Generated Tauri schemas, dependency locks, build output, and CDP captures are excluded by `.rgignore`; address them explicitly when the task actually concerns them.
- Preview repository cleanup with `pwsh -NoProfile -File scripts/clean-workspace.ps1`; dependency caches are opt-in and should normally be preserved for faster iteration.
- Prefer these entry points after edits:
  - run `rg` from repo root
  - inspect files directly with explicit paths from this map
  - use `.rgignore` for quick scans

## Tauri UI inspection
- Use the repo-local `$cuepilot-ui` skill for live Svelte/Tauri UI work.
- Start with `bash ui/scripts/cdp/c.sh inspect`; use `map` or `find` to discover selectors.
- Use `act` for interaction plus settled verification, and `look` only when pixels matter.
- Diagnose availability with `npm --prefix ui run cdp:doctor`; the supported inspectable launcher is `npm --prefix ui run cdp:dev`.
- CDP is development-only. Never enable it in release configuration or kill CuePilot by image name.
