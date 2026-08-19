# Handoff — CuePilot — 2026-08-19 CDT

## Current state

- CuePilot 5.1.3 is built and verified. The user-facing installer is
  `outputs/CuePilot_5.1.3_x64-setup.exe` with SHA-256
  `79807C9585E177F6D234936B3A32E80D0678E5FD19F38EC7A2A0DC57C2E5C94E`.
- Fishing uses one ROI/keycap/text/luminance-based detector for Ready, Casting,
  Waiting, Result, and Decision states. The action contract remains Cast/Keep
  only; no parallel recognizer or full-frame background template was added.
- Daylight/bush-video fixtures are under `tests/CuePilot.Tests/Fixtures/`.
  The supplied six-frame daylight sequence is covered by the production meter
  tracker and the new safe fishing replay regression.
- CuePilot Dev and release both attempt normal Windows F10/F9/Pause shortcut
  registration. A conflict is reported instead of silently making F10 a no-op.
- The target card now has **Verify setup**. It is read-only: target resolution,
  input capability, capture health/latency, and window dimensions are checked;
  no mouse or keyboard input is emitted. The Fishing routine reuses this check
  before it can begin.
- Detection Review now embeds at most 12 MB of decisive frames per snapshot
  (6 MB per frame). Omitted frames are still available through its local folder.
- Lockpicking Class C remains fail-closed and observation-only. Concurrent
  literal-label evidence is not yet sufficient to enable automated input.

## Completed verification

1. `pwsh -NoProfile -File scripts/verify.ps1 -All` — passed: 202 .NET tests,
   14 UI tests, 0 Svelte warnings, 8 Rust tests, and engine self-test.
2. `npm --prefix ui run tauri:build` — passed; NSIS installer created.
3. The staged release engine self-test passed and the copied installer hash
   matches the generated artifact.

## Useful commands

- Fishing replay (no capture or input):
  `dotnet run --project CuePilot.csproj -c Release -- --replay-fishing tests/CuePilot.Tests/Fixtures/Fishing`
- Full local validation:
  `pwsh -NoProfile -File scripts/verify.ps1 -All`
- Development UI (do not use it as a live-control smoke test):
  `npm --prefix ui run cdp:dev`

## Boundaries

- Keep capture, detector decisions, and all input in the .NET engine.
- Do not enable Class C merely because its UI command exists; literal-label
  replay evidence is the gate.
- Preserve the 35–90 ms Fishing LMB envelope and `Pause / Break` emergency
  release behavior.
