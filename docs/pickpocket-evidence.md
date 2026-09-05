# Pickpocket video evidence

## Current evidence — 2026-09-03

User clarification after the UI review: item identities and positions are randomized on each attempt, and additional clips will show different items/layouts. Treat every popup as a new layout. The observed four colors, item order, spacing, and item-to-color mapping are properties of this recording only. Whether card slots themselves stay evenly spaced and how colors map across items still require more footage; do not assume either is invariant.

User-provided source: `C:/Users/BLAZZER/Videos/Clips/FiveM_b3258_GTAProcess/clip-1788472296181-export-1788472359527.mp4`.

The user describes pressing Space when the moving slider overlaps an item's colored region. The supplied clip shows one successful Luxury Watch grab. No miss, timeout, full-width traversal, reversal, or second attempt is shown. Fishing is accepted by the user and parked.

## Recording and measurements

- Container duration: 10.618 seconds. Video: 1920×1080, nominal 60 fps, 634 decoded frames, first presentation timestamp 0.016667 seconds. The clip has two audio streams; analysis here is visual only.
- Measurements use native decoded frames and their `best_effort_timestamp_time`, not frame index divided by nominal fps. Default FFmpeg output synchronization duplicates frames, so extraction used `-fps_mode passthrough`.
- The bar occupies approximately x=672–1247, y=928–950. Its enclosing UI includes four item cards, a top status/countdown line, and the bottom `Press SPACE to grab item` prompt.
- Contrary to the initial description, the moving marker in this recording is lime green: a thin vertical stem with arrowheads above and below the bar. The broad white band is stationary and matches Rope.
- A clip-specific offline pixel measurement finds the marker outside the colored track using its green stem/arrowheads. Between 7.2 and 7.7 seconds, a least-squares fit gives approximately 282.8 pixels/second rightward motion (about 4.7 pixels per nominal frame). This is one observed speed, not a universal setting.

| Item shown | Stationary band | Approximate x span | Visible width | Estimated crossing time |
| --- | --- | --- | --- | --- |
| Rope | White | 728–807 | 80 px | 283 ms |
| Luxury Watch | Purple | 888–902 | 15 px | 53 ms |
| Ruby | Red | 1022–1025 | 4 px | 14 ms |
| Broken Electronic Part | Pale green | 1132–1171 | 40 px | 141 ms |

Band spans were measured on row y=938 near 7.5 seconds. The marker occludes part of the Rope band; its width includes that occlusion. Compression, anti-aliasing, marker thickness, and unobserved game hit testing mean these are visible spans and estimated traversal durations, not proven input acceptance windows. The Ruby estimate is shorter than one 60 fps frame, so this recording cannot resolve its full timing behavior.

## Observed sequence

1. The player selects Pickpocket from the interaction menu before the bottom UI appears.
2. Around 6.6–7.1 seconds, the UI fades in with `PICKPOCKET / GET READY`; the marker waits at the left edge. Color detection alone must not arm a press during preparation.
3. Around 7.08 seconds, the countdown is active and the marker begins moving right. The initial countdown is approximately eight seconds.
4. At 7.8167 seconds (decoded frame 467), the marker is around x=888, at the purple region's left edge. Frame 468 at 7.8333 seconds shows x=892 and the active countdown.
5. At 7.8500 seconds (frame 469), the marker is around x=896 and the header changes to `GRABBED / LUXURY_WATCH`. The watch card is emphasized and the marker remains frozen. This is the first observed success frame, not a measurement of when the physical Space press occurred.
6. The result remains visible briefly and fades away around 8.8–9.0 seconds. A Luxury Watch item-added notification appears afterward. The Space prompt remains visible during the grabbed result, so it cannot be the sole active-state signal.

## Implementation implications

