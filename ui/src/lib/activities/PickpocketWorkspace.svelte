<script lang="ts">
  import { onMount } from "svelte";
  import { ArrowLeft, Watch, Gem, Cpu, Cable, Check, Crosshair, Play, Pause, RotateCcw, ShieldCheck, Film, ArrowRight, ScanEye, Circle, Pill, ScrollText, Coins, X } from "@lucide/svelte";
  import { barPosition as positionOnBar, knownItemCount, pickpocketRecordings, readRecording, readTarget, referenceAt, recordingStorageKey, targetStorageKey, type PickpocketTarget, type PickpocketRecording } from "./pickpocket";
  import PickpocketLiveWorkspace from "./PickpocketLiveWorkspace.svelte";
  import type { PickpocketObserveStatus, PickpocketPolicy, PickpocketInputMode, PickpocketTiming } from "../engine.svelte";

  let { status, connected, targetValid, error, onmode, shortcut, onpolicy }: {
    status?: PickpocketObserveStatus;
    connected: boolean; targetValid: boolean; error: string | null;
    onmode: (mode: "observe" | "stop", policy: PickpocketPolicy, inputMode: PickpocketInputMode) => Promise<void>;
    shortcut: string;
    onpolicy: (policy: PickpocketPolicy, inputMode: PickpocketInputMode, timing?: PickpocketTiming) => Promise<void>;
  } = $props();
  let view = $state<"live" | "reference">("live");
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

