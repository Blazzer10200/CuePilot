---
name: cuepilot-ui
description: Inspect, navigate, interact with, measure, and visually verify the running CuePilot Svelte/Tauri development UI through its local WebView2 CDP bridge. Use for CuePilot UI implementation, layout or typography checks, screenshots, accessibility inspection, console-error checks, and reproducing frontend interactions without desktop screen control.
---

# CuePilot UI

Use the repository's CDP bridge before desktop automation. It reads the live
WebView DOM, accessibility tree, computed layout, console errors, and pixels
without taking focus from the user.

## Connect

Run commands from the repository root unless noted otherwise.

```bash
npm --prefix ui run cdp:doctor
```

If the inspectable app is not running, launch it. If the wrapper is not
running, start it as a background process.

```bash
npm --prefix ui run cdp:dev
npm --prefix ui run cdp:serve
```

Reuse healthy processes. The inspectable launcher restarts only the
repository-owned development stack and never the installed application.

## Inspect and Act

Start with a screenshot-free structural probe:

```bash
bash ui/scripts/cdp/c.sh inspect
bash ui/scripts/cdp/c.sh map
bash ui/scripts/cdp/c.sh find "Settings"
bash ui/scripts/cdp/c.sh text main
bash ui/scripts/cdp/c.sh measure main
```

Use selectors returned by `map` or `find`; do not guess when discovery is
cheap. Use `act` for an input, DOM settling, console-error check, and screenshot
in one request.

```bash
bash ui/scripts/cdp/c.sh act click '[aria-label="Close settings"]'
bash ui/scripts/cdp/c.sh act key Escape
bash ui/scripts/cdp/c.sh look
```

Use `look [selector]` only when the claim is visual. Open the returned absolute
image path at original detail. Use `inspect`, `text`, or `measure` for structure,
copy, geometry, typography, and tokens.

## Verify Honestly

- Prove structure and copy with `inspect`, `map`, `text`, or `ax`.
- Prove layout, spacing, color, or overlap by examining a `look` screenshot.
- Prove interaction behavior with `act` and its settled result.
- Check `errors` after frontend changes; current-generation errors are separate
  from stale errors left by an earlier reload.
- Report unrun checks as unverified.

## Safety

- Do not click Start Automation, capture a game target, or change controller
  values unless the user asked for that behavior test.
- Use Settings, Diagnostics, and Escape for harmless navigation checks.
- CDP is development-only and loopback-only; do not enable it in release config.
- Never kill CuePilot by image name. Use the path-scoped inspectable
  launcher when a development restart is required.
- The bridge sees WebView content, not native OS dialogs or external game UI.