- Pickpocketing now has its own observe-only activity following `docs/activities.md`. Keep detection, timing, and input authority in .NET.
- Locate the complete panel/bar structure relative to the captured viewport. Do not ship these single-resolution coordinates or this clip's item order as fixed assumptions.
- Detect colored target intervals separately from the green moving marker; its protruding arrowheads distinguish it from the pale-green item interval.
- Track timestamped marker positions over multiple fresh captures. Estimate direction and speed, then predict an interior target crossing with a margin for capture and input delay. Do not bake in 283 px/s or assume motion remains linear across attempts.
- Model preparation, active motion, successful result, and disappearance separately. Failure/timeout states still need evidence. Suppress repeated input after one attempted grab until an observed new attempt is established.
- The desired item must be an explicit activity choice or user-defined priority. Do not infer that the narrowest region is always wanted or that rarity proves value.
- First implementation should support offline replay/observation with positive and negative fixtures. A single successful clip does not complete the activity's input-readiness requirements.

## Next evidence

- A missed Space press and/or timeout, including what the UI does afterward.
- A different item layout or target, preferably including the narrow Ruby region.
- An uninterrupted traversal to establish whether the marker wraps, reverses, or stops at the edge.
- Target choice was resolved after this analysis: the user approved a UI chooser and a sensible default. Live observation now defaults to the widest upcoming region, with preferred-color options. Named item recognition remains unverified.

## Implementation status

The detector now acquires the panel from its header and marker geometry, independently of region count/order/spacing. Live observation, timing telemetry, bounded evidence, and an engine-owned 180-second cooldown are connected; automatic Space delivery is absent. Shuffled synthetic layouts, duplicate colors, cooldown boundaries/restart, and focus loss have regression coverage. The 146-frame replay still has zero labeled mismatches and one hypothetical purple press (latest cropped detector mean 2.263 ms, p95 4.083 ms under concurrent build load). These results do not prove real key-delivery timing. See `docs/pickpocket-plan.md`.

## Local analysis artifacts

### Second clip — missed attempt, 2026-09-03

Source: `C:/Users/BLAZZER/Videos/Clips/FiveM_b3258_GTAProcess/clip-1788474408979-export-1788474756564.mp4`. 1920×1080, nominal 60 fps, 547 decoded native frames, 9.184 seconds video. Original timestamps are preserved; frame count is not used as a substitute for presentation time.

- Observed cards, left to right in this clip: Ring (purple), Cuff Medicine (blue), TNT Recipe (yellow), Loose Change (white). Names are reference observations, not runtime recognition rules. The new arrangement confirms that item/color positions differ from clip one.
- Stable Preparing detection begins around 3.817 s; Active at 4.100 s. The marker travels right at roughly 280–300 px/s, reaches the right edge, and is visibly returning left by about 6.083 s. This is a bounce, not a new attempt; the predictor now reacquires motion after a reversal without clearing its attempt latch.
- The yellow interval is roughly x=1022–1025 in the full frame (about 3 px), approximately 10 ms at the observed speed. The blue interval is about 35 px and white about 92 px. The default widest-region strategy selects white in this layout.
- `MISSED` is first detected at 6.900 s. The bar becomes red and the marker is near x=1016, just left of the yellow interval. If TNT Recipe was the intended target, the visible result is consistent with pressing after that interval on the return pass; the recording alone does not expose the exact key-delivery timestamp. The bar disappears around 7.817 s.
- Added a dedicated Missed header mask and terminal state. The red result bar is never interpreted as an oversized red target. Missed immediately starts the user-specified 180-second cooldown; this short video does not independently measure the cooldown duration.
- Added blue/yellow detection, return-pass timing, and geometry-bound selection that survives list-index changes from marker occlusion. Eleven native fixtures plus their manifest are retained in `tests/CuePilot.Tests/Fixtures/Pickpocket/clip2*`.
- Full 547-frame replay: zero labeled-state mismatches. White yields one hypothetical candidate; yellow yields none because its width is insufficient for the current uncertainty/delay envelope. Latest white replay detector mean 1.060 ms, p95 2.761 ms; these exclude capture and real input. Clip one still has zero mismatches and one hypothetical purple candidate.
- Current focused backend/bridge tests: 65 passed. UI: 24 tests, clean Svelte check/build. Rust: 11 tests and Clippy passed. No live Space delivery is connected.

Local clip-two analysis is under `tmp/pickpocket-clip2/`, including full timestamped cropped replay, contact sheet, source samples, and replay output logs.

### Third clip — Electronic Part grab, 2026-09-03

