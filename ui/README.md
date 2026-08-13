# Workflow Looper Tauri UI

The Svelte/Tauri shell is intentionally separate from the .NET fishing engine.
The shell can only ask the local `--ui-bridge` process to snapshot, start, stop,
or capture a target; the engine remains the sole owner of capture and input.

## Development

```powershell
.\scripts\build-engine.ps1
npm install
npm run tauri dev
```

For a release installer, stage the release sidecar first:

```powershell
.\scripts\build-engine.ps1 -Release
npm run tauri build
```
