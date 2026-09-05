# Pickpocket reference frames

Source: user-supplied `clip-1788472296181-export-1788472359527.mp4`, 1920×1080, nominal 60 fps, received 2026-09-03. These lossless PNGs retain only the 680×300 panel crop at full-frame origin (620, 710). The crop avoids chat, player names, and unrelated overlays. It includes part of the game scene behind the translucent panel.

`clip-01` through `clip-13` correspond to zero-based native decoded frames 360, 410, 425, 433, 441, 450, 459, 463, 467, 468, 469, 480, and 535. `manifest.json` preserves presentation timestamps and expected states in chronological order. `clip-14` and `clip-15` are frames 434 and 449, where marker overlap previously distorted the inferred panel origin.

`sequence-<n>.png` adds frames needed for the consecutive 459–469 crossing; `crossing.json` references these and the existing clips without duplicating image files. Timestamps came from ffprobe's `best_effort_timestamp_time`, and extraction used FFmpeg `-fps_mode passthrough`. Do not infer time solely from frame number divided by 60.

The three embedded `assets/vision/pickpocket-header-*.png` masks are 110×18 crops at (672, 728) from frames 410 (Preparing), 441 (Active), and 480 (Grabbed). The detector uses bright glyph cores plus negative background points. These references establish only this observed theme/layout; other recordings remain necessary.

See `docs/pickpocket-evidence.md` for measured bar spans, state transitions, and limits. Offline replay and synthetic scale tests do not establish live input readiness.
