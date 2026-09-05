<script lang="ts">
  import { onMount } from "svelte";
  import { Crosshair, Timer, ShieldCheck, OctagonX, Eye, Copy, FolderOpen } from "@lucide/svelte";
  import { invoke } from "@tauri-apps/api/core";
  import PickpocketHistory from "./PickpocketHistory.svelte";
  import { attemptReport, timingStep } from "./history";
  import type { PickpocketObserveStatus, PickpocketPolicy, PickpocketInputMode, PickpocketTiming, PickpocketColor } from "../engine.svelte";
  import { itemGroups, targetChoices, targetHint, defaultPriority, changePriority, defaultItemPriority, changeItemPriority } from "./pickpocket-catalog";

  let { status, connected, targetValid, error, onmode, shortcut, onpolicy, onreference }: {
    status?: PickpocketObserveStatus; connected: boolean; targetValid: boolean; error: string | null;
    onmode: (mode: "observe" | "stop", policy: PickpocketPolicy, inputMode: PickpocketInputMode) => Promise<void>;
    shortcut: string;
    onpolicy: (policy: PickpocketPolicy, inputMode: PickpocketInputMode, timing?: PickpocketTiming) => Promise<void>;
    onreference: () => void;
  } = $props();
  let policy = $state<PickpocketPolicy>("Widest");
  let inputMode = $state<PickpocketInputMode>("Observe");
  let pending = $state(false);
  let historyOpen = $state(false);
  let saveMessage = $state("");
  let saveFailed = $state(false);
  function setWorkspace(history: boolean) { historyOpen = history; window.scrollTo({ top: 0 }); }
  let view = $state<"Run" | "Timing" | "Items" | "Priority">("Run");
  let showMeasurements = $state(false);
  let redAdvanceMs = $state(8);
  let yellowAdvanceMs = $state(20);
  let customPriority = $state<PickpocketColor[]>([...defaultPriority]);
  let selectedHistoryId = $state("");
  let itemPriority = $state<string[]>([...defaultItemPriority]);
  let itemColor = $state<PickpocketColor>("Purple");
  const itemGroup = $derived(itemGroups.find(group => group.color === itemColor)!);
  const rankedItems = $derived(itemPriority.filter(name => itemGroup.items.some(item => item.name === name)));
  const recent = $derived(status?.recentAttempts ?? []);
  const savedAttempt = $derived(selectedHistoryId ? recent.find(attempt => attempt.id === selectedHistoryId) : !status?.observing ? recent[0] : undefined);
  const result = $derived(savedAttempt
    ? savedAttempt && { state: savedAttempt.outcome, color: savedAttempt.color, widthPixels: savedAttempt.widthPixels, offsetPixels: savedAttempt.offsetPixels }
    : status?.debug?.result);
  const displayedPresses = $derived(savedAttempt ? savedAttempt.automaticPresses : status?.automatedPressCount ?? 0);
  $effect(() => { if (selectedHistoryId && !recent.some(attempt => attempt.id === selectedHistoryId)) selectedHistoryId = ""; });
  $effect(() => {
    if (!pending && status) {
      policy = status.targetPolicy;
      inputMode = status.inputMode ?? "Observe";
      redAdvanceMs = status.redAdvanceMs ?? 8;
      yellowAdvanceMs = status.yellowAdvanceMs ?? 20;
      customPriority = [...(status.customPriority ?? defaultPriority)];
      itemPriority = [...(status.itemPriority ?? defaultItemPriority)];
    }
  });
  let now = $state(Date.now());
  let debugAction = $state("");
  let debugFailed = $state(false);
  const storageIssue = $derived(status?.sessionStateError || status?.debug?.error);
  const observing = $derived(connected && status?.observing === true);
  const observation = $derived(observing ? status?.observation : undefined);
  const bands = $derived(observation?.bands ?? []);
  const selectedBand = $derived(bands[status?.selectedBandIndex ?? -1]);
  const seconds = $derived(Math.max(0, Math.ceil(((status?.cooldownUntilUnixMs ?? 0) - now) / 1000)));
  const countdown = $derived(`${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`);
  const sessionLabel = $derived(!connected ? "Engine disconnected" : seconds > 0 ? "Cooldown · input paused" : status?.inputArmed ? "One tap armed" : observing && (status?.automatedPressCount ?? 0) > 0 ? "Tap sent · input off" : observing ? "Observing · input off" : "Idle · input off");
  const nextAction = $derived(!connected ? "Waiting for the local engine to reconnect." : !targetValid ? "Select a FiveM window above before starting." : seconds > 0 ? "Wait for cooldown to finish before the next prediction." : observing ? status?.inputArmed ? "Keep FiveM focused. One automatic press is armed." : "Watching the selected window. Automatic input is off." : inputMode === "Observe" ? "Start observing, then play the minigame manually." : "Arm one tap, then open the minigame in FiveM.");
  const colors = { White: "#e7eceb", Purple: "#ac88ef", Red: "#ef7580", PaleGreen: "#b8d8a1", Blue: "#7cd5ee", Yellow: "#e4d333" };
  const label = (color: string) => color === "PaleGreen" ? "Pale green" : color;
  const position = (x: number) => observation?.bar.width ? Math.max(0, Math.min(100, (x - observation.bar.x) / observation.bar.width * 100)) : 0;
  onMount(() => {
    if (status) policy = status.targetPolicy;
    const timer = setInterval(() => now = Date.now(), 250);
    return () => clearInterval(timer);
  });
  async function toggle() {
    pending = true;
    try { await onmode(observing ? "stop" : "observe", policy, inputMode); }
    catch { /* EngineClient displays the bridge error. */ }
    finally { pending = false; }
  }
  async function changePolicy() {
    pending = true;
    saveMessage = "Saving…";
    saveFailed = false;
    try { await onpolicy(policy, inputMode, { redAdvanceMs, yellowAdvanceMs, customPriority, itemPriority }); saveMessage = "Saved."; }
    catch { policy = status?.targetPolicy ?? "Widest"; inputMode = status?.inputMode ?? "Observe"; saveFailed = true; saveMessage = "Could not save. Previous values restored."; }
    finally { pending = false; }
  }
  async function adjust(color: "red" | "yellow", direction: "earlier" | "later") {
    const previous = color === "red" ? redAdvanceMs : yellowAdvanceMs;
    const next = timingStep(previous, direction);
    if (color === "red") redAdvanceMs = next; else yellowAdvanceMs = next;
    await changePolicy();
    if (saveMessage === "Saved.") saveMessage = `${color === "red" ? "Red" : "Yellow"}: ${previous} → ${next} ms. Press ${direction} by 1 ms. Saved.`;
  }
  async function setItemPriority(current: string, replacement: string) {
    itemPriority = changeItemPriority(itemPriority, current, replacement);
    await changePolicy();
  }
  async function setPriority(index: number, value: PickpocketColor | "") {
    customPriority = changePriority(customPriority, index, value);
    policy = "Custom";
    await changePolicy();
  }
  const timeLabel = (timestamp: number) => new Date(timestamp).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
  async function debugCommand(action: "copy" | "open") {
    debugFailed = false;
    try {
      if (action === "copy") {
        const report = savedAttempt ? attemptReport(savedAttempt) : status?.debug?.report;
        if (!report) return;
        await navigator.clipboard.writeText(report);
        debugAction = savedAttempt ? "Selected attempt report copied." : "Current session report copied.";
      } else {
        await invoke("open_diagnostics");
        debugAction = "Open the pickpocket folder for session files.";
      }
    } catch (failure) { debugFailed = true; debugAction = `Could not ${action === "copy" ? "copy report" : "open logs"}: ${String(failure)}`; }
  }
