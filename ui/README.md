# CuePilot Tauri UI

The Svelte/Tauri app is the only CuePilot product UI. It owns desktop
windowing and presentation while the packaged `--ui-bridge` sidecar remains the
sole owner of game capture, detection, input, and safety. Commands use a
versioned, correlated newline-JSON protocol over redirected stdin/stdout; no
network listener is opened. Installed builds separately use outbound HTTPS to
read CuePilot's public GitHub release feed through Velopack.

The root shell opens on an activity picker. Activity metadata lives in
`src/lib/activities.ts`, and dedicated workspaces live in
`src/lib/activities/`. Fishing is release-ready and Pickpocket makes one timed
Space press. Vehicle Lockpicking exposes an
input-free observer while Class C live-calibration input remains gated;
classes A, B, and D remain unavailable until evidence-backed profiles are added.

## Development

```powershell
npm install
npm run tauri:dev
```

`tauri:dev` stages the current Release engine before starting Tauri, preventing
a stale sidecar. Run `npm test`, `npm run test:e2e`, `npm run check`, and
`cargo fmt --manifest-path src-tauri\Cargo.toml -- --check`,
`cargo clippy --manifest-path src-tauri\Cargo.toml --all-targets -- -D warnings`,
and `cargo test --manifest-path src-tauri\Cargo.toml` for the focused frontend and bridge gates.
The root [development guide](../docs/development.md) documents the complete
cross-layer verification and release workflow. Current product status and
historical design records are organized in the
[documentation index](../docs/README.md).

## Focus-safe UI inspection

The development shell includes a local WebView2 CDP bridge for inspecting the
rendered DOM, console, interactions, and screenshots without desktop screen
control. Start it with `npm run cdp:dev` and `npm run cdp:serve`; the full
command set is in [scripts/cdp/README.md](scripts/cdp/README.md). CDP ports
`9322` and `9323` bind to loopback and are enabled only by
`scripts/run-dev-inspectable.ps1`; normal development and release builds remain
unchanged.

## Release build

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
