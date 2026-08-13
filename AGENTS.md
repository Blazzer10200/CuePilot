# WorkflowLooper (root)

## Current direction
- This repo is mid-migration from the legacy WinForms dashboard to a Svelte/Tauri shell.
- Primary logic remains in .NET for capture, detection, input control, and routine orchestration.
- Keep migration in sync with [`HANDOFF.md`](HANDOFF.md).

## Fast repository map
- `src/` = .NET application and runtime logic
  - `Automation/` = detector/routine engine orchestration (`AdaptiveRoutineEngine`, detector implementations)
  - `Capture/`, `Input/`, `Platform/` = frame source + input action + host platform hooks
  - `Application/` = app startup and bridge entry points
  - `Presentation/` and `Diagnostics/` = legacy UI + safety/diagnostic hooks
- `ui/` = Svelte + Tauri front-end
  - `ui/src/` = app shell and state wiring
  - `ui/src-tauri/` = Rust bridge shell
- `tests/` = NUnit test suite and fixture assets
- `assets/` = model/vision helper assets

## High-signal edit points
- Start with:
  - `src/Automation/AdaptiveRoutineEngine.cs`
  - `src/Automation/*Detector*.cs`
  - `src/Application/UiBridge.cs`
  - `ui/src/lib/App.svelte`
  - `ui/src-tauri/src/lib.rs`

## Runtime boundaries
- Keep the .NET side authoritative for capture + detection + safety checks.
- Keep UI calls side-channeled through the bridge and commands.
- Avoid changing transport/security assumptions without matching updates on both sides of the bridge.

## Local validation
- `.NET`: `dotnet restore`, `dotnet build WorkflowLooper.sln`, `dotnet test`
- `UI`: `npm run check --prefix ui`, `npm run build --prefix ui`
- `Rust`: `cargo check --manifest-path ui/src-tauri/Cargo.toml`

## Search and navigation defaults
- Use `rg` for code search.
- `tmp/` is noise output from sessions and should be treated as non-source.
- Prefer these entry points after edits:
  - run `rg` from repo root
  - inspect files directly with explicit paths from this map
  - use `.rgignore` for quick scans
