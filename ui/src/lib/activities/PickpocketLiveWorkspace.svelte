<script lang="ts">
  import { onMount, untrack } from "svelte";
  import { Timer, ShieldCheck, OctagonX, Eye, Copy, FolderOpen, Lock } from "@lucide/svelte";
  import { invoke } from "@tauri-apps/api/core";
  import PickpocketHistory from "./PickpocketHistory.svelte";
  import { attemptReport, timingStep } from "./history";
  import { cooldownSeconds } from "./pickpocket-session";
  import type { PickpocketObserveStatus, PickpocketPolicy, PickpocketInputMode, PickpocketTiming, PickpocketColor } from "../engine.svelte";
  import { itemGroups, targetChoices, targetHint, defaultPriority, changePriority, defaultItemPriority, changeItemPriority } from "./pickpocket-catalog";

  let { status, connected, targetValid, error, onmode, shortcut, onpolicy, historyOpen }: {
    status?: PickpocketObserveStatus; connected: boolean; targetValid: boolean; error: string | null;
    onmode: (mode: "observe" | "stop", policy: PickpocketPolicy, inputMode: PickpocketInputMode) => Promise<void>;
    shortcut: string;
    onpolicy: (policy: PickpocketPolicy, inputMode: PickpocketInputMode, timing?: PickpocketTiming) => Promise<void>;
    historyOpen: boolean;
  } = $props();
  let policy = $state<PickpocketPolicy>("Widest");
  let inputMode = $state<PickpocketInputMode>("Observe");
  let pending = $state(false);
  let saveMessage = $state("");
  let saveFailed = $state(false);
  let view = $state<"Run" | "Timing" | "Items" | "Priority">("Run");
  let showMeasurements = $state(false);
  let redAdvanceMs = $state(11);
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
    if (pending || !status) return;
    const nextPolicy = status.targetPolicy;
    const nextInputMode = status.inputMode ?? "Observe";
    const nextRedAdvanceMs = status.redAdvanceMs ?? 11;
    const nextYellowAdvanceMs = status.yellowAdvanceMs ?? 20;
    const nextCustomPriority = status.customPriority ?? defaultPriority;
    const nextItemPriority = status.itemPriority ?? defaultItemPriority;
    // Local fields are read untracked: a user edit must not re-run this effect and revert itself before its change handler saves it.
    untrack(() => {
      if (policy !== nextPolicy) policy = nextPolicy;
      if (inputMode !== nextInputMode) inputMode = nextInputMode;
      if (redAdvanceMs !== nextRedAdvanceMs) redAdvanceMs = nextRedAdvanceMs;
      if (yellowAdvanceMs !== nextYellowAdvanceMs) yellowAdvanceMs = nextYellowAdvanceMs;
      if (customPriority.length !== nextCustomPriority.length || customPriority.some((value, index) => value !== nextCustomPriority[index])) {
        customPriority = [...nextCustomPriority];
      }
      if (itemPriority.length !== nextItemPriority.length || itemPriority.some((value, index) => value !== nextItemPriority[index])) {
        itemPriority = [...nextItemPriority];
      }
    });
  });
  let now = $state(Date.now());
  let debugAction = $state("");
  let debugFailed = $state(false);
  const storageIssue = $derived(status?.sessionStateError || status?.debug?.error);
  const observing = $derived(connected && status?.observing === true);
  const observation = $derived(observing ? status?.observation : undefined);
  const bands = $derived(observation?.bands ?? []);
  const selectedBand = $derived(bands[status?.selectedBandIndex ?? -1]);
  const seconds = $derived(cooldownSeconds(status, now));
  const armed = $derived(connected && !!status?.inputArmed && seconds === 0);
  const countdown = $derived(`${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`);
  const nextAction = $derived(!connected ? "Waiting for the local engine to reconnect." : !targetValid ? "Select a FiveM window above before starting." : seconds > 0 ? "Wait for cooldown to finish before the next prediction." : observing ? status?.inputArmed ? "Keep FiveM focused. One automatic press is armed." : "Watching the selected window. Automatic input is off." : inputMode === "Observe" ? "Start observing, then play the minigame manually." : "Arm one tap, then open the minigame in FiveM.");
  const colors = { White: "#e7eceb", Purple: "#ac88ef", Red: "#ef7580", PaleGreen: "#b8d8a1", Blue: "#7cd5ee", Yellow: "#e4d333" };
  const label = (color: string) => color === "PaleGreen" ? "Pale green" : color;
  const position = (x: number) => observation?.bar.width ? Math.max(0, Math.min(100, (x - observation.bar.x) / observation.bar.width * 100)) : 0;
  const locked = $derived(observing || pending || !connected);
  onMount(() => {
    if (status) policy = status.targetPolicy;
    let timer = 0;
    const start = () => { if (!timer) timer = setInterval(() => now = Date.now(), 250); };
    const stop = () => { if (timer) { clearInterval(timer); timer = 0; } };
    const visibilityChanged = () => { if (document.hidden) stop(); else start(); };
    document.addEventListener("visibilitychange", visibilityChanged);
    start();
    return () => { document.removeEventListener("visibilitychange", visibilityChanged); stop(); };
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

{#if historyOpen}<PickpocketHistory />{:else}
<div class="live-layout">
  <div class="live-main">
    <section class="card bar-card" aria-labelledby="live-bar-title">
      <div class="card-top">
        <h2 id="live-bar-title">Current bar</h2>
        <span class="card-meta">{#if selectedBand}Target: <b>{selectedBand.itemName ?? label(selectedBand.color)}</b> · {Math.round(selectedBand.right - selectedBand.left)} px · {bands.length} regions{:else if bands.length}{bands.length} regions detected{:else}Waiting for the minigame{/if}</span>
      </div>
      <div class="track" role="img" aria-label={bands.length ? `Detected bar with ${bands.length} colored regions and a moving marker` : "No live bar detected"}>
        {#each bands as band, index}
          <span class="region" class:selected={index === status?.selectedBandIndex} style:left={`${position(band.left)}%`} style:width={`${position(band.right) - position(band.left)}%`} style:--region={colors[band.color]}></span>
        {/each}
        {#if observation && observation.bar.width > 0}<span class="marker" style:left={`${position(observation.markerX)}%`}><span>marker</span></span>{/if}
      </div>
      {#if bands.length}
        <div class="regions">{#each bands.filter((_, index) => index === status?.selectedBandIndex || index < 3) as band}<span class:chosen={band === selectedBand} style:--region={colors[band.color]}><i></i>{label(band.color)} <b>{Math.round(band.right - band.left)} px</b></span>{/each}{#if bands.length > 4}<span>All {bands.length} shown above</span>{/if}</div>
      {:else}<p class="empty">{observing ? status?.inputArmed ? "Waiting for the minigame. One tap is armed." : "Watching for the minigame. Automatic input is off." : "The bar appears here once you start observing and open the minigame."}</p>{/if}
    </section>

    <section class="card controls-card" aria-labelledby="live-target-title">
      <div class="card-top">
        <h2 id="live-target-title">Precision controls</h2>
        <nav class="segmented segmented--inset" aria-label="Pickpocket controls">{#each ["Run", "Timing", "Items", "Priority"] as tab}<button class:active={view === tab} aria-pressed={view === tab} onclick={() => view = tab as typeof view}>{tab}</button>{/each}</nav>
      </div>
      <div class="control-page">
      {#if view === "Run"}
        <div class="run-options"><div><label for="pp-input-mode">Run mode</label><select id="pp-input-mode" bind:value={inputMode} onchange={changePolicy} disabled={locked || !status?.inputMode}>
          <option value="Observe">Manual · observe only</option><option value="SingleAttempt">Automatic · wide targets</option>
          <option value="PrecisionAttempt">Precision · all target sizes</option>
        </select></div><div><label for="pp-live-policy">Aim for</label>
        <select id="pp-live-policy" bind:value={policy} onchange={changePolicy} disabled={locked}>
          {#each targetChoices as choice}<option value={choice.value}>{choice.label}</option>{/each}
        </select></div></div>
        <p class="explanation">{targetHint(policy, inputMode, customPriority)}</p>
        <div class="strategy"><span>{inputMode === "Observe" ? "Manual Space" : "One automatic press"}</span><span>{inputMode === "PrecisionAttempt" ? "Thin targets · survey first" : inputMode === "SingleAttempt" ? "Wide windows only" : "Input stays off"}</span><span>{policy === "Widest" ? "Widest ignores item rank" : "Color rank → known item rank → width"}</span></div>
      {:else if view === "Timing"}
        <div class="run-options">
          <div><label for="pp-red-advance">Red · earlier by</label><select id="pp-red-advance" bind:value={redAdvanceMs} onchange={changePolicy} disabled={locked}>{#each Array.from({length:21}, (_, i) => i) as ms}<option value={ms}>{ms} ms{ms === 11 ? " · default" : ms === 8 ? " · red-tested" : ""}</option>{/each}</select></div>
          <div><label for="pp-yellow-advance">Yellow · earlier by</label><select id="pp-yellow-advance" bind:value={yellowAdvanceMs} onchange={changePolicy} disabled={locked}>{#each Array.from({length:21}, (_, i) => i) as ms}<option value={ms}>{ms} ms</option>{/each}</select></div>
        </div>
        <div class="run-options timing-actions">{#each ["red", "yellow"] as color}<div><button class="step" onclick={() => adjust(color as "red" | "yellow", "later")} disabled={locked || (color === "red" ? redAdvanceMs : yellowAdvanceMs) === 0} aria-label={`${color} press later`}>Press later −1</button><button class="step" onclick={() => adjust(color as "red" | "yellow", "earlier")} disabled={locked || (color === "red" ? redAdvanceMs : yellowAdvanceMs) === 20} aria-label={`${color} press earlier`}>Press earlier +1</button></div>{/each}</div>
        <p class="explanation">Higher values press earlier: 17 → 14 ms presses 3 ms later. Wider targets retain their normal timing.</p>
        {#if status?.calibration}<p class="explanation">{status.calibration}</p>{/if}
      {:else if view === "Priority"}
        <div class="priority-order">{#each [0,1,2,3,4,5] as index}<div>
          <label for={`pp-priority-${index}`}>{index === 0 ? "1 · First choice" : `${index + 1} · Next choice`}</label>
          <select id={`pp-priority-${index}`} style:border-left-color={customPriority[index] ? colors[customPriority[index]] : "var(--line)"} value={customPriority[index] ?? ""} disabled={locked} onchange={event => setPriority(index, event.currentTarget.value as PickpocketColor | "")}>
            <option value="" disabled={customPriority.length === 1 && index === 0}>Skip</option>
            {#each itemGroups as group}<option value={group.color}>{label(group.color)}</option>{/each}
          </select>
        </div>{/each}</div>
        <p class="explanation">First available color wins. Changes save and activate My priority order. Rank matching items in Items.</p>
      {:else}
        <div class="item-group"><label for="pp-item-color">Within color</label>
        <select id="pp-item-color" bind:value={itemColor}>{#each itemGroups as group}<option value={group.color}>{label(group.color)} · {group.items.length} known {group.items.length === 1 ? "item" : "items"}</option>{/each}</select></div>
        <div class="item-ranking">{#each rankedItems as name, index}<div><label for={`pp-item-rank-${index}`}>{index + 1} · {index === 0 ? "First choice" : "Then"}</label>
          <select id={`pp-item-rank-${index}`} style:border-left-color={colors[itemColor]} value={name} disabled={locked || rankedItems.length < 2} onchange={event => setItemPriority(name, event.currentTarget.value)}>{#each itemGroup.items as item}<option value={item.name}>{item.name}</option>{/each}</select>
        </div>{/each}</div>
        <p class="explanation">Known cards follow this order, wherever they appear. Unreadable cards rank last; ties use the wider region. Auto-saved.</p>
      {/if}
      {#if observing}<p class="locked-hint"><Lock size={14} /> Locked while observing — stop to change.</p>
      {:else}<p class="save-feedback" class:save-failed={saveFailed} role="status">{saveMessage || "Changes save automatically."}</p>{/if}
      </div>
      <div class="controls">
        <button class="run" class:stop={observing} onclick={toggle} disabled={pending || !connected || !status || (!observing && !targetValid)}>
          {#if observing}<OctagonX size={16} />{:else}<Eye size={16} />{/if}{pending ? "Please wait…" : observing ? "Stop safely" : inputMode !== "Observe" ? "Arm one tap" : "Start observing"}<kbd>{shortcut}</kbd>
        </button>
        <p class="next-action">{nextAction}</p>
      </div>
      <p class="pp-live-detail" class:live-error={!!error} role={error ? "alert" : "status"}>{error || (!connected ? "Connect to the local engine to observe." : !status ? "Restart CuePilot to load the updated engine." : status.detail)}</p>
    </section>
  </div>

  <aside class="live-side" aria-label="Live timing and cooldown">
    <section class="card next-card">
      <h2 class="card-title"><Timer size={14} /> Next attempt</h2>
      <strong class:counting={seconds > 0}>{!connected ? "Offline" : seconds > 0 ? countdown : !targetValid ? "Set up" : observing ? "In progress" : "Ready"}</strong>
      <p>{!connected ? "Waiting for the engine." : seconds > 0 ? "Cooldown active. Predictions paused." : !targetValid ? "Select a FiveM window above." : "3-minute cooldown after each result."}</p>
    </section>
    <section class="card result-card">
      <nav class="segmented segmented--inset segmented--fill" aria-label="Result view"><button class:active={!showMeasurements} aria-pressed={!showMeasurements} onclick={() => showMeasurements = false}>Result</button><button class:active={showMeasurements} aria-pressed={showMeasurements} onclick={() => showMeasurements = true}>Diagnostics</button></nav>
      <div class="result-body">
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
        {#if savedAttempt || !recent.length}<p class="result-context">{savedAttempt ? `Saved result · ${timeLabel(savedAttempt.endedAtUnixMs)}` : observing ? "Current attempt" : "No completed attempt"}</p>{/if}
        {#if recent.length}<select class="history-select" aria-label="Recent attempts" bind:value={selectedHistoryId}><option value="">{observing ? "Current attempt" : "Latest result"}</option>{#each recent as attempt}<option value={attempt.id}>{timeLabel(attempt.endedAtUnixMs)} · {attempt.color ? label(attempt.color) : "Unknown"} · {attempt.outcome}</option>{/each}</select>{/if}
        <strong class:grabbed={result?.state === "Grabbed"} class:missed={result?.state === "Missed"}>{result?.state === "Grabbed" ? "Grabbed" : result?.state === "Missed" ? "Missed" : result?.state === "Ended" ? "Result not seen" : status?.automatedPressCount ? "Press sent" : "Ready to measure"}</strong>
        {#if result}<p>{result.color ? label(result.color) : "Unknown target"}{result.widthPixels != null ? ` · ${result.widthPixels.toFixed(1)} px region` : ""}</p><div class="result-offset">{result.offsetPixels == null ? "Offset unavailable" : Math.abs(result.offsetPixels) < .05 ? "At the center" : `${Math.abs(result.offsetPixels).toFixed(1)} px ${result.offsetPixels > 0 ? "past" : "before"} center`}</div><p class="result-note">Visual offset, not input latency.</p>
        {:else}<p>{observing ? "Watching the current attempt. Thin targets wait for the return." : "Start an attempt to see its result here."}</p>{/if}
        <span class="chip auto-chip">{displayedPresses} / 1 auto press</span>
      </div>{/if}
      {#if showMeasurements || status?.debug?.state === "Error" || status?.debug?.state === "Limited"}<p class="debug-health" class:warning={!connected || status?.debug?.state === "Error" || status?.debug?.state === "Limited"} title={status?.debug?.error ?? "Automatically records timing decisions and bounded local images."}>Debug: {!connected ? "Disconnected" : status?.debug?.state ?? "Ready"}{#if status?.debug} · {status.debug.recordsSaved} records{/if}</p>{/if}
      {#if status?.debug && (status.debug.recordsDropped > 0 || status.debug.imagesSkipped > 0)}<p class="warning">{status.debug.recordsDropped} records / {status.debug.imagesSkipped} images skipped</p>{/if}
      </div>
    </section>
  </aside>
</div>
<footer class="live-footer">
  <p class="safety"><ShieldCheck size={15} /><strong>{armed ? "One tap armed." : "Input off."}</strong><kbd>Pause / Break</kbd>stops and releases Space.</p>
  <p class="evidence" class:warning={debugFailed || !!storageIssue} role="status" title={storageIssue || debugAction || status?.evidenceDirectory}>{storageIssue || debugAction}</p>
  <div class="debug-actions"><button onclick={() => debugCommand("copy")} disabled={!savedAttempt && !status?.debug?.report} title={savedAttempt ? "Copy the selected attempt's report" : "Copy the current session report"}><Copy size={13} /> Copy report</button><button onclick={() => debugCommand("open")} title={status?.evidenceDirectory ? `Saved locally: ${status.evidenceDirectory}` : "Open the local session folder"}><FolderOpen size={13} /> Open logs</button></div>
</footer>
{/if}

<style>
  .live-layout { flex:1; min-height:0; display:grid; grid-template-columns:minmax(0,1fr) 300px; gap:14px; }
  .live-main, .live-side { display:flex; flex-direction:column; gap:14px; min-width:0; min-height:0; }
  .card { border:1px solid var(--line); border-radius:var(--radius-card); background:var(--panel); min-width:0; }
  .card-top { display:flex; align-items:center; justify-content:space-between; gap:12px; min-width:0; }
  h2 { margin:0; color:var(--text); font-size:var(--fs-title); font-weight:620; }
  .card-title { display:flex; align-items:center; gap:7px; color:var(--text-2); font-size:var(--fs-small); font-weight:600; }
  .card-meta { overflow:hidden; color:var(--text-2); font-size:var(--fs-small); text-overflow:ellipsis; white-space:nowrap; }
  .card-meta b { color:var(--text); font-weight:600; }

  /* Current bar */
  .bar-card { padding:20px 22px 18px; flex-shrink:0; }
  .track { position:relative; height:36px; margin-top:30px; border:1px solid #494744; border-radius:5px; background:repeating-linear-gradient(90deg,transparent 0,transparent calc(25% - 1px),#3a3936 calc(25% - 1px),#3a3936 25%),var(--surface-sunken); }
  .region { position:absolute; top:0; bottom:0; background:var(--region); opacity:.55; }
  .region.selected { z-index:1; opacity:1; box-shadow:0 0 0 2px var(--surface-sunken),0 0 0 4px var(--region); }
  .marker { position:absolute; z-index:2; top:-12px; bottom:-12px; width:2px; background:#ade85c; transform:translateX(-50%); box-shadow:0 0 10px rgba(173,232,92,.4); }
  .marker > span { position:absolute; bottom:calc(100% + 4px); left:50%; color:#ade85c; font:600 var(--fs-meta)/1 "Cascadia Code",Consolas,monospace; white-space:nowrap; transform:translateX(-50%); }
  .regions { display:flex; flex-wrap:wrap; gap:6px; margin-top:22px; }
  .regions span { display:inline-flex; align-items:center; gap:6px; height:26px; padding:0 9px; border:1px solid rgba(255,255,255,.1); border-radius:var(--radius-chip); color:var(--text-2); font-size:var(--fs-caption); }
  .regions span.chosen { border-color:color-mix(in srgb,var(--region) 50%,transparent); color:var(--text); }
  .regions i { width:8px; height:8px; border-radius:2px; background:var(--region); }
  .regions b { color:var(--text-3); font-weight:400; }
  .empty { margin:18px 0 0; color:var(--text-3); font-size:var(--fs-small); }

  /* Precision controls */
  .controls-card { flex:1; min-height:0; display:flex; flex-direction:column; gap:14px; padding:18px 22px 20px; }
  .control-page { flex:1; min-height:0; overflow-y:auto; }
  .run-options { display:grid; grid-template-columns:1fr 1fr; gap:12px; }
  .run-options > div { min-width:0; }
  label { display:block; margin-bottom:6px; color:var(--text-3); font-size:var(--fs-caption); }
  select { width:100%; height:40px; padding:0 12px; border:1px solid rgba(255,255,255,.12); border-radius:var(--radius-control); background:var(--surface-sunken); color:var(--text); font:inherit; font-size:13px; }
  select:disabled { color:var(--text-3); cursor:not-allowed; }
  .explanation { margin:12px 0 0; color:var(--text-2); font-size:var(--fs-small); line-height:1.5; }
  .strategy { display:flex; flex-wrap:wrap; gap:6px; margin-top:12px; }
  .chip, .strategy span { display:inline-flex; align-items:center; height:26px; padding:0 9px; border:1px solid var(--line); border-radius:var(--radius-chip); background:var(--surface-sunken); color:var(--text-2); font-size:var(--fs-caption); white-space:nowrap; }
  .timing-actions { margin-top:10px; }
  .timing-actions > div { display:flex; gap:6px; }
  .step { flex:1; height:32px; padding:0 10px; border:1px solid var(--line-strong); border-radius:7px; background:transparent; color:var(--text); font:inherit; font-size:var(--fs-caption); cursor:pointer; transition:border-color var(--dur-fast) var(--ease-out); }
  .step:hover:not(:disabled) { border-color:rgba(255,255,255,.28); }
  .step:disabled { opacity:.5; cursor:not-allowed; }
  .priority-order, .item-ranking { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:10px; }
  .item-ranking { margin-top:10px; }
  .priority-order select, .item-ranking select { border-left-width:3px; }
  .item-group { display:grid; grid-template-columns:96px minmax(0,1fr); align-items:center; gap:10px; }
  .item-group label { margin:0; }
  .save-feedback, .locked-hint { display:flex; align-items:center; gap:6px; margin:12px 0 0; color:var(--text-3); font-size:var(--fs-caption); }
  .save-feedback.save-failed { color:var(--danger-text); }
  .controls { display:flex; flex-wrap:wrap; align-items:center; gap:10px 16px; }
  .run { display:inline-flex; align-items:center; gap:9px; height:46px; min-width:230px; padding:0 10px 0 16px; border:1px solid var(--accent); border-radius:var(--radius-button); background:var(--accent); color:#fff8f3; font:inherit; font-size:14px; font-weight:650; cursor:pointer; transition:background-color var(--dur-fast) var(--ease-out),border-color var(--dur-fast) var(--ease-out); }
  .run:hover:not(:disabled) { background:#df8364; border-color:#df8364; }
  .run kbd { margin-left:auto; padding:4px 7px; border:1px solid rgba(255,255,255,.28); border-radius:var(--radius-kbd); background:rgba(0,0,0,.16); font:700 11.5px "Cascadia Code",Consolas,monospace; }
  .run.stop { border-color:var(--danger-line); background:var(--danger-bg); color:var(--danger-text); }
  .run.stop:hover:not(:disabled) { background:rgba(229,105,95,.22); border-color:var(--danger-line); }
  .run.stop kbd { border-color:rgba(255,217,214,.25); background:transparent; }
  .run:disabled { opacity:.5; cursor:not-allowed; }
  .next-action { flex:1; min-width:160px; margin:0; color:var(--text-2); font-size:var(--fs-small); line-height:1.45; }
  .pp-live-detail { margin:-4px 0 0; color:var(--text-3); font-size:var(--fs-caption); line-height:1.45; }
  .pp-live-detail.live-error { padding-left:9px; border-left:2px solid var(--danger); color:var(--danger-text); overflow-wrap:anywhere; }

  /* Right column */
  .next-card { flex-shrink:0; padding:18px 20px; }
  .next-card strong { display:block; margin:12px 0 6px; color:var(--text-strong); font-family:"Segoe UI Variable Display","Segoe UI",sans-serif; font-size:30px; font-weight:650; letter-spacing:-.02em; line-height:1.1; }
  .next-card strong.counting { color:var(--accent-text); }
  .next-card p { margin:0; color:var(--text-3); font-size:var(--fs-small); }
  .result-card { flex:1; min-height:0; display:flex; flex-direction:column; gap:12px; padding:16px 20px 18px; }
  .result-body { flex:1; min-height:0; display:flex; flex-direction:column; overflow-y:auto; }
  .shot-result { flex:1; display:flex; flex-direction:column; }
  .result-context { margin:0 0 8px; color:var(--text-3); font-size:var(--fs-caption); }
  .history-select { height:32px; font-size:var(--fs-caption); }
  .shot-result strong { display:block; margin:10px 0 4px; color:var(--text-strong); font-size:22px; font-weight:650; letter-spacing:-.01em; }
  .shot-result strong.grabbed { color:var(--accent-text); }
  .shot-result strong.missed { color:var(--warning-text); }
  .shot-result p { margin:0; color:var(--text-2); font-size:var(--fs-small); line-height:1.5; }
  .shot-result .result-note { color:var(--text-3); font-size:var(--fs-caption); }
  .result-offset { margin:8px 0 4px; color:var(--text); font-size:14px; font-variant-numeric:tabular-nums; }
  .auto-chip { align-self:flex-start; margin-top:auto; }
  .shot-result p + .auto-chip { margin-top:auto; }
  dl { margin:0; }
  dl div { display:flex; justify-content:space-between; gap:12px; padding:5px 0; font-size:var(--fs-small); }
  dt { color:var(--text-3); }
  dd { margin:0; color:var(--text); font-variant-numeric:tabular-nums; }
  .debug-health { margin:10px 0 0; color:var(--accent-text); font-size:var(--fs-caption); }
  .warning { color:var(--warning-text); }
  .result-body > .warning { margin:6px 0 0; font-size:var(--fs-caption); }

  /* Footer */
  .live-footer { display:flex; align-items:center; gap:12px; min-width:0; padding-top:10px; border-top:1px solid var(--line); color:var(--text-3); font-size:var(--fs-caption); }
  .safety { display:flex; align-items:center; gap:7px; margin:0; white-space:nowrap; }
  .safety strong { color:var(--text); font-weight:600; }
  .live-footer kbd { padding:2px 6px; border:1px solid var(--line-strong); border-radius:var(--radius-kbd); background:rgba(0,0,0,.18); color:var(--text); font:600 var(--fs-meta) "Cascadia Code",Consolas,monospace; }
  .evidence { flex:1; min-width:0; margin:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .evidence.warning { color:var(--warning-text); }
  .debug-actions { display:flex; gap:6px; flex-shrink:0; margin-left:auto; }
  .debug-actions button { display:inline-flex; align-items:center; gap:6px; height:30px; padding:0 10px; border:1px solid var(--line-strong); border-radius:7px; background:transparent; color:var(--text-2); font:inherit; font-size:var(--fs-caption); white-space:nowrap; cursor:pointer; transition:border-color var(--dur-fast) var(--ease-out),color var(--dur-fast) var(--ease-out); }
  .debug-actions button:hover:not(:disabled) { border-color:rgba(255,255,255,.22); color:var(--text); }
  .debug-actions button:disabled { opacity:.5; cursor:not-allowed; }

  /* Narrow windows keep two columns down to the 760px minimum. */
  @media (max-width:1040px) { .live-layout { grid-template-columns:minmax(0,1fr) 250px; } .run { min-width:200px; } }
  @media (max-width:880px) {
    .live-layout { grid-template-columns:minmax(0,1fr) 220px; gap:12px; }
    .live-main, .live-side { gap:12px; }
    .bar-card, .controls-card { padding-inline:16px; }
    .next-card, .result-card { padding-inline:16px; }
    .run { min-width:0; }
    .safety > :global(svg) { display:none; }
  }
  @media (max-height:800px) {
    .bar-card { padding-block:14px; }
    .track { margin-top:26px; }
    .regions { margin-top:14px; }
    .empty { margin-top:12px; }
    .controls-card { gap:10px; padding-block:14px 16px; }
    .next-card { padding-block:14px; }
    .next-card strong { margin:8px 0 4px; font-size:26px; }
    .result-card { gap:10px; padding-block:12px 14px; }
  }
  @media (max-height:720px) {
    .live-layout, .live-main, .live-side { gap:10px; }
    .bar-card { padding-block:12px; }
    .track { height:30px; margin-top:24px; }
    .regions, .empty, .strategy { display:none; }
    .controls-card { gap:8px; padding-block:12px; }
    select { height:36px; }
    .explanation { margin-top:8px; font-size:var(--fs-caption); }
    .save-feedback, .locked-hint { margin-top:8px; }
    .run { height:42px; }
    .next-action { font-size:var(--fs-caption); }
    .pp-live-detail:not(.live-error) { display:none; }
    .next-card { padding-block:12px; }
    .next-card strong { font-size:24px; }
    .shot-result strong { margin-top:6px; font-size:20px; }
    .live-footer { padding-top:8px; }
  }
</style>