Source: `C:/Users/BLAZZER/Videos/Clips/FiveM_b3258_GTAProcess/clip-1788476264336-export-1788476296112.mp4`. 1920×1080, nominal 60 fps, 430 native frames, 7.217 seconds video.

- Cards are TNT Recipe (yellow), Loose Change (white), Ruby (red), Broken Electronic Part (pale green). These are existing names in another arrangement; the reference catalog remains eight items. The Precision Timing selector now offers all three clips, preserving each one's targets, measured/interpolated trace, estimates, and saved selection separately.
- Preparing first appears around 0.983 s, with a brief acquisition dropout at 1.000 s. Active starts 1.267 s, Grabbed appears at 2.983 s, and the panel disappears around 3.883 s. The result explicitly identifies `BROKEN_ELECTRONIC_PART`. The grabbed region can flash white; its color after success is not an active target constraint.
- Full-frame regions measured from the crop: yellow core x768–770 (~7 ms), white x850–942 (~322 ms), red x1022–1026 (~14 ms), pale green x1132–1172 (~140 ms), using approximate 286 px/s reference motion. These are display estimates, not live scheduling constants.
- At 2.017 s, the compressed marker's pale-green fringe created a spurious one-pixel band inside white and prevented reconnecting the target. Excluding three scaled pixels around the stem instead of two fixes that split. A retained 19-native-frame crossing regression exercises the same geometry selection/matching used by the observer and produces exactly one white timing candidate. Ten additional fixtures cover preparation, occlusion, success, and disappearance.
- Full replay: 430 frames, 401 stable frames labeled, zero labeled-state mismatches. Pale green yields one hypothetical candidate; yellow yields none. The standalone color-only White replay remains conservative (zero candidates when other white fragments make the color ambiguous); it does not use the live observer's geometry selection. The geometry-selected crossing regression passes. Latest pale-green detector mean 1.494 ms/p95 4.195 ms excludes decoding, capture, and input.
- After the fringe change, clip-one Purple and clip-two White replays remain at zero labeled mismatches and one candidate each. 63 focused .NET pickpocket tests, 28 UI tests, Svelte check, and production UI build pass. Both tested UI sizes fit without scrolling, and each clip restores its own target choice. No live capture or Space-delivery trial was performed.

Local third-clip frames and replay logs are under `tmp/pickpocket-clip3/`; retained fixtures and manifests are under `tests/CuePilot.Tests/Fixtures/Pickpocket/clip3*`.

### First live observation — manual blue grab, 2026-09-03

Source: local diagnostic session `20260903-234424-170fd25ae509469d8499234b952b3896`, with 1792×518 captured regions from the 1440p game window. Cards are Ruby (red), Pocket Watch (blue), Broken Electronic Part (pale green), and Wrist Band (white). The last two newly catalogued names are Pocket Watch and Wrist Band, bringing the reference catalog to ten. Names are visually read reference labels, not runtime item recognition.

- Native capture spans: bar x514–1277; red x639–643, blue x784–838, pale green x954–1008, white x1109–1193. Motion is approximately 388 px/s, with reversal before the manual grab. These display coordinates are specific to this evidence.
- The trace reports Grabbed with marker x792 inside the blue region and one sampled manual Space press. The result PNG was skipped after the image budget filled; item attribution combines the earlier visible card mapping and the captured observation state, rather than a saved result-title image.
- Prefer purple was selected, but this layout had no purple band, so the observer correctly produced no candidates. Replaying retained observations with Blue gives one candidate; Widest gives one on the return pass. Neither is proof of delivered automatic input.
- Active sample interval averaged 31.37 ms, with about 9.25 ms processing. This exposed timer oversleep and full-region image-budget pressure. The new timer and panel crops are documented in `docs/pickpocket-debugging.md`; a second manual live test must establish the new cadence and ending coverage.
- Retained fixture: `tests/CuePilot.Tests/Fixtures/Pickpocket/Live/first-attempt.png` and the sampled `first-attempt.json`. The UI provides a fourth reference with native geometry and the sampled marker trace, without claiming 60 fps video timing.

### Second live observation — Cuff Medicine grab, 2026-09-03

