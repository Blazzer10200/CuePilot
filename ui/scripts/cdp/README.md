# CuePilot WebView UI bridge

This development-only bridge exposes the running Svelte WebView through a
compact local inspection API:

```text
c.sh -> HTTP wrapper 127.0.0.1:9323 -> WebView2 CDP 127.0.0.1:9322
```

## Start

From `ui/`, launch the app and wrapper in separate terminals:

```powershell
npm run cdp:dev
npm run cdp:serve
```

The launcher uses a separate WebView profile and handles elevated shells by
starting the dev app at medium integrity. It stops only development processes
whose executable path belongs to CuePilot's Cargo target.

## Core workflow

```bash
bash scripts/cdp/c.sh doctor
bash scripts/cdp/c.sh inspect
bash scripts/cdp/c.sh map
bash scripts/cdp/c.sh find "Settings"
bash scripts/cdp/c.sh measure main
bash scripts/cdp/c.sh act click 'button.sub-action:nth-of-type(2)'
bash scripts/cdp/c.sh act key Escape
bash scripts/cdp/c.sh look
```

- `inspect [selector]` returns current page state, console errors, and the
  accessibility tree without a screenshot.
- `map [selector]` inventories actionable controls with reusable selectors.
- `find <text>` searches labels, text, roles, titles, and placeholders.
- `text [selector]` returns normalized rendered copy.
- `measure <selector>` returns geometry, typography, colors, borders, and child
  measurements.
- `act click|key|type` performs the input, waits for DOM quiescence, and returns
  the settled page, console errors, and a screenshot.
- `look [selector]` writes a screenshot and reports current page state/errors.

Run `bash scripts/cdp/c.sh` without a command for the full command list.

Generated screenshots live in `scripts/cdp/.tmp/`. The wrapper retains the
newest 20 by default; set `CUEPILOT_CDP_TMP_KEEP` to change that limit.

## Boundaries

The bridge binds only to loopback and is enabled only by the inspectable dev
launcher. It is not present in Tauri release configuration. It sees WebView
content, not native Windows dialogs or external applications. Do not exercise
the global Start / Stop shortcut or target capture unless that behavior is explicitly under
test.
