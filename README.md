<p align="center">
  <img src="assets/branding/workflow-looper-icon.png" width="128" alt="Workflow Looper icon">
</p>

<h1 align="center">Workflow Looper</h1>

<p align="center">Application-bound minigame automation for Windows.</p>

Workflow Looper targets a specific game window, reads live visual state, and runs a deterministic fishing controller.

![Workflow Looper dashboard](docs/workflow-looper.png)

## Fishing profile

1. Select **Set target**, switch to FiveM, and wait for the target capture.
2. Leave input delivery on **Auto · Focus FiveM** for verified physical scan-code input.
3. Select **Start automation**.
4. Preflight resolves FiveM, verifies capture, activates the target, and checks input.
5. The loop verifies the Cast prompt before pressing `E`, detects and controls the circular meter, verifies Keep Fish before collecting, then waits for the next verified Cast prompt.

Every LMB hold is independently capped at 35–90 ms by the feedback controller. LMB is never sent outside the active circle minigame.

## Dashboard

- Target, capture, and input health are visible together.
- Current automation state, detector confidence, and processed samples are shown live.
- Advanced tuning is collapsed until needed.
- `Pause / Break` is the global emergency stop and releases held input.
- If FiveM stops being the active visible window, the routine stops safely rather than reading or clicking another application.

## Input modes

- **Auto · Focus FiveM** — activates FiveM and uses physical scan codes. Recommended.
- **Experimental · Background** — sends application-addressed messages. FiveM may reject them; it does not enable unattended tabbed-out fishing.
- **Foreground only** — refuses input unless FiveM is already foreground.

Meter capture uses the visible desktop frame. Keep FiveM visible and foreground while automation is active; covered-window operation is not supported.

## Diagnostics

Numeric fishing traces are stored under:

```text
%LOCALAPPDATA%\WorkflowLooper\diagnostics\
```

They contain detector measurements, high-level loop transitions, and input state, never captured game frames.

Useful commands:

```powershell
dotnet test .\tests\WorkflowLooper.Tests\WorkflowLooper.Tests.csproj -c Release
dotnet run --project .\WorkflowLooper.csproj -c Release -- --self-test
dotnet run --project .\WorkflowLooper.csproj -c Release -- --target-probe FiveM_b3258_GTAProcess
dotnet run --project .\WorkflowLooper.csproj -c Release -- --capture-probe FiveM_b3258_GTAProcess
dotnet run --project .\WorkflowLooper.csproj -c Release -- --input-probe FiveM_b3258_GTAProcess
```

## Project structure

- `src/Application` — startup, persistence, and window shell.
- `src/Presentation` — dashboard, advanced settings, and focused UI controls.
- `src/Automation` — fishing detector, controller, and state machine.
- `src/Capture` — visible-desktop frame capture.
- `src/Input` — foreground and experimental application input backends.
- `src/Platform` — Windows target resolution and interop.
- `tests/WorkflowLooper.Tests` — fishing, migration, input, and capture contracts.

## Safety

- Run Workflow Looper and FiveM at the same Windows integrity level.
- No anti-cheat bypass, injection, stealth, or detection-evasion behavior is included.
- The emergency stop always attempts to release LMB and `E`.

## License

[MIT](LICENSE)
