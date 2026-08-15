# Handoff — CuePilot — 2026-08-15 03:10 CDT

## Current Objective

- Make Class C lockpicking input safe for concurrent, independently shrinking numbered bubbles; Class B remains Observe-only.

## Current State

- Development UI is running through the inspectable CDP launcher and was last verified engine-online with zero UI errors. It has not been restarted after the latest source edits.
- C evidence: `20260815-013127` is the user-designated authoritative three-target advanced-lockpick rhythm. Targets 2 and 3 are already visible/shrinking while target 1 becomes ready.
- B evidence: `20260815-014806` records four concurrent targets; session `20260815-014440` is an additional observed run. B has no input profile and must stay Observe-only.
- Latest focused lockpicking tests passed: 40/40; `dotnet build CuePilot.sln -c Release` is clean.
- The tracker now maintains independent spatial tracks for every observed target. A track can reach READY only after its own stable, literal label and its own two-frame/80 ms bright dwell.
- Source is still not ready for a new Class C run: saved C replay cannot identify every literal label. In the authoritative C sequence, early frames are unlabeled and later candidates report duplicate `2` labels, so Class C must remain fail-closed.

## Recent Relevant Changes

- `LockpickingObservation` now carries a `Targets` collection, preserving detected candidates instead of only the old selected target.
- Observe mode writes local per-frame candidate traces to `candidate-trace.jsonl` (`lockpick-target-trace-v1`).
- Added a two-fresh-frame, 80 ms bright-fill dwell gate before tracker READY.
- Added `HasLiteralNumber`; `LockpickingClassController` rejects inferred target numbers and accepts only literal detector labels.
- Added an initial `RecognizeTargetNumber` glyph heuristic; it is experimental and not field-validated.
- Added `ClassCControllerRejectsInferredTargetNumber` regression coverage.

## Known Problems

- The old tracker no longer assigns inferred sequence numbers. Stable tracks preserve an established literal label across outline/bright transitions, but a conflicting label permanently makes that track ambiguous.
- The glyph heuristic still fails on C target 1 and reports duplicate target-2 labels in saved C frames; do not enable/restart Class C with this source.
- `--analyze-lockpicking` only surfaces one active candidate; it now prints labels/literal state but cannot validate all C targets alone.
- The user is frustrated by repeated premature progress reports. Do not report readiness or request another Class C test until replay proves literal labels and target timing on the saved C sequence.

## Next Actions

1. Improve `RecognizeTargetNumber` against saved C/B frames; validate 1, 2, 3, and B's 4 before relying on it.
2. Add a local-only replay harness that reports per-track label consistency and candidate glyph features, then tune the recognizer only against that evidence.
3. Add privacy-reviewed deterministic fixture coverage only if approved; current concurrent-target synthetic regressions cover the tracker/controller safety boundary.
4. Restart `npm --prefix ui run cdp:dev` and verify CDP only after literal-label replay passes, then request one controlled Class C test.

## Relevant Files

- `src/Automation/LockpickingDetector.cs`
- `src/Automation/LockpickingObservationTracker.cs`
- `src/Automation/LockpickingClassController.cs`
- `src/Automation/LockpickingObserverEngine.cs`
- `src/Application/Program.cs`
- `tests/CuePilot.Tests/LockpickingDetectorTests.cs`
- Local evidence: `%LOCALAPPDATA%\CuePilot\diagnostics\lockpicking\20260815-013127`, `20260815-014440`, `20260815-014806`

## Canonical Commands

- Focused test: `dotnet test tests/CuePilot.Tests/CuePilot.Tests.csproj -c Release --filter "FullyQualifiedName~LockpickingDetectorTests"`
- Inspect saved frame: `dotnet run --project CuePilot.csproj -- --analyze-lockpicking C:\path\to\frame.jpg`
- Development UI: `npm --prefix ui run cdp:dev`; then `npm --prefix ui run cdp:doctor`
- Full validation: `pwsh -NoProfile -File scripts/verify.ps1 -All`

## Important Decisions

- C and B both use concurrent target countdowns; target order or screen position must never imply a literal label.
- B receives no automated-input implementation from the current observation alone.
- Class C must fail closed for unknown/ambiguous identity; Pause / Break remains the emergency input release.
- Restart the development app after source changes; a passing `dotnet test` build is not the running desktop process.
