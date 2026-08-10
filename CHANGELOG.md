# Changelog

## 3.0.0

- Rebuilt the interface around responsive DPI-aware layouts and consistent Fluent-style icons.
- Removed built-in tap presets and the preset drawer.
- Added a precision event editor with timing analysis, normalization, undo/redo, duplication, deletion, and enable/disable controls.
- Added version 2 pattern migration, atomic saving, `.bak` protection, and per-pattern execution settings.
- Added target-window capture and foreground safety checks.
- Added a reusable triggered routine with physical left-click handoff, drift-free tapping, `E` collection, cooldown, and re-arming.
- Added physical rhythm calibration and optional local visual-cue change detection.
- Added a formal xUnit test project, richer self-test coverage, portable ZIP packaging, and SHA-256 release checksums.

All notable changes are documented here.

## 2.1.0

- Replaced native window chrome with a draggable, fully themed title bar.
- Replaced native numeric spinners, checkbox, preset menu, and library scrollbar with custom controls.
- Added a Settings page for capturing, validating, saving, and resetting global shortcuts.
- Added conflict detection and persistent local shortcut settings.
- Added a structured animated preset chooser with full preset details.
- Rebalanced the complete layout and removed clipped labels and path text.
- Added custom-control and shortcut-settings coverage to the self-test.

## 2.0.0

- Added a persistent pattern library with automatic most-recent loading.
- Added clear-current, confirmed delete, open, and Save As actions.
- Added Rapid Tap, Steady Tap, Balanced Hold, and Slow Hold presets.
- Added animated Studio/Guide page transitions and live status motion.
- Added a custom dark preset menu, refined typography, and cleaner layout.
- Kept cursor capture and playback disabled by default for click/key accuracy.
- Added preset validation to the deterministic self-test.
- Added GitHub build and tagged-release workflows.

## 1.1.0

- Added high-resolution waitable-timer playback.
- Preserved exact event spacing and variable click duration.
- Made mouse-movement capture optional.

## 1.0.0

- Initial local macro recording and playback release.