</script>

<section class="live-heading">
  <div><span class="kicker">Live workspace</span><p>{nextAction}</p></div>
  <span class="input-badge" class:observing class:armed={connected && status?.inputArmed && seconds === 0} class:paused={!connected || seconds > 0} role="status"><ShieldCheck size={16} /> {sessionLabel}</span>
</section>

<nav class="workspace-tabs" aria-label="Pickpocket workspace"><button class:active={!historyOpen} aria-pressed={!historyOpen} onclick={() => setWorkspace(false)}>Live</button><button class:active={historyOpen} aria-pressed={historyOpen} onclick={() => setWorkspace(true)}>History</button><button aria-pressed="false" disabled={observing} onclick={onreference}>Reference</button></nav>
{#if historyOpen}<PickpocketHistory />{:else}
<div class="live-layout">
  <section class="reader" aria-labelledby="live-target-title">
    <div class="section-top"><h2 id="live-target-title">Precision controls</h2><Crosshair size={18} /></div>
    <nav class="workspace-tabs" aria-label="Pickpocket controls">{#each ["Run", "Timing", "Items", "Priority"] as tab}<button class:active={view === tab} aria-pressed={view === tab} onclick={() => view = tab as typeof view}>{tab}</button>{/each}</nav>
    <div class="control-page">
    {#if view === "Run"}
    <div class="run-options"><div><label for="pp-input-mode">Run mode</label><select id="pp-input-mode" bind:value={inputMode} onchange={changePolicy} disabled={observing || pending || !connected || !status?.inputMode}>
      <option value="Observe">Manual · observe only</option><option value="SingleAttempt">Automatic · wide targets</option>
      <option value="PrecisionAttempt">Precision · all target sizes</option>
    </select></div><div><label for="pp-live-policy">Aim for</label>
    <select id="pp-live-policy" bind:value={policy} onchange={changePolicy} disabled={observing || pending || !connected}>
      {#each targetChoices as choice}<option value={choice.value}>{choice.label}</option>{/each}
    </select></div></div>
    <p class="explanation">{targetHint(policy, inputMode, customPriority)}</p>
    <div class="strategy"><span>{inputMode === "Observe" ? "Manual Space" : "One automatic press"}</span><span>{inputMode === "PrecisionAttempt" ? "Thin targets · survey first" : inputMode === "SingleAttempt" ? "Wide windows only" : "Input stays off"}</span><span>{policy === "Widest" ? "Widest ignores item rank" : "Color rank → known item rank → width"}</span></div>
    {:else if view === "Timing"}
      <div class="run-options">
        <div><label for="pp-red-advance">Red · earlier by</label><select id="pp-red-advance" bind:value={redAdvanceMs} onchange={changePolicy} disabled={observing || pending || !connected}>{#each Array.from({length:21}, (_, i) => i) as ms}<option value={ms}>{ms} ms{ms === 8 ? " · red-tested" : ""}</option>{/each}</select></div>
        <div><label for="pp-yellow-advance">Yellow · earlier by</label><select id="pp-yellow-advance" bind:value={yellowAdvanceMs} onchange={changePolicy} disabled={observing || pending || !connected}>{#each Array.from({length:21}, (_, i) => i) as ms}<option value={ms}>{ms} ms</option>{/each}</select></div>
      </div>
      <div class="run-options timing-actions">{#each ["red", "yellow"] as color}<div><button onclick={() => adjust(color as "red" | "yellow", "later")} disabled={observing || pending || !connected || (color === "red" ? redAdvanceMs : yellowAdvanceMs) === 0} aria-label={`${color} press later`}>Press later −1</button><button onclick={() => adjust(color as "red" | "yellow", "earlier")} disabled={observing || pending || !connected || (color === "red" ? redAdvanceMs : yellowAdvanceMs) === 20} aria-label={`${color} press earlier`}>Press earlier +1</button></div>{/each}</div>
      <p class="explanation">Higher values press earlier: 17 → 14 ms presses 3 ms later. Wider targets retain their normal timing.</p>
    {:else if view === "Priority"}
      <div class="priority-order">{#each [0,1,2,3,4,5] as index}<div>
        <label for={`pp-priority-${index}`}>{index === 0 ? "1 · First choice" : `${index + 1} · Next choice`}</label>
        <select id={`pp-priority-${index}`} style:border-left-color={customPriority[index] ? colors[customPriority[index]] : "var(--line)"} value={customPriority[index] ?? ""} disabled={observing || pending || !connected} onchange={event => setPriority(index, event.currentTarget.value as PickpocketColor | "")}>
          <option value="" disabled={customPriority.length === 1 && index === 0}>Skip</option>
          {#each itemGroups as group}<option value={group.color}>{label(group.color)}</option>{/each}
        </select>
      </div>{/each}</div>
      <p class="explanation">First available color wins. Changes save and activate My priority order. Rank matching items in Items.</p>
    {:else}
      <div class="item-group"><label for="pp-item-color">Within color</label>
      <select id="pp-item-color" bind:value={itemColor}>{#each itemGroups as group}<option value={group.color}>{label(group.color)} · {group.items.length} known {group.items.length === 1 ? "item" : "items"}</option>{/each}</select></div>
      <div class="item-ranking">{#each rankedItems as name, index}<div><label for={`pp-item-rank-${index}`}>{index + 1} · {index === 0 ? "First choice" : "Then"}</label>
        <select id={`pp-item-rank-${index}`} style:border-left-color={colors[itemColor]} value={name} disabled={observing || pending || !connected || rankedItems.length < 2} onchange={event => setItemPriority(name, event.currentTarget.value)}>{#each itemGroup.items as item}<option value={item.name}>{item.name}</option>{/each}</select>
      </div>{/each}</div>
      <p class="explanation">Known cards follow this order, wherever they appear. Unreadable cards rank last; ties use the wider region. Auto-saved.</p>
    {/if}
    <p class="save-feedback" class:save-failed={saveFailed} role="status">{saveMessage || "Changes save automatically."}</p>
    </div>
    <div class="controls">
      <button class="run" class:stop={observing} onclick={toggle} disabled={pending || !connected || !status || (!observing && !targetValid)}>
        {#if observing}<OctagonX size={16} />{:else}<Eye size={16} />{/if}{pending ? "Please wait…" : observing ? "Stop safely" : inputMode !== "Observe" ? "Arm one tap" : "Start observing"}<kbd>{shortcut}</kbd>
      </button>
      <span class="target-ready">{!targetValid ? "Select FiveM above to continue" : inputMode === "Observe" ? "No automatic input" : "One press per attempt"}</span>
    </div>
    <p class="pp-live-detail" class:live-error={!!error} role={error ? "alert" : "status"}>{error || (!connected ? "Connect to the local engine to observe." : !status ? "Restart CuePilot to load the updated engine." : status.detail)}</p>
    <div class="readout">
      <div class="reader-top"><span class="kicker">02 / Current bar</span><span>{selectedBand ? `${selectedBand.itemName ?? label(selectedBand.color)} · ${Math.round(selectedBand.right - selectedBand.left)} px · ${bands.length} regions` : bands.length ? `${bands.length} regions detected` : "Waiting for the minigame"}</span></div>
      <div class="scale" aria-hidden="true"><span>LEFT</span><span>RIGHT</span></div>
      <div class="track" role="img" aria-label={bands.length ? `Detected bar with ${bands.length} colored regions and a moving marker` : "No live bar detected"}>
        {#each bands as band, index}
          <span class="region" class:selected={index === status?.selectedBandIndex} style:left={`${position(band.left)}%`} style:width={`${position(band.right) - position(band.left)}%`} style:background={colors[band.color]}></span>
        {/each}
        {#if observation && observation.bar.width > 0}<span class="marker" style:left={`${position(observation.markerX)}%`}></span>{/if}
      </div>
      <div class="legend"><span><i></i> Moving marker</span><span>Outlined region = selected target</span></div>
      {#if bands.length}
        <div class="regions">{#each bands.filter((_, index) => index === status?.selectedBandIndex || index < 3) as band}<span class:chosen={band === bands[status?.selectedBandIndex ?? -1]}><i style:background={colors[band.color]}></i>{label(band.color)} <b>{Math.round(band.right - band.left)} px</b></span>{/each}{#if bands.length > 4}<span>All {bands.length} shown above</span>{/if}</div>
      {:else if observing}<p class="empty">{status?.inputArmed ? "Waiting for the minigame. One tap is armed." : "Watching for the minigame. Automatic input is off."}</p>{/if}
    </div>
  </section>
  <aside aria-label="Live timing and cooldown">
    <div class="cooldown"><div class="section-top"><span class="kicker">Next attempt</span><Timer size={18} /></div><strong class:counting={seconds > 0}>{!connected ? "Offline" : seconds > 0 ? countdown : !targetValid ? "Set up" : observing ? "In progress" : "Ready"}</strong><p>{!connected ? "Waiting for the engine." : seconds > 0 ? "Cooldown active. Predictions paused." : !targetValid ? "Select a FiveM window above." : "3-minute cooldown after each result."}</p></div>
    <div class="pp-live-telemetry"><div class="inspector-tabs"><button class:active={!showMeasurements} aria-pressed={!showMeasurements} onclick={() => showMeasurements = false}>Result</button><button class:active={showMeasurements} aria-pressed={showMeasurements} onclick={() => showMeasurements = true}>Diagnostics</button></div>
    {#if showMeasurements}<dl>
      <div><dt>Observer</dt><dd>{connected ? status?.state ?? "Unavailable" : "Disconnected"}</dd></div>
      <div><dt>Capture</dt><dd>{observing ? `${status!.captureMilliseconds.toFixed(1)} ms` : "—"}</dd></div>
      <div><dt>Analysis</dt><dd>{observing ? `${status!.analysisMilliseconds.toFixed(1)} ms` : "—"}</dd></div>
      <div><dt>Image age</dt><dd>{observing && status?.frameAgeMilliseconds != null ? `${status.frameAgeMilliseconds.toFixed(1)} ms` : "—"}</dd></div>
      <div><dt>Marker speed</dt><dd>{observing && status?.prediction?.speedPixelsPerSecond ? `${Math.round(status.prediction.speedPixelsPerSecond)} px/s` : "—"}</dd></div>
      <div><dt>Timing candidates</dt><dd>{status?.predictedPressCount ?? 0}</dd></div>
      <div><dt>Samples</dt><dd>{status?.sampleCount ?? 0}</dd></div>
      <div><dt>Space observed</dt><dd>{status?.manualSpacePressCount ?? 0}</dd></div>
      <div><dt>Auto Space</dt><dd>{status?.automatedPressCount ?? 0} / 1</dd></div>
    </dl>{:else}<div class="shot-result">
      <p class="result-context">{savedAttempt ? `Saved result · ${timeLabel(savedAttempt.endedAtUnixMs)}` : observing ? "Current attempt" : "No completed attempt"}</p>
      {#if recent.length}<select class="history-select" aria-label="Recent attempts" bind:value={selectedHistoryId}><option value="">{observing ? "Current attempt" : "Latest result"}</option>{#each recent as attempt}<option value={attempt.id}>{timeLabel(attempt.endedAtUnixMs)} · {attempt.color ? label(attempt.color) : "Unknown"} · {attempt.outcome}</option>{/each}</select>{:else}<span class="kicker">Last attempt</span>{/if}
      <strong class:missed={result?.state === "Missed"}>{result?.state === "Grabbed" ? "Grabbed" : result?.state === "Missed" ? "Missed" : result?.state === "Ended" ? "Result not seen" : status?.automatedPressCount ? "Press sent" : "Ready to measure"}</strong>
      {#if result}<p>{result.color ? label(result.color) : "Unknown target"}{result.widthPixels != null ? ` · ${result.widthPixels.toFixed(1)} px region` : ""}</p><div class="result-offset">{result.offsetPixels == null ? "Offset unavailable" : Math.abs(result.offsetPixels) < .05 ? "At the center" : `${Math.abs(result.offsetPixels).toFixed(1)} px ${result.offsetPixels > 0 ? "past" : "before"} center`}</div><p>Visual offset, not input latency.</p>
      {:else}<p>{observing ? "Watching the current attempt. Thin targets wait for the return." : "Start an attempt to see its result here."}</p>{/if}
      <div class="strategy"><span>{displayedPresses} / 1 auto press</span></div>
    </div>{/if}
    {#if showMeasurements || status?.debug?.state === "Error" || status?.debug?.state === "Limited"}<p class="debug-health" class:warning={!connected || status?.debug?.state === "Error" || status?.debug?.state === "Limited"} title={status?.debug?.error ?? "Automatically records timing decisions and bounded local images."}>Debug: {!connected ? "Disconnected" : status?.debug?.state ?? "Ready"}{#if status?.debug} · {status.debug.recordsSaved} records{/if}</p>{/if}
    {#if status?.debug && (status.debug.recordsDropped > 0 || status.debug.imagesSkipped > 0)}<p class="warning">{status.debug.recordsDropped} records / {status.debug.imagesSkipped} images skipped</p>{/if}
    </div>
  </aside>
</div>
<footer><ShieldCheck size={16} /><div class="debug-caption"><p><strong>{status?.inputArmed ? "One tap armed" : "Input off"}</strong> · <kbd>Pause / Break</kbd> stops and releases Space</p><p class="evidence" class:warning={debugFailed || !!storageIssue} role="status" title={storageIssue || debugAction || status?.evidenceDirectory}>{storageIssue || debugAction || (status?.evidenceDirectory ? `Saved locally: ${status.evidenceDirectory}` : "Debug recording starts automatically with F7.")}</p></div><div class="debug-actions"><button onclick={() => debugCommand("copy")} disabled={!savedAttempt && !status?.debug?.report}><Copy size={14} /> {savedAttempt ? "Copy selected report" : "Copy current report"}</button><button onclick={() => debugCommand("open")}><FolderOpen size={14} /> Open logs</button></div></footer>
{/if}

<style>
  .timing-actions { margin-top:8px; } .timing-actions > div { display:flex; gap:4px; } .timing-actions button { padding:6px; font-size:10px; } .save-feedback { font-size:10px; color:var(--accent); margin:0; }
  .workspace-tabs,.inspector-tabs { display:flex; gap:4px; padding:3px; border:1px solid var(--line); border-radius:9px; margin-bottom:10px; }
  .workspace-tabs button,.inspector-tabs button { flex:1; justify-content:center; border:0; padding:7px 10px; color:var(--text-muted); }
  .workspace-tabs button.active,.inspector-tabs button.active { background:var(--line); color:var(--accent); }
  .control-page { min-height:132px; }
  .save-feedback.save-failed { color: #ffabab; }
  .priority-order { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:10px; }
  .priority-order label { font-size:10px; margin-bottom:5px; }
  .priority-order select { padding:8px; font-size:11px; border-left-width:3px; }
  .history-select { padding:6px; font-size:10px; margin-bottom:2px; }
  .strategy { display:flex; gap:7px; flex-wrap:wrap; font-size:10px; color:var(--text-muted); }
  .strategy span { padding:4px 7px; background:var(--bg); border:1px solid var(--line); border-radius:5px; }
  .item-ranking { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:8px; margin-top:8px; }
  .item-group { display:grid; grid-template-columns:90px minmax(0,1fr); align-items:center; gap:8px; }
  .item-group label { margin:0; }
  .item-ranking label { font-size:10px; margin-bottom:4px; }
  .item-ranking select { font-size:11px; padding:8px; border-left-width:3px; }
  .shot-result { min-height:178px; padding:6px 0 12px; }
  .shot-result strong { display:block; margin:12px 0; font-size:22px; color:var(--accent); font-weight:500; }
  .shot-result strong.missed { color:#efb774; }
  .result-offset { margin:14px 0 7px; font-size:16px; font-variant-numeric:tabular-nums; }
  .shot-result .strategy { margin-top:18px; }
  .run-options { display:grid; grid-template-columns:1fr 1fr; gap:10px; }
  .run-options > div { min-width:0; }
  .live-heading { display:flex; justify-content:space-between; gap:24px; align-items:center; margin:5px 0 9px; }
  .live-heading p:last-child { color:var(--text-muted); font-size:12px; margin:4px 0 0; }
  .input-badge { display:flex; gap:8px; white-space:nowrap; align-items:center; color:var(--accent); font-size:12px; border:1px solid var(--line); border-radius:20px; padding:9px 13px; }
  .live-layout { display:grid; grid-template-columns:minmax(0,1fr) 280px; gap:22px; }
  .reader, aside { border:1px solid var(--line); border-radius:16px; background:var(--panel); overflow:hidden; }
  .reader { padding:20px; }
  .section-top, .reader-top { display:flex; justify-content:space-between; align-items:center; gap:14px; }
  .section-top > :global(svg) { color:var(--accent); }
  .kicker { color:var(--text-muted); font-size:10px; letter-spacing:.1em; text-transform:uppercase; }
  h2 { font-size:19px; letter-spacing:-.4px; margin:8px 0 16px; }
  label { display:block; font-size:12px; margin-bottom:9px; }
  select { width:100%; background:var(--bg); color:var(--text); border:1px solid var(--line); border-radius:9px; padding:13px; font:inherit; font-size:13px; }
  button:focus-visible, select:focus-visible { outline:2px solid var(--accent); outline-offset:3px; }
  .explanation, .pp-live-detail, .empty, aside p, footer p { color:var(--text-muted); font-size:12px; line-height:1.7; }
  .explanation { margin:10px 0 18px; }
  .readout { padding:18px 0; border-top:1px solid var(--line); border-bottom:1px solid var(--line); }
  .reader-top > span:last-child { color:var(--text-muted); font-size:11px; }
  .scale { display:flex; justify-content:space-between; color:var(--text-muted); font-size:9px; letter-spacing:.1em; margin:16px 0 13px; }
  .track { position:relative; height:25px; border-radius:5px; background:#202a2b; }
  .region { position:absolute; top:0; height:100%; border-radius:2px; }
  .region.selected { outline:2px solid var(--accent); outline-offset:5px; }
  .marker { position:absolute; top:-9px; height:43px; width:3px; background:#bbf944; transform:translateX(-50%); box-shadow:0 0 10px #bafa4450; }
  .legend { display:flex; justify-content:space-between; gap:10px; margin-top:16px; font-size:10px; color:var(--text-muted); }
  .legend span:first-child, .regions span { display:flex; align-items:center; gap:7px; }
  .legend i { width:5px; height:5px; background:#bbf944; border-radius:50%; }
  .empty { margin:14px 0 0; }
  .regions { display:flex; flex-wrap:wrap; gap:8px; margin-top:20px; }
  .regions span { border:1px solid var(--line); border-radius:6px; padding:6px 8px; font-size:10px; }
  .regions span.chosen { border-color:var(--accent); }
  .regions i { width:6px; height:6px; border-radius:2px; }
  .regions b { font-weight:400; color:var(--text-muted); }
  .controls { display:flex; flex-wrap:wrap; align-items:center; gap:16px; margin-top:22px; }
  button { display:flex; gap:8px; align-items:center; background:transparent; color:var(--text); border:1px solid var(--line); border-radius:8px; padding:11px 14px; font:inherit; font-size:12px; cursor:pointer; }
  .run { color:#082924; background:var(--accent); border-color:var(--accent); font-weight:600; }
  .run.stop { color:#ffcecb; border-color:#af615e; background:#4a282c; }
  kbd { font:10px "Cascadia Code",Consolas,monospace; border:1px solid var(--line-strong); border-radius:4px; padding:2px 5px; white-space:nowrap; }
  .run kbd { border-color:#0b584450; margin-left:4px; }
  .run.stop kbd { border-color:#ffcecb60; }
  .input-badge.armed { color:var(--warning); border-color:var(--warning); background:var(--muted-warning); }
  .input-badge.paused { color:var(--warning); }
  .result-context { color:var(--text-muted); font-size:11px; margin:0 0 9px; }
  footer strong { font-weight:500; color:var(--text); }
  button:disabled, select:disabled { opacity:.5; cursor:default; }
  .target-ready { font-size:11px; color:var(--text-muted); }
  .pp-live-detail { margin:12px 0 20px; }
  .cooldown, .pp-live-telemetry { padding:24px; }
  .cooldown { border-bottom:1px solid var(--line); background:linear-gradient(135deg,#25423950,transparent); }
  .cooldown strong { display:block; font-size:43px; letter-spacing:-1.5px; margin:20px 0 10px; font-weight:500; font-variant-numeric:tabular-nums; }
  .counting { color:var(--accent); }
  aside p { margin:0; font-size:11px; }
  dl { margin:16px 0; } dl div { display:flex; gap:12px; justify-content:space-between; padding:9px 0; font-size:11px; } dt { color:var(--text-muted); } dd { margin:0; font-variant-numeric:tabular-nums; }
  footer { display:flex; gap:9px; color:var(--text-muted); margin-top:10px; padding:0; } footer > :global(svg) { flex-shrink:0; color:var(--accent); margin-top:2px; } footer > div { min-width:0; } footer p { margin:0; font-size:11px; } .evidence { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .pp-live-detail.live-error { color:#ffabab; border-left:2px solid #ec7171; padding-left:9px; overflow-wrap:anywhere; }
  .debug-caption { flex:1; }
  .debug-actions { display:flex; align-items:center; gap:6px; flex-shrink:0; }
  .debug-actions button { font-size:10px; padding:7px 9px; white-space:nowrap; }
  .debug-health { color:var(--accent); }
  .warning { color:#efb774; }
  @media(max-width:900px) { .live-layout { grid-template-columns:1fr; } aside { display:grid; grid-template-columns:1fr 1fr; } .cooldown { border-bottom:0; border-right:1px solid var(--line); } }
  @media(max-width:560px) { .live-heading { align-items:flex-start; gap:12px; } .reader { padding:18px; } aside { grid-template-columns:1fr; } .cooldown { border-right:0; border-bottom:1px solid var(--line); } .reader-top { flex-wrap:wrap; } }
  @media(min-width:620px) {
    .live-heading { margin:2px 0 8px; gap:12px; }
    .live-heading p:last-child { margin:0; font-size:12px; }
    .live-layout { grid-template-columns:minmax(0,1fr) 235px; gap:14px; }
    aside { display:block; }
    .reader { padding:18px; }
    h2 { font-size:17px; margin:0 0 14px; }
    select { padding:10px; font-size:12px; }
    .explanation { margin:8px 0 12px; line-height:1.5; }
    .controls { margin-top:12px; gap:10px; }
    .pp-live-detail { margin:10px 0 14px; line-height:1.5; }
    .readout { padding:12px 0 0; border-bottom:0; }
    .scale { margin:12px 0 10px; }
    .legend { margin-top:12px; }
    .regions { gap:5px; margin-top:12px; }
    .regions span { padding:4px 6px; }
    .cooldown,.pp-live-telemetry { padding:16px; }
    .cooldown { border-right:0; border-bottom:1px solid var(--line); }
    .cooldown strong { margin:10px 0 6px; font-size:36px; }
    dl { margin:10px 0; } dl div { padding:6px 0; }
  }
  @media(min-width:620px) and (max-height:650px) {
    .live-heading { margin:0 0 7px; }
    .control-page { min-height:105px; }
    .reader { padding:12px 15px; }
    .workspace-tabs { margin-bottom:8px; }
    .readout { padding:10px 0; }
    .controls { margin-top:11px; }
    .pp-live-detail { margin:6px 0 10px; line-height:1.4; }
    .cooldown,.pp-live-telemetry { padding:14px; }
    .cooldown strong { font-size:34px; margin:10px 0 5px; }
    footer { display:none; }
  }
  @media(min-width:620px) and (min-height:651px) and (max-height:800px) {
    .live-heading { margin:0 0 8px; }
    .reader { padding-block:14px; }
    .control-page { min-height:112px; }
    .readout { padding-block:12px; }
    .controls { margin-top:12px; }
    .pp-live-detail { margin-block:7px 12px; }
    footer { margin-top:0; }
    footer .evidence { display:none; }
  }
  @media(min-width:620px) and (max-height:720px) {
    .workspace-tabs { margin-bottom:8px; }
    .workspace-tabs button { padding:5px 10px; }
    .control-page { min-height:86px; }
    .control-page .strategy,.empty { display:none; }
    .control-page .explanation { margin:6px 0; font-size:10px; }
    .readout { padding-top:10px; }
    .legend,.scale { margin-top:9px; }
    .live-heading { margin:0 0 10px; }
    .live-heading p:last-child { display:none; }
    .live-layout { grid-template-columns:minmax(0,1fr) 215px; gap:12px; }
    .reader { padding:10px 14px; }
    select { padding:8px; }
    .pp-live-detail { margin:8px 0 10px; }
    .regions { display:none; }
    h2 { font-size:16px; margin-bottom:10px; }
    .explanation,.pp-live-detail,.empty { font-size:11px; }
    .controls { margin-top:8px; }
    .scale { display:none; }
    .track { margin-top:13px; }
    .legend { margin-top:9px; font-size:10px; }
    .cooldown,.pp-live-telemetry { padding:10px 12px; }
    .shot-result { min-height:150px; padding:0 0 6px; }
    .shot-result strong { margin:8px 0; font-size:20px; }
    .result-offset { margin:8px 0 5px; font-size:14px; }
    .shot-result .strategy { margin-top:8px; }
    dl div { padding:3px 0; }
    .cooldown strong { font-size:32px; }
    footer p { font-size:10px; line-height:1.4; }
  }
</style>
