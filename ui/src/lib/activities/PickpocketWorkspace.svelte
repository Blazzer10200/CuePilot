<script lang="ts">
  import { onMount } from "svelte";
  import { ArrowLeft, Watch, Gem, Cpu, Cable, Check, Crosshair, Play, Pause, RotateCcw, ShieldCheck, Film, ArrowRight, ScanEye, Circle, Pill, ScrollText, Coins, X } from "@lucide/svelte";
  import { barPosition as positionOnBar, knownItemCount, pickpocketRecordings, readRecording, readTarget, referenceAt, recordingStorageKey, targetStorageKey, type PickpocketTarget, type PickpocketRecording } from "./pickpocket";
  import PickpocketLiveWorkspace from "./PickpocketLiveWorkspace.svelte";
  import type { PickpocketView } from "./pickpocket-session";
  import type { PickpocketObserveStatus, PickpocketPolicy, PickpocketInputMode, PickpocketTiming } from "../engine.svelte";

  let { status, connected, targetValid, error, onmode, shortcut, onpolicy, view = $bindable("live") }: {
    status?: PickpocketObserveStatus;
    connected: boolean; targetValid: boolean; error: string | null;
    onmode: (mode: "observe" | "stop", policy: PickpocketPolicy, inputMode: PickpocketInputMode) => Promise<void>;
    shortcut: string;
    onpolicy: (policy: PickpocketPolicy, inputMode: PickpocketInputMode, timing?: PickpocketTiming) => Promise<void>;
    view?: PickpocketView;
  } = $props();
  let recording = $state<PickpocketRecording>("live");
  let selected = $state<PickpocketTarget>("White");
  let position = $state(0);
  let playing = $state(false);
  let speed = $state(0.5);
  let storageError = $state(false);
  let reducedMotion = $state(false);
  const clip = $derived(pickpocketRecordings[recording]);
  const barWidth = $derived(recording === "live" ? 763 : 576);
  const barPosition = (x: number) => positionOnBar(x, recording === "live" ? 514 : 672, barWidth);
  const traceDurationMs = $derived(clip.durationMs);
  const target = $derived(clip.targets.find(item => item.id === selected) ?? clip.targets[0]);
  const sample = $derived(referenceAt(position, recording));
  const aim = $derived(barPosition((target.left + target.right) / 2));
  const stateLabel = $derived(sample.state === "Preparing" ? "Get ready" : sample.state === "Active" ? `Marker moving ${sample.direction}` : clip.outcome);
  const icons = { Rope: Cable, "Luxury Watch": Watch, Ruby: Gem, "Broken Electronic Part": Cpu, Ring: Circle, "Cuff Medicine": Pill, "TNT Recipe": ScrollText, "Loose Change": Coins, "Pocket Watch": Watch, "Wrist Band": Circle };

  function restoreTarget() {
    selected = readTarget(null, recording);
    try {
      selected = readTarget(localStorage.getItem(`${targetStorageKey}.${recording}`) ?? (recording === "grab" ? localStorage.getItem(targetStorageKey) : null), recording);
      storageError = false;
    } catch { storageError = true; }
  }
  function changeRecording(event: Event) {
    playing = false;
    position = 0;
    recording = readRecording((event.currentTarget as HTMLSelectElement).value);
    restoreTarget();
    try { localStorage.setItem(recordingStorageKey, recording); } catch { storageError = true; }
  }

  onMount(() => {
    try { recording = readRecording(localStorage.getItem(recordingStorageKey)); } catch { storageError = true; }
    restoreTarget();
    const query = matchMedia("(prefers-reduced-motion: reduce)");
    reducedMotion = query.matches;
    const motionChanged = () => { reducedMotion = query.matches; if (reducedMotion) playing = false; };
    query.addEventListener("change", motionChanged);
    const visibilityChanged = () => { if (document.hidden) playing = false; };
    document.addEventListener("visibilitychange", visibilityChanged);
    return () => { query.removeEventListener("change", motionChanged); document.removeEventListener("visibilitychange", visibilityChanged); };
  });

  $effect(() => {
    if (!playing) return;
    let previous = performance.now();
    let request = 0;
    const advance = (now: number) => {
      position = Math.min(traceDurationMs, position + (now - previous) * speed);
      previous = now;
      if (position >= traceDurationMs) { playing = false; return; }
      request = requestAnimationFrame(advance);
    };
    request = requestAnimationFrame(advance);
    return () => cancelAnimationFrame(request);
  });

  function choose(value: PickpocketTarget) {
    selected = value;
    try { localStorage.setItem(`${targetStorageKey}.${recording}`, value); storageError = false; } catch { storageError = true; }
  }
  function togglePlayback() {
    if (position >= traceDurationMs) position = 0;
    playing = !playing;
  }
  function scrub(event: Event) {
    playing = false;
    position = Number((event.currentTarget as HTMLInputElement).value);
  }
