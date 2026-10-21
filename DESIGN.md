# CuePilot design brief

Warm neutral dark theme: flat gray surfaces, one terracotta accent. Dark only; there is no light theme.

The `:root` tokens in [ui/src/app.css](ui/src/app.css) and [ui/src/motion.css](ui/src/motion.css) are the source of truth. Use a token, never a new hex. If this file and the CSS disagree, the CSS wins and this file gets fixed.

## Fonts

| Role | Stack | Used for |
| --- | --- | --- |
| UI | `"Segoe UI Variable Text", "Segoe UI Variable", "Inter", "Segoe UI", sans-serif` | everything by default (`body`) |
| Display | `"Segoe UI Variable Display", "Segoe UI", sans-serif` | page, card and hero titles, big numbers |
| Mono | `"Cascadia Code", Consolas, monospace` | keys, version chip, process names, numeric inputs, step markers |

The pair is Segoe UI Variable + Cascadia Code. `body` sets `font-variant-numeric: tabular-nums`. Weights sit on the variable axis: 400 body, 560–620 labels, 650–680 titles; keep values like 620 and 680 as written instead of rounding to 600/700. Titles tighten letter-spacing to −0.02em … −0.04em.

Type scale (`--fs-*`, px): meta 11 · caption 12 · small 12.5 · body 13.5 · title 14 · card 22 · page 24 · display 34 · hero 40. Nothing below 11px.

## Colors

| Name | Token | Hex |
| --- | --- | --- |
| Background | `--bg` | `#1f1f1d` |
| Surface | `--panel` | `#262624` |
| Text | `--text` | `#ececea` |
| Accent (terracotta) | `--accent` | `#d97757` |
| Danger | `--danger` | `#e5695f` |

Everything else is a variant from the same `:root`: `--text-strong` / `--text-2` / `--text-3`, `--accent-strong` (hover) and `--accent-text` (accent-colored text), `--surface-drawer`, and the status pair `--success` / `--warning`. Borders are 1px `--line` (white 8%) or `--line-strong` (white 16%).

Filled accent is for the one primary action in a view (Start Fishing, the Pickpocket run button, the update dialog's primary button) and small state marks (live dots, the current-step bar, toggle on, progress fill). Everywhere else the accent is text, a border, a faint tint (`--accent-bg`, `--accent-soft`) or the focus ring.

## Spacing and radius

There is no spacing scale. These are the recurring values, by selector in `app.css`:

| Where | Value |
| --- | --- |
| Window (`ui/src-tauri/tauri.conf.json`) | 1180×760 default, 760×620 minimum |
| Page column (`main`) | max 1440px wide, 12px top / 14px bottom, 11px between sections |
| Rail to content (`.app-body`) | 20px |
| Card grid gap (`.activity-grid`, `.fishing-cards`) | 14px |
| Card padding | 20px (`.activity-card`), 18×20px (`.fishing-card`), 28×32px (`.fishing-hero`) |
| Drawer padding (`.drawer`) | 22px top, 24px sides, 20px bottom |
| Small gaps | 6px chips, 8px icon to label, 10px between actions |
| Control heights | 48 hero action · 44 primary/secondary · 40 inputs and drawer buttons · 36 toolbar · 28 segmented · 24–28 pills and chips |

Radius tokens (px): `--radius-kbd` 5 · `--radius-chip` 6 · `--radius-control` 8 · `--radius-button` 10 · `--radius-card` 14 · `--radius-panel` 16. Status pills, toasts and toggles are 999px.

## Surfaces, motion, icons

- Cards are flat: `--panel` fill, 1px border, no resting shadow. Shadows belong to floating layers (drawer, target picker, menus) and the activity-card hover lift.
- Focus: `--focus-ring` (3px accent at 28%) as a box-shadow, with `outline: none`.
- Motion: the `--dur-*` / `--ease-*` tokens in `motion.css`. Buttons never lift on hover, only cards do (`--lift-card`, −2px); press is `scale(0.98)`. Reduced motion zeroes the durations.
- Icons: Lucide (`@lucide/svelte`).

## Reference screenshot

[docs/design-reference.png](docs/design-reference.png): the Fishing workspace at 1180×760, from the `cuepilot-frontend` preview at `?scenario=history`. One flaw in it: the active rail item renders at 48% opacity because the global `button[disabled]` rule overrides the rail's `:disabled:not(.active)` exemption. Don't copy that.

`docs/cuepilot.png` (the README image) is the 5.1.0 teal design. It is not a reference.

## Never use

Purple-to-blue gradients, big rounded cards with soft shadows, emoji as icons, a centered hero with three feature boxes, cream backgrounds, pill buttons, numbered "01 / 02 / 03" labels, or lorem ipsum. Use real copy from the app.

How that applies here, so a look-and-fix pass leaves the real design alone:

- Gradients stay functional and faint: the tension track (accent to warning), the orbit gauge, and 6–13% tint washes (overlay cards, update hero, Pickpocket summary).
- Pills are status only. Buttons are 6–10px.
- The Fishing 1–5 stepper marks progress; it isn't decorative numbering.

When Claude picks a look that gets rejected, add it to this list.

## Legacy (don't copy)

- `--radius-sm` / `--radius-md` / `--radius-lg` (6 / 9 / 12px) date from 5.1.0 and are still in use. The named radius tokens arrived with the 2026-09-28 UI polish pass; new work uses those.
- Older sections hardcode greys (`#8c8883`, `#d6d5d4`, …) and 4.5–10% accent tints instead of the `--text-*` steps and tint tokens.
- 8.5–10.5px text in the target picker, the Settings delivery menu and the Pickpocket status footer.
