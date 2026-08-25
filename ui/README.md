# CuePilot Tauri UI

The Svelte/Tauri app is the only CuePilot product UI. It owns desktop
windowing and presentation while the packaged `--ui-bridge` sidecar remains the
sole owner of game capture, detection, input, and safety. Commands use a
versioned, correlated newline-JSON protocol over redirected stdin/stdout; no
network listener is opened. Installed builds separately use outbound HTTPS to
read CuePilot's public GitHub release feed through Velopack.

The root shell opens on an activity picker. Activity metadata lives in
`src/lib/activities.ts`, and dedicated workspaces live in
`src/lib/activities/`. Fishing is release-ready. Vehicle Lockpicking exposes an
input-free observer while Class C live-calibration input remains gated;
classes A, B, and D remain unavailable until evidence-backed profiles are added.

## Development

```powershell
npm install
npm run tauri:dev
```

`tauri:dev` stages the current Debug engine before starting Tauri, preventing a
stale sidecar. Run `npm test`, `npm run check`, and
`cargo fmt --manifest-path src-tauri\Cargo.toml -- --check`,
`cargo clippy --manifest-path src-tauri\Cargo.toml --all-targets -- -D warnings`,
and `cargo test --manifest-path src-tauri\Cargo.toml` for the focused frontend and bridge gates.
The root [development guide](../docs/development.md) documents the complete
cross-layer verification and release workflow.

## Focus-safe UI inspection

The development shell includes a local WebView2 CDP bridge adapted from Rift.
It can inspect the rendered DOM, accessibility tree, computed styles, console
errors, interactions, and screenshots without desktop screen control.

Start the inspectable app and the bridge from separate terminals in `ui/`:

```powershell
npm run cdp:dev
npm run cdp:serve
```

Then inspect it from Git Bash:

```bash
bash scripts/cdp/c.sh doctor
bash scripts/cdp/c.sh inspect
bash scripts/cdp/c.sh map
bash scripts/cdp/c.sh look
```

Use `map` or `find` before interaction, `act` for an action plus settled review,
and `measure` for exact layout and typography. Captures are generated under
`scripts/cdp/.tmp/` and are ignored by Git. CDP ports `9322` and `9323` bind to
loopback and are enabled only by `scripts/run-dev-inspectable.ps1`; normal
development and release builds remain unchanged.

Build the complete versioned release installer with:

```powershell
npm run tauri:build
```

The build wrapper stages an allowlisted Release shell and .NET engine, then
creates the Velopack installer, portable zip, update package/feed, checksum,
and release manifest under `../release/velopack/`. Velopack, not Tauri's
disabled bundler, owns the supported end-user package. The standalone engine
executable is an internal resource, not a separate end-user application.

Use `npm run tauri:build:portable` only to produce the raw shell and staged
engine consumed by the packager. Velopack exclusively creates and maintains the
installed `CuePilot` shortcuts. Development uses its own product identity,
WebView profile, and circuit-mark icon and runs through `npm run cdp:dev`; no
repository script repoints the official shortcut at a raw build.