</script>

{#if view !== "reference"}
  <div class="pp-live"><PickpocketLiveWorkspace {status} {connected} {targetValid} {error} {onmode} {shortcut} {onpolicy} historyOpen={view === "history"} /></div>
{:else}
<section class="pp-heading" aria-labelledby="pickpocket-heading">
  <div><h1 id="pickpocket-heading">Pick your moment.</h1><p>Choose an item. Track the marker. Aim inside its color.</p></div>
  <div class="pp-heading-tools">
    <label class="pp-recording"><span><Film size={14} /> Clips · {knownItemCount} item names</span><select value={recording} onchange={changeRecording} aria-label="Reference clip">{#each Object.entries(pickpocketRecordings) as [id, reference]}<option value={id}>{reference.label}</option>{/each}</select></label>
    <button class="pp-back" onclick={() => { view = "live"; playing = false; }}><ArrowLeft size={14} /> Back to live workspace</button>
  </div>
</section>

<div class="pp-layout">
  <section class="pp-reader" aria-labelledby="pp-target-heading">
    <header class="pp-section-heading"><div><span class="pp-kicker">01 / Target selection</span><h2 id="pp-target-heading">What are you aiming for?</h2></div><span class="pp-caption">Items in this recording</span></header>
    <fieldset class="pp-targets">
      <legend class="pp-sr-only">Pickpocket target</legend>
      {#each clip.targets as item}
        {@const Icon = icons[item.name]}
        <label class="pp-target" class:selected={selected === item.id} style={`--item-color:${item.color}`}>
          <input type="radio" name="pickpocket-target" value={item.id} checked={selected === item.id} onchange={() => choose(item.id)} aria-label={`${item.name}, ${item.colorName} target`} />
          <span class="pp-target-top"><span>{item.colorName}</span><span class="pp-check">{#if selected === item.id}<Check size={12} />{/if}</span></span>
          <span class="pp-item-icon"><Icon size={31} strokeWidth={1.35} /></span>
          <strong>{item.name}</strong><span class="pp-window">~{item.windowMs} ms window</span>
        </label>
      {/each}
    </fieldset>

    <div class="pp-timing" aria-label="Recorded timing bar">
      <div class="pp-timing-heading"><span class="pp-kicker">02 / Timing reader</span><span><i class="pp-lime-dot"></i>Marker {#if sample.direction === "left"}<ArrowLeft size={12} />{:else}<ArrowRight size={12} />{/if}</span></div>
      <div class="pp-scale" aria-hidden="true"><span>0</span><span>25</span><span>50</span><span>75</span><span>100%</span></div>
      <div class="pp-track" role="img" aria-label={`Recorded marker at ${Math.round(barPosition(sample.markerX))} percent. Selected target: ${target.name}. ${stateLabel}.`}>
        {#each clip.targets as item}
          <span class="pp-band" class:chosen={selected === item.id} style={`left:${barPosition(item.left)}%;width:${(item.right-item.left)/barWidth*100}%;--item-color:${item.color}`}></span>
        {/each}
        <span class="pp-aim" style={`left:${aim}%;--item-color:${target.color}`}><span>Aim</span></span>
        <span class="pp-marker" style={`left:${barPosition(sample.markerX)}%`}></span>
      </div>
      <div class="pp-phase" aria-live="polite"><span class:grabbed={sample.state === "Grabbed"} class:missed={sample.state === "Missed"}>{#if sample.state === "Grabbed"}<Check size={14} />{:else if sample.state === "Missed"}<X size={14} />{:else}<ScanEye size={14} />{/if}{stateLabel}</span><span>Game input <kbd>SPACE</kbd></span></div>
    </div>

    <div class="pp-playback">
      <div class="pp-playback-controls">
        <button class="pp-play" onclick={togglePlayback} aria-label={playing ? "Pause recorded trace" : "Play recorded trace"}>{#if playing}<Pause size={15} />{:else}<Play size={15} />{/if}{playing ? "Pause" : position >= traceDurationMs ? "Replay trace" : "Play trace"}</button>
        <button class="pp-icon-button" aria-label="Restart recorded trace" onclick={() => { playing = false; position = 0; }}><RotateCcw size={15} /></button>
        <label class="pp-speed">Speed <select bind:value={speed} aria-label="Recorded trace playback speed"><option value={0.25}>0.25×</option><option value={0.5}>0.5×</option><option value={1}>1×</option></select></label>
        <span class="pp-time">{(position / 1000).toFixed(2)} <span>/ {(traceDurationMs / 1000).toFixed(2)} s</span></span>
      </div>
      <input class="pp-scrubber" type="range" min="0" max={traceDurationMs} step="1" value={position} oninput={scrub} aria-label="Recorded trace position" aria-valuetext={`${(position / 1000).toFixed(2)} seconds, ${stateLabel}`} />
      <p>{reducedMotion ? "Scrub or play to inspect. " : ""}Interpolated clip trace; your selection moves the aim point.</p>
    </div>
  </section>

  <aside class="pp-summary" aria-labelledby="pp-summary-heading" style={`--item-color:${target.color}`}>
    <div class="pp-summary-top"><span class="pp-kicker">Your target</span><Crosshair size={18} /></div>
    <h2 id="pp-summary-heading">{target.name}</h2><p class="pp-target-color"><i></i>{target.colorName} region</p>
    <div class="pp-window-value"><strong>~{target.windowMs}</strong><span>ms</span></div>
    <p class="pp-window-label">Estimated crossing window</p>
    <span class="pp-difficulty">{target.difficulty}</span>
    <p class="pp-target-description">{target.windowMs < 17 ? "A very narrow crossing window. Input timing needs calibration before aiming here." : target.windowMs < 100 ? "A precise window. Aim toward the center; input timing still needs calibration." : "A wider region leaves more room for capture and input delay. Input timing still needs calibration."}</p>
    <dl class="pp-facts"><div><dt>Reference motion</dt><dd>~{clip.speed} px/s</dd></div><div><dt>Recording</dt><dd>{recording === "live" ? "1440p · live trace" : "1080p · 60 fps"}</dd></div><div><dt>Input timing</dt><dd>Not calibrated</dd></div></dl>
    <div class="pp-live-state"><span><i></i>Recorded reference</span><p>Names and positions are from this clip. Use Live observation for the next layout.</p></div>
    <p class="pp-saved" role="status">{storageError ? "Selection applies here; local saving is unavailable." : "Target choice is saved on this PC."}</p>
  </aside>
</div>

<footer class="status-footer pp-footer"><div class="safety-summary"><ShieldCheck size={15} /><p><strong>Input off</strong><i></i>Preview never presses keys</p></div><span>Estimates from the selected recording.</span></footer>
{/if}

<style>
  /* Live view: keeps the window-sized flex chain from the app shell down to the live grid. */
  .pp-live { flex:1; min-height:0; display:flex; flex-direction:column; gap:10px; }

  /* Reference view. The window is never narrower than 760px, so these are the only widths it needs. */
  .pp-back { display:inline-flex; align-items:center; gap:7px; height:32px; padding:0 12px; border:1px solid var(--line-strong); border-radius:var(--radius-control); background:transparent; color:var(--text-2); font:inherit; font-size:var(--fs-caption); cursor:pointer; transition:border-color var(--dur-fast) var(--ease-out),color var(--dur-fast) var(--ease-out); }
  .pp-back:hover { border-color:rgba(255,255,255,.22); color:var(--text); }
  .pp-heading { display:flex; align-items:center; justify-content:space-between; gap:12px; }
  .pp-heading h1 { margin:4px 0; color:var(--text-strong); font-size:26px; font-weight:650; line-height:1.1; letter-spacing:-.02em; }
  .pp-heading > div > p:last-child { margin:0; color:var(--text-2); font-size:var(--fs-body); }
  .pp-heading-tools { display:flex; align-items:flex-end; gap:8px; flex-shrink:0; }
  .pp-heading-tools .pp-back { height:34px; }
  .pp-recording { display:grid; gap:6px; flex-shrink:0; color:var(--text-3); font-size:var(--fs-caption); }
  .pp-recording > span { display:flex; align-items:center; gap:6px; }
  .pp-recording select { height:34px; padding:0 10px; border:1px solid rgba(255,255,255,.12); border-radius:var(--radius-control); background:var(--surface-sunken); color:var(--text); font:inherit; font-size:var(--fs-caption); }
  .pp-layout { display:grid; grid-template-columns:minmax(0,1fr) 240px; gap:12px; }
  .pp-reader, .pp-summary { min-width:0; padding:16px; border:1px solid var(--line); border-radius:var(--radius-card); background:var(--panel); }
  .pp-section-heading { display:flex; align-items:center; justify-content:space-between; gap:12px; margin-bottom:12px; }
  .pp-section-heading h2 { margin:4px 0 0; color:var(--text); font-size:var(--fs-title); font-weight:620; }
  .pp-kicker, .pp-caption { color:var(--text-3); font-size:var(--fs-caption); }
  .pp-targets { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:8px; margin:0; padding:0; border:0; }
  .pp-target { position:relative; display:flex; flex-direction:column; min-height:108px; padding:9px; border:1px solid var(--line); border-radius:10px; background:var(--surface-sunken); cursor:pointer; transition:background-color var(--dur-fast) var(--ease-out),border-color var(--dur-fast) var(--ease-out); }
  .pp-target:hover { border-color:var(--item-color); }
  .pp-target.selected { border-color:var(--item-color); background:color-mix(in srgb,var(--item-color) 9%,var(--surface-sunken)); box-shadow:inset 0 -3px var(--item-color); }
  .pp-target input { position:absolute; width:1px; height:1px; opacity:0; }
  .pp-target:has(input:focus-visible) { box-shadow:var(--focus-ring); }
  .pp-target-top { display:flex; align-items:center; justify-content:space-between; gap:4px; color:var(--item-color); font-size:var(--fs-caption); }
  .pp-check { display:grid; place-items:center; width:16px; height:16px; border:1px solid color-mix(in srgb,var(--item-color) 35%,transparent); border-radius:50%; }
  .selected .pp-check { background:var(--item-color); color:var(--surface-sunken); }
  .pp-item-icon { display:flex; align-items:center; height:30px; color:var(--item-color); }
  .pp-target strong { min-height:32px; color:var(--text); font-size:var(--fs-caption); font-weight:600; line-height:1.35; }
  .pp-window { margin-top:3px; color:var(--text-3); font:var(--fs-meta) "Cascadia Code",Consolas,monospace; }
  .pp-timing { padding-top:16px; }
  .pp-timing-heading { display:flex; align-items:center; justify-content:space-between; gap:10px; }
  .pp-timing-heading > span:last-child { display:flex; align-items:center; gap:7px; color:var(--text-3); font-size:var(--fs-caption); }
  .pp-lime-dot { width:6px; height:6px; border-radius:50%; background:#ade85c; }
  .pp-scale { display:flex; justify-content:space-between; margin:12px 0 18px; color:var(--text-3); font:var(--fs-meta) "Cascadia Code",Consolas,monospace; }
  .pp-track { position:relative; height:28px; border:1px solid #494744; border-radius:5px; background:repeating-linear-gradient(90deg,transparent 0,transparent calc(25% - 1px),#3a3936 calc(25% - 1px),#3a3936 25%),var(--surface-sunken); }
  .pp-band { position:absolute; top:0; bottom:0; background:var(--item-color); opacity:.55; }
  .pp-band.chosen { opacity:1; box-shadow:0 0 0 2px var(--surface-sunken),0 0 0 4px var(--item-color); }
  .pp-aim { position:absolute; top:-13px; height:52px; border-left:1px dashed var(--item-color); color:var(--item-color); }
  .pp-aim > span { position:absolute; top:50px; left:0; font:600 var(--fs-meta) "Cascadia Code",Consolas,monospace; transform:translateX(-50%); }
  .pp-marker { position:absolute; top:-12px; bottom:-12px; width:2px; background:#ade85c; transform:translateX(-50%); box-shadow:0 0 10px rgba(173,232,92,.4); }
  .pp-phase { display:flex; justify-content:space-between; gap:8px; min-height:19px; margin:34px 0 10px; color:var(--text-2); font-size:var(--fs-caption); }
  .pp-phase > span { display:flex; align-items:center; gap:7px; }
  .pp-phase .grabbed { color:var(--accent-text); }
  .pp-phase .missed { color:var(--warning-text); }
  kbd { padding:2px 6px; border:1px solid var(--line-strong); border-radius:var(--radius-kbd); color:var(--text); font:600 var(--fs-meta) "Cascadia Code",Consolas,monospace; }
  .pp-playback { padding-top:10px; border-top:1px solid var(--line); }
  .pp-playback-controls { display:flex; align-items:center; gap:8px; }
  .pp-play, .pp-icon-button { display:flex; align-items:center; justify-content:center; gap:7px; height:32px; padding:0 11px; border:1px solid var(--line-strong); border-radius:var(--radius-control); background:transparent; color:var(--text); font:inherit; font-size:var(--fs-caption); cursor:pointer; transition:border-color var(--dur-fast) var(--ease-out); }
  .pp-play { min-width:104px; color:var(--accent-text); }
  .pp-icon-button { width:32px; padding:0; }
  .pp-play:hover, .pp-icon-button:hover { border-color:rgba(255,255,255,.28); }
  .pp-speed { display:flex; align-items:center; gap:7px; margin-left:4px; color:var(--text-3); font-size:var(--fs-caption); }
  .pp-speed select { height:28px; padding:0 6px; border:1px solid rgba(255,255,255,.12); border-radius:6px; background:var(--surface-sunken); color:var(--text); font:inherit; font-size:var(--fs-caption); }
  .pp-time { margin-left:auto; color:var(--text); font:var(--fs-meta) "Cascadia Code",Consolas,monospace; white-space:nowrap; }
  .pp-time span { color:var(--text-3); }
  .pp-scrubber { width:100%; margin:8px 0 2px; accent-color:var(--accent); cursor:pointer; }
  .pp-playback p { margin:4px 0 0; color:var(--text-3); font-size:var(--fs-caption); line-height:1.45; }
  .pp-summary { display:flex; flex-direction:column; background:linear-gradient(155deg,color-mix(in srgb,var(--item-color) 6%,var(--panel)),var(--panel) 65%); }
  .pp-summary-top { display:flex; align-items:center; justify-content:space-between; color:var(--item-color); }
  .pp-summary h2 { margin:10px 0 6px; color:var(--text-strong); font-size:20px; font-weight:620; line-height:1.2; letter-spacing:-.01em; }
  .pp-target-color { display:flex; align-items:center; gap:7px; margin:0; color:var(--item-color); font-size:var(--fs-caption); }
  .pp-target-color i { width:6px; height:6px; border-radius:2px; background:var(--item-color); }
  .pp-window-value { display:flex; align-items:baseline; gap:7px; margin:12px 0 0; }
  .pp-window-value strong { color:var(--text-strong); font-size:36px; font-weight:600; line-height:1.05; letter-spacing:-.03em; }
  .pp-window-value > span { color:var(--text-3); font-size:14px; }
  .pp-window-label { margin:6px 0 10px; color:var(--text-3); font-size:var(--fs-caption); }
  .pp-difficulty { align-self:flex-start; padding:4px 8px; border:1px solid color-mix(in srgb,var(--item-color) 28%,transparent); border-radius:var(--radius-chip); color:var(--item-color); font-size:var(--fs-caption); }
  .pp-target-description { margin:10px 0; color:var(--text-2); font-size:var(--fs-caption); line-height:1.5; }
  .pp-facts { margin:0 0 8px; padding-top:6px; border-top:1px solid var(--line); }
  .pp-facts > div { display:flex; justify-content:space-between; gap:10px; margin:7px 0; font-size:var(--fs-caption); }
  .pp-facts dt { color:var(--text-3); }
  .pp-facts dd { margin:0; color:var(--text); }
  .pp-live-state { margin-top:auto; padding-top:8px; border-top:1px solid var(--line); }
  .pp-live-state > span { display:flex; align-items:center; gap:7px; color:var(--text); font-size:var(--fs-caption); }
  .pp-live-state i { width:6px; height:6px; border-radius:50%; background:var(--text-3); }
  .pp-live-state p { margin:6px 0 0; color:var(--text-3); font-size:var(--fs-caption); line-height:1.4; }
  .pp-saved { margin:8px 0 0; color:var(--text-3); font-size:var(--fs-meta); }
  .pp-footer { flex-direction:row; align-items:center; min-height:0; margin:0; padding:6px 0; }
  .pp-footer > span { color:var(--text-3); font-size:var(--fs-caption); }
  .pp-sr-only { position:absolute; width:1px; height:1px; overflow:hidden; clip-path:inset(50%); white-space:nowrap; }
  @media (max-width:1000px) { .pp-caption { display:none; } }
  @media (max-height:700px) {
    .pp-heading h1 { font-size:22px; }
    .pp-heading > div > p:last-child { font-size:var(--fs-caption); }
    .pp-reader, .pp-summary { padding:12px; }
    .pp-target { min-height:92px; }
    .pp-item-icon { height:22px; }
    .pp-item-icon :global(svg) { width:22px; height:22px; }
    .pp-target strong { min-height:28px; }
    .pp-timing { padding-top:10px; }
    .pp-scale { margin:8px 0 16px; }
    .pp-phase { margin-top:30px; }
    .pp-playback { padding-top:7px; }
    .pp-summary h2 { margin-top:7px; }
    .pp-window-value { margin-top:8px; }
    .pp-window-label { margin:4px 0 8px; }
    .pp-footer { padding:3px 0; }
  }
</style>