Session `20260904-001755-9a926f52ace542fc8070b2b3383ac419` ran with Automatic/Widest and the timer/crop fixes. It stopped cleanly through F7. The saved result image explicitly reads `GRABBED / CUFF_MEDICINE`; one manual Space press was observed. Cards were Broken Electronic Part (pale green), Cuff Medicine (blue), Ruby (red), and Loose Change (white), with no new item names.

- 238 captured samples, 190 Active samples. The 189 consecutive Active intervals averaged 16.783 ms (p95 17.131, max 17.302), none above 24 ms. Active capture averaged 3.144 ms, analysis 5.525 ms, total measured processing 9.128 ms. First-test Active intervals averaged 31.37 ms; the faster pacing is now measured live, not just simulated.
- One actual observer candidate at sample 122 selected white/Loose Change (native capture x1090–1213) moving right at about 380 px/s. Candidate press time was 39.24 ms after analysis; independently interpolated subsequent recorded marker positions were x1149.83, 1152.96, and 1156.03 for simulated 0/8/16 ms delivery delays, inside the white region. No key was sent.
- The manual Space press was observed later at sample 217 on the return pass in blue (x788–834), followed by Grabbed at sample 219/marker x805. This successful Cuff Medicine grab does not establish automatic delivery success for the different white target. The engine entered its three-minute cooldown.
- Debugger saved all 200 requested trace records and 60 requested images with zero drops/skips, using 23.06 MiB of PNGs. Maximum image age was 25.03 ms, with zero stale samples above 40 ms. Saved panel images include the full item row, bar, Space prompt, and result header.
- Retained `Fixtures/Pickpocket/Live/second-attempt.json` contains rebased observations and actual candidate timing; `second-candidate.png` and `second-grab.png` preserve candidate/result crops. A regression checks the candidate against later sampled positions and verifies Active/Grabbed detection on these crops. Automatic input remains off; physical input latency is still uncalibrated.

### Third live run — no press / Too Slow, 2026-09-03

Session `20260904-003029-3f3ca7c3edda481a876914deab0a67cb` actually ran in Observe mode with zero physical or automated presses. The saved ending explicitly says `TOO SLOW`. Cards are TNT Recipe, Ring, Wallet (new observed name), and Broken Electronic Part. The UI reference catalog has not yet added Wallet.

Independently of the mode mismatch, marker occlusion caused the green interval's left boundary x1125 to appear at x1130/1135 around samples120–121. This reset the predictor's motion fit or temporarily broke target matching, so it gathered fresh samples until the center had passed. The same happened on return passes. Active pacing itself was healthy: 480 samples, 16.70 ms mean/18.32 ms max consecutive interval. There were 488 saved trace rows, 103 saved PNGs and36 skipped periodic PNGs at the bounded image budget; ending images survived the critical reserve.

The retained `Live/third-attempt.json` plus `third-117/121/124/128.png` cover the edge crossing; `third-507.png` preserves the timeout. Before the fix, replay through predictor/input controller produced zero taps. With bounded restoration of a previously verified edge hidden by the marker, it produces one tap whose interpolated positions at simulated 0/8/16 ms delays are all inside x1125–1178. Pixel regression checks actual edge recovery, and negative cases reject absent targets, changed opposite boundaries and non-Active history. Stop now retains the selected run mode while disarming input. No actual automatic acceptance has yet been observed.

### Fourth live run — SingleAttempt stuck on fade-in target, 2026-09-03

Session `20260904-004425-44854428de3a4d358c4f8880447ff036`: SingleAttempt/Widest, 401 captures, 217 Active samples, zero automated presses. During Preparing, false white regions appeared in the transparent bar over bright scenery. The widest was x1003–1147 and was latched permanently. Actual Active bands were Blue x618–664, PaleGreen x776–845, Yellow x979–983 and Red x1149–1153. Every Active row reported waiting for a matching region because the false White target was absent. A manual Space press at sample240 was followed by Grabbed at242; no automatic attempt preceded it. Recorder saved246 rows/69 PNGs without loss; Active interval averaged16.83 ms, max18.96 ms.