{#if view === "reference"}<div class="pp-toolbar"><div class="pp-view-switch" aria-label="Pickpocket view"><button onclick={() => { view = "live"; playing = false; }}><ArrowLeft size={13} /> Back to live workspace</button></div></div>{/if}

{#if view === "live"}
  <div><PickpocketLiveWorkspace {status} {connected} {targetValid} {error} {onmode} {shortcut} {onpolicy} onreference={() => view = "reference"} /></div>
{:else}

<section class="pp-heading" aria-labelledby="pickpocket-heading">
  <div><p class="eyebrow"><Crosshair size={14} /> Precision timing</p><h1 id="pickpocket-heading">Pick your moment.</h1><p>Choose an item. Track the marker. Aim inside its color.</p></div>
  <label class="pp-recording"><span><Film size={14} /> Clips · {knownItemCount} item names</span><select value={recording} onchange={changeRecording} aria-label="Reference clip">{#each Object.entries(pickpocketRecordings) as [id, reference]}<option value={id}>{reference.label}</option>{/each}</select></label>
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
  :global(main:has(.pp-toolbar)) { gap:8px; }
  .pp-toolbar { display:flex; justify-content:space-between; align-items:center; gap:16px; }
  .pp-view-switch { display:flex; gap:5px; }
  .pp-view-switch button { padding:9px 14px; border:1px solid var(--line); border-radius:7px; background:transparent; color:var(--text-muted); font:inherit; font-size:12px; cursor:pointer; }
  .pp-view-switch button:focus-visible { outline:2px solid var(--accent); outline-offset:3px; }
  .pp-view-switch button:disabled { opacity:.5; cursor:default; }
  .pp-heading { display:flex; align-items:center; justify-content:space-between; gap:24px; padding:14px 0 17px; }
  .pp-heading h1 { margin:7px 0 8px; font-size:clamp(26px,3vw,37px); line-height:1.1; letter-spacing:-1.1px; font-weight:650; }
  .pp-heading > div > p:last-child { color:var(--text-muted); margin:0; font-size:13px; }
  .pp-recording { display:grid; gap:6px; flex-shrink:0; color:var(--text-muted); font-size:10px; }
  .pp-recording > span { display:flex; align-items:center; gap:6px; }
  .pp-recording select { color:var(--text); background:#0e1b24; border:1px solid var(--line); border-radius:6px; padding:8px; font:inherit; font-size:11px; }
  .pp-phase .missed { color:#f0757e; }
  .pp-layout { display:grid; grid-template-columns:minmax(0,1fr) 280px; gap:16px; }
  .pp-reader,.pp-summary { border:1px solid var(--line); border-radius:var(--radius-lg); background:var(--panel-soft); min-width:0; }
  .pp-reader { padding:24px; }
  .pp-section-heading { display:flex; justify-content:space-between; align-items:center; gap:12px; margin-bottom:22px; }
  .pp-kicker { font-size:10px; text-transform:uppercase; letter-spacing:1.5px; color:var(--text-muted); }
  .pp-section-heading h2 { font-size:17px; margin:7px 0 0; font-weight:570; letter-spacing:-.3px; }
  .pp-caption { font-size:10px; color:var(--text-muted); }
  .pp-targets { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:9px; padding:0; border:0; margin:0; }
  .pp-target { position:relative; display:flex; flex-direction:column; padding:12px 11px 14px; min-height:171px; border:1px solid var(--line); border-radius:9px; background:rgba(7,14,21,.6); cursor:pointer; transition:background 120ms,border-color 120ms; }
  .pp-target.selected { border-color:var(--item-color); background:color-mix(in srgb,var(--item-color) 9%,#0b141d); box-shadow:inset 0 -3px var(--item-color); }
  .pp-target:hover { border-color:var(--item-color); }
  .pp-target input { position:absolute; width:1px; height:1px; opacity:0; }
  .pp-target:has(input:focus-visible) { outline:2px solid var(--accent); outline-offset:4px; }
  .pp-target-top { display:flex; align-items:center; justify-content:space-between; gap:4px; font-size:10px; color:var(--item-color); }
  .pp-check { display:grid; place-items:center; width:16px; height:16px; border:1px solid color-mix(in srgb,var(--item-color) 35%,transparent); border-radius:50%; }
  .selected .pp-check { color:#111822; background:var(--item-color); }
  .pp-item-icon { color:var(--item-color); display:flex; align-items:center; height:60px; }
  .pp-target strong { font-size:12px; line-height:1.35; font-weight:560; min-height:33px; }
  .pp-window { font-size:10px; margin-top:7px; color:var(--text-muted); }
  .pp-timing { padding:27px 0 0; }
  .pp-timing-heading { display:flex; align-items:center; justify-content:space-between; gap:10px; }
  .pp-timing-heading > span:last-child { display:flex; align-items:center; gap:7px; font-size:10px; color:var(--text-muted); }
  .pp-lime-dot { width:5px; height:5px; border-radius:50%; background:#ade85c; }
  .pp-scale { display:flex; justify-content:space-between; margin:22px 0 23px; color:#788e9c; font-size:9px; font-family:Consolas,monospace; }
  .pp-track { position:relative; height:25px; border:1px solid #34434c; border-radius:3px; background:repeating-linear-gradient(90deg,transparent 0,transparent calc(25% - 1px),#283740 calc(25% - 1px),#283740 25%),#0a141b; }
  .pp-band { position:absolute; top:0; bottom:0; background:var(--item-color); opacity:.48; }
  .pp-band.chosen { opacity:1; box-shadow:0 0 18px color-mix(in srgb,var(--item-color) 25%,transparent); }
  .pp-aim { position:absolute; top:-13px; height:50px; border-left:1px dashed var(--item-color); color:var(--item-color); }
  .pp-aim > span { position:absolute; top:47px; left:0; transform:translateX(-50%); font-size:9px; text-transform:uppercase; letter-spacing:1px; }
  .pp-marker { position:absolute; top:-10px; bottom:-10px; width:2px; transform:translateX(-50%); background:#ade85c; box-shadow:0 0 8px #ade85c44; }
  .pp-marker::before,.pp-marker::after { content:""; position:absolute; left:-4px; border-left:5px solid transparent; border-right:5px solid transparent; }
  .pp-marker::before { top:-1px; border-top:6px solid #ade85c; } .pp-marker::after { bottom:-1px; border-bottom:6px solid #ade85c; }
  .pp-phase { margin:46px 0 20px; display:flex; justify-content:space-between; gap:8px; min-height:19px; font-size:11px; color:var(--text-muted); }
  .pp-phase > span { display:flex; align-items:center; gap:7px; } .pp-phase .grabbed { color:var(--accent); }
  kbd { padding:3px 7px; border:1px solid var(--line-strong); border-radius:4px; font:9px Consolas,monospace; color:var(--text); }
  .pp-playback { border-top:1px solid var(--line); padding-top:17px; }
  .pp-playback-controls { display:flex; align-items:center; gap:9px; }
  .pp-play,.pp-icon-button { display:flex; align-items:center; justify-content:center; gap:7px; border-radius:6px; padding:9px 11px; border:1px solid var(--line-strong); background:#15252e; color:var(--text); box-shadow:none; cursor:pointer; font-size:11px; min-height:34px; }
  .pp-play { color:var(--accent); min-width:104px; } .pp-icon-button { padding:9px; }
  button:hover { border-color:var(--accent); } button:focus-visible,select:focus-visible,.pp-scrubber:focus-visible { outline:2px solid var(--accent); outline-offset:3px; }
  .pp-speed { display:flex; gap:7px; align-items:center; color:var(--text-muted); font-size:10px; margin-left:4px; }
  .pp-speed select { padding:5px; color:var(--text); background:#0e1b24; border:1px solid var(--line); border-radius:4px; font-size:11px; }
  .pp-time { margin-left:auto; font:11px Consolas,monospace; white-space:nowrap; } .pp-time span { color:var(--text-muted); }
  .pp-scrubber { width:100%; margin:16px 0 3px; accent-color:var(--accent); cursor:pointer; }
  .pp-playback p { font-size:10px; color:var(--text-muted); line-height:1.6; margin:8px 0 0; max-width:65ch; }
  .pp-summary { padding:24px; display:flex; flex-direction:column; background:linear-gradient(155deg,color-mix(in srgb,var(--item-color) 6%,#101a22),#0b151d 65%); }
  .pp-summary-top { display:flex; justify-content:space-between; align-items:center; color:var(--item-color); }
  .pp-summary h2 { margin:21px 0 7px; font-size:22px; font-weight:550; line-height:1.2; letter-spacing:-.5px; }
  .pp-target-color { display:flex; align-items:center; gap:7px; color:var(--item-color); font-size:11px; margin:0; } .pp-target-color i { width:6px; height:6px; border-radius:2px; background:var(--item-color); }
  .pp-window-value { margin:22px 0 0; display:flex; align-items:baseline; gap:7px; } .pp-window-value strong { font-size:48px; font-weight:500; line-height:1.05; letter-spacing:-2px; } .pp-window-value > span { font-size:14px; color:var(--text-muted); }
  .pp-window-label { color:var(--text-muted); font-size:10px; margin:7px 0 14px; }
  .pp-difficulty { align-self:flex-start; padding:5px 8px; border:1px solid color-mix(in srgb,var(--item-color) 28%,transparent); color:var(--item-color); border-radius:4px; font-size:10px; }
  .pp-target-description { color:var(--text-muted); font-size:11px; line-height:1.65; margin:13px 0 20px; }
  .pp-facts { margin:0 0 20px; padding-top:12px; border-top:1px solid var(--line); } .pp-facts > div { display:flex; justify-content:space-between; gap:10px; margin:10px 0; font-size:10px; } .pp-facts dt { color:var(--text-muted); } .pp-facts dd { margin:0; }
  .pp-live-state { margin-top:auto; padding-top:15px; border-top:1px solid var(--line); } .pp-live-state > span { display:flex; align-items:center; gap:7px; font-size:11px; } .pp-live-state i { width:5px; height:5px; border-radius:50%; background:var(--text-muted); } .pp-live-state p { color:var(--text-muted); font-size:11px; line-height:1.6; margin:8px 0 0; }
  .pp-saved { color:var(--text-muted); font-size:9px; margin:17px 0 0; }
  .pp-footer > span { font-size:10px; color:var(--text-muted); }
  .pp-sr-only { position:absolute; width:1px; height:1px; overflow:hidden; clip-path:inset(50%); white-space:nowrap; }
  @media(min-width:801px) and (max-height:850px) {
    .pp-heading { padding:5px 0 9px; } .pp-heading h1 { font-size:30px; margin:4px 0 6px; }
    .pp-reader,.pp-summary { padding:18px; }
    .pp-section-heading { margin-bottom:14px; } .pp-section-heading h2 { font-size:16px; margin-top:4px; }
    .pp-kicker { line-height:12px; display:block; }
    .pp-target { min-height:127px; padding:9px 10px 9px; } .pp-item-icon { height:33px; }
    .pp-target strong { min-height:31px; } .pp-window { margin-top:3px; }
    .pp-timing { padding-top:21px; } .pp-scale { margin:16px 0 20px; }
    .pp-phase { margin:35px 0 9px; }
    .pp-playback { padding-top:12px; } .pp-scrubber { margin-top:10px; } .pp-playback p { line-height:1.4; margin-top:5px; }
    .pp-summary h2 { margin-top:15px; font-size:21px; } .pp-window-value { margin-top:16px; } .pp-window-value strong { font-size:42px; }
    .pp-target-description { margin:10px 0 12px; } .pp-facts { padding-top:7px; margin-bottom:10px; } .pp-live-state { padding-top:11px; } .pp-saved { margin-top:12px; }
  }
  @media(max-width:1000px) { .pp-layout { grid-template-columns:minmax(0,1fr) 240px; gap:12px; } .pp-reader,.pp-summary { padding:18px; } .pp-target { padding:10px 8px; } .pp-caption { display:none; } }
  @media(max-width:800px) { .pp-layout { grid-template-columns:1fr; } .pp-summary { display:grid; grid-template-columns:1fr 1fr; gap:0 24px; } .pp-summary-top,.pp-facts,.pp-live-state,.pp-saved { grid-column:1/-1; } .pp-summary h2 { grid-column:1; margin:16px 0 7px; } .pp-target-color { grid-column:1; } .pp-window-value { grid-column:2; grid-row:2/4; align-self:center; margin:0; } .pp-window-label { grid-column:2; } .pp-difficulty { grid-column:1; grid-row:4; } .pp-target-description { grid-column:1/-1; } }
  @media(max-width:480px) { .pp-targets { grid-template-columns:repeat(2,minmax(0,1fr)); } .pp-target { min-height:150px; } .pp-heading h1 { font-size:28px; } .pp-phase { flex-wrap:wrap; } .pp-speed { margin-left:0; } .pp-speed select { max-width:60px; } .pp-speed { font-size:0; } .pp-time { font-size:10px; } .pp-footer { align-items:flex-start; gap:10px; flex-direction:column; } }
  @media(prefers-reduced-motion:reduce) { * { transition:none !important; } }
  @media(min-width:620px) {
    .pp-heading { padding:0; gap:12px; }
    .pp-heading h1 { font-size:26px; margin:4px 0; }
    .pp-layout { grid-template-columns:minmax(0,1fr) 225px; gap:12px; }
    .pp-reader,.pp-summary { padding:14px; }
    .pp-summary { display:flex; }
    .pp-section-heading { margin-bottom:10px; }
    .pp-target { min-height:103px; padding:8px; }
    .pp-item-icon { height:27px; }
    .pp-target strong { min-height:30px; font-size:11px; }
    .pp-window { margin-top:3px; font-size:9px; }
    .pp-timing { padding-top:15px; }
    .pp-scale { margin:12px 0 18px; }
    .pp-phase { margin:33px 0 8px; }
    .pp-playback { padding-top:10px; }
    .pp-scrubber { margin-top:8px; }
    .pp-summary h2 { margin:10px 0 6px; font-size:20px; }
    .pp-window-value { margin:12px 0 0; }
    .pp-window-value strong { font-size:36px; }
    .pp-target-description { margin:10px 0; line-height:1.45; }
    .pp-facts { margin-bottom:8px; padding-top:5px; }
    .pp-facts > div { margin:7px 0; }
    .pp-live-state { padding-top:8px; }
    .pp-live-state p { line-height:1.4; }
    .pp-saved { margin:8px 0 0; }
    .pp-footer { flex-direction:row; align-items:center; min-height:0; padding:6px 0; margin:0; }
  }
  @media(min-width:620px) and (max-height:680px) {
    .pp-recording select { padding:5px 8px; }
    .pp-footer { padding:3px 0; }
    .pp-reader,.pp-summary { padding:12px; }
    .pp-timing { padding-top:10px; }
    .pp-scale { margin-top:8px; }
    .pp-phase { margin-top:29px; }
    .pp-playback { padding-top:7px; }
    .pp-playback p { max-width:none; }
    .pp-heading .eyebrow { display:none; }
    .pp-heading h1 { font-size:23px; }
    .pp-heading > div > p:last-child { font-size:11px; }
    .pp-target { min-height:89px; }
    .pp-item-icon { height:20px; }
    .pp-item-icon :global(svg) { width:22px; height:22px; }
    .pp-target strong { min-height:26px; }
    .pp-section-heading h2 { font-size:14px; }
    .pp-summary h2 { margin-top:7px; }
    .pp-window-value { margin-top:8px; }
    .pp-window-label { margin:4px 0 8px; }
    .pp-target-description { font-size:10px; margin:7px 0; }
    .pp-live-state p { font-size:10px; }
    .pp-playback p { margin-top:4px; line-height:1.4; }
  }
</style>