`PickpocketTargetTracker` now confirms targets only during Active play, across three fresh samples; it can reacquire a vanished region without inheriting a shifted list index. Preparing pixels never latch the target. All four recorded live sequences now run through the actual engine's selection/predictor/input/diagnostic path using recorded observations, a virtual clock and mocked keyboard. The fourth case failed with zero presses before the fix and passes with exactly one green down/up pair after it; simulated 0/8/16 ms delivery positions remain inside the observed band. Actual physical input remains untested. Fixtures: `Live/fourth-attempt.json`, `fourth-21/25/28/242.png`.

## Fifth live run: detector stall over bright scenery

Session `20260904-023205-6c6e9900fe4a4ac2b0e4abc077f5f276`: SingleAttempt/Widest correctly armed, zero manual or automated presses. Frame 31 consumed 406 ms; frame 32 consumed 8,143 ms and returned Hidden despite an intact Active panel and 7.5 s countdown. Only three Active observations were processed. Final saved image is Missed. Bright scenery was classified as header text at the original threshold, failing negative template evidence and triggering repeated fallback searches. Retained `Live/fifth-25/29/31/32/33/34.png` and `fifth-frames.jsonl` preserve the pixels and crop coordinates. White/Loose Change is x579–702; Yellow is labeled Lucky Charm in this run (new observed item name; reference UI has not been extended).

The dual-threshold header check, packed masks, per-frame cache, 96-location search cap and cached bitmap dimensions keep frame 32 Active and reduce its measured Release analysis to 2.19 ms. A constructed movement sequence using the actual retained layout and difficult header passes through the real pixel detector and engine, producing one mocked Space down/up in White under 0/8/16 ms delay bounds. Missing frames are not recoverable; this constructed test is distinct from the four recorded-observation engine replays. 130 focused backend tests and all three complete source-clip replays pass. Native automatic receipt remains unverified. Dev now uses the Release engine with its inspectable development shell.

## First verified automatic grab and small-target follow-up

Session `20260904-024532-c35a4cdf10df4ffcada54d7765371bfc` proves one automatic Rope grab: SingleAttempt/Widest, one native Space down/up, zero observed manual presses, and `GRABBED ROPE` at frame 102. User independently confirmed success. Selected White x927–1035; final marker x985. Planned/down/up times were 29041307.425/29041309.526/29041346.474 ms: host lateness 2.101 ms and hold 36.948 ms. Active capture/analysis averaged 3.16/0.91 ms; cadence 16.82 ms, max 17.58 ms; no stale frames. 83 records/26 PNGs saved without loss. A preceding short run at024457 never reached Active and was not a failure to hit an item.

Retained `Live/automatic-rope.json` rebases the trace and native delivery timestamps; `automatic-rope-100/101/102.png` preserve the candidate, press and named result. Green was Broken Electronic Part, Red was Ruby x809–813, White was Rope (new observed name), Yellow was TNT Recipe. Red's four pixels are about 10 ms at the final measured 401 px/s. The wide grab alone cannot determine sufficiently tight game-input latency bounds for that region.

New opt-in `PrecisionAttempt` keeps one-tap ownership and final checks while permitting smaller delivery windows. Purple is the next selected target; Red offers an explicitly experimental center shot using the provisional 8 ms nominal delay, not a guaranteed hit. Synthetic purple motion and reconstructed red pixels exercise scheduling/input; all nominal-red assertions are distinguished from full-envelope accuracy. The original SingleAttempt policy remains the successful wide-target baseline.

Generated inspection output is under ignored `tmp/pickpocket-analysis/`: `overview.jpg`, `bar-sequence.jpg`, `success.png`, `grab-transition.jpg`, `timestamps.json`, `bar-native.rgb`, `marker.csv`, and `measure.py`. These are local analysis artifacts, not production detector code or committed regression fixtures. `measure.py` is deliberately specific to this clip's resolution and crop.

Native raw crop extraction used `ffmpeg -i <source> -vf crop=576:60:672:908 -an -fps_mode passthrough -f rawvideo -pix_fmt rgb24 <output>`. Frame timestamps came from `ffprobe -select_streams v:0 -show_entries frame=best_effort_timestamp_time -of json <source>`. Selected full-resolution crops were visually checked against the measured positions and the success transition.
