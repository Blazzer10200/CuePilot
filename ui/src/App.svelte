<script lang="ts">
  import { onMount } from "svelte";
  import { invoke } from "@tauri-apps/api/core";
  import { getCurrentWindow } from "@tauri-apps/api/window";
  import {
    Activity, AlertTriangle, Check, Crosshair, FolderOpen, Gauge, GripHorizontal,
    Maximize2, Minus, Play, Radio, RefreshCw, Settings2, ShieldCheck, Square,
    Waves, X,
  } from "@lucide/svelte";
  import { EngineClient, type RoutineSettings, type RoutineState } from "./lib/engine.svelte";

  interface DiagnosticsSnapshot {
    directory: string;
    recentSamples: string[];
    latestLoss: { name: string; imageData: string | null } | null;
  }

  const engine = new EngineClient();
  let selectingTarget = $state(false);
  let showSettings = $state(false);
  let savingSettings = $state(false);
  let settingsError = $state<string | null>(null);
  let draft = $state<RoutineSettings | null>(null);
  let showDiagnostics = $state(false);
  let diagnostics = $state<DiagnosticsSnapshot | null>(null);
  let diagnosticsLoading = $state(false);
  let diagnosticsError = $state<string | null>(null);

  const active = $derived(!["Stopped", "Faulted"].includes(engine.status.state));
  const target = $derived(engine.snapshot?.settings.routine.targetWindow);
  const stateLabel = $derived(stateTitle(engine.status.state));
  const confidence = $derived(Math.round(engine.status.confidence * 100));
  const currentStep = $derived(cycleStep(engine.status.state));

  onMount(() => {
    engine.onEvent = (name) => {
      if (name === "target" && selectingTarget) {
        selectingTarget = false;
        void restoreConsole();
      }
    };
    void engine.connect().catch((error: unknown) => engine.error = String(error));
    return () => engine.onEvent = undefined;
  });

  function stateTitle(state: RoutineState) {
    return ({
      Stopped: "Ready to fish",
      Casting: "Casting line",
      Armed: "Watching for meter",
      Regulating: "Controlling tension",
      Collecting: "Collecting fish",
      Stowing: "Preparing next cast",
      Faulted: "Automation paused",
    } as Record<RoutineState, string>)[state];
  }

  function cycleStep(state: RoutineState) {
    return ({ Stopped: 0, Casting: 1, Armed: 2, Regulating: 3, Collecting: 4, Stowing: 4, Faulted: 0 } as Record<RoutineState, number>)[state];
  }

  function cloneRoutine(routine: RoutineSettings): RoutineSettings {
    return { ...routine, targetWindow: { ...routine.targetWindow } };
  }

  async function run() {
    try {
      await engine.command(active ? "stop" : "start");
    } catch (error) {
      engine.error = String(error);
    }
  }

  async function selectTarget() {
    selectingTarget = true;
    engine.error = null;
    try {
      await getCurrentWindow().minimize();
      await engine.command("capture_target", 3500);
    } catch (error) {
      selectingTarget = false;
      engine.error = String(error);
      await restoreConsole();
    }
  }

  async function restoreConsole() {
    await getCurrentWindow().show();
    await getCurrentWindow().setFocus();
  }

  function openSettings() {
    if (!engine.snapshot) return;
    draft = cloneRoutine(engine.snapshot.settings.routine);
    settingsError = null;
    showSettings = true;
  }

  async function saveSettings() {
    if (!draft || !engine.snapshot) return;
    if (draft.fishingUpperTensionPercent < draft.fishingLowerTensionPercent + 5) {
      settingsError = "Target tension must be at least 5% above the pulse threshold.";
      return;
    }
    savingSettings = true;
    settingsError = null;
    try {
      await engine.saveSettings({ ...engine.snapshot.settings, routine: cloneRoutine(draft) });
      showSettings = false;
    } catch (error) {
      settingsError = String(error);
    } finally {
      savingSettings = false;
    }
  }

  async function inspectDiagnostics() {
    showDiagnostics = true;
    diagnosticsLoading = true;
    diagnosticsError = null;
    try {
      diagnostics = await invoke<DiagnosticsSnapshot>("diagnostics_snapshot");
    } catch (error) {
      diagnosticsError = String(error);
    } finally {
      diagnosticsLoading = false;
    }
  }

  async function openDiagnosticsFolder() {
    try {
      await invoke("open_diagnostics");
    } catch (error) {
      diagnosticsError = String(error);
    }
  }

  function closePanels() {
    showSettings = false;
    showDiagnostics = false;
  }

  async function minimize() { await getCurrentWindow().minimize(); }
  async function maximize() { await getCurrentWindow().toggleMaximize(); }
  async function close() { await getCurrentWindow().close(); }

  async function startDragging(event: PointerEvent) {
    if (event.button !== 0 || (event.target as Element).closest("button")) return;
    await getCurrentWindow().startDragging();
  }
</script>

<svelte:head><meta name="theme-color" content="#071518" /></svelte:head>
<svelte:window onkeydown={(event) => event.key === "Escape" && closePanels()} />

<main class:running={active}>
  <div class="titlebar" role="group" aria-label="Window controls" onpointerdown={startDragging}>
    <div class="brand">
      <div class="mark" aria-hidden="true"><Waves size={17} strokeWidth={2.4} /></div>
      <span>WORKFLOW LOOPER</span><small>FIELD CONSOLE</small>
    </div>
    <div class="drag-hint" aria-hidden="true"><GripHorizontal size={16} /> DRAG WINDOW</div>
    <div class="top-actions">
      <div class:offline={!engine.connected} class="title-signal"><Radio size={13} /> LOCAL ENGINE {engine.connected ? "ONLINE" : "CONNECTING"}</div>
      <button aria-label="Minimize" onclick={minimize}><Minus size={15} /></button>
      <button aria-label="Maximize" onclick={maximize}><Maximize2 size={14} /></button>
      <button class="close" aria-label="Close" onclick={close}><X size={15} /></button>
    </div>
  </div>

  <section class="hero" aria-labelledby="state-heading">
    <div class="hero-copy">
      <p class="eyebrow"><Activity size={14} /> LIVE FISHING CONTROL</p>
      <h1 id="state-heading">{stateLabel}</h1>
      <p class="detail">{selectingTarget ? "Bring FiveM forward now. The target locks in after the short countdown." : engine.status.detail}</p>
      {#if engine.error}<p class="error"><AlertTriangle size={15} /> {engine.error}</p>{/if}
    </div>
    <div class="control-orbit" aria-label="Current detector confidence">
      <div class="orbit-ring"></div><div class="orbit-core"><strong>{confidence}%</strong><span>LOCK</span></div>
    </div>
  </section>

  <section class="instrument" aria-label="Live engine telemetry">
    <article class="target-card">
      <div class="card-kicker"><Crosshair size={15} /> GAME TARGET</div>
      <strong>{target?.processName || "No target selected"}</strong>
      <span>{target?.windowTitle || "Select FiveM before you start."}</span>
      <button class="target-button" onclick={selectTarget} disabled={active || selectingTarget || !engine.connected}>
        <Crosshair size={16} /> {selectingTarget ? "CAPTURING FIVEM…" : "SELECT FIVEM TARGET"}
      </button>
    </article>
    <div class="metric"><span>SAMPLE COUNT</span><strong>{engine.status.sampleCount.toLocaleString()}</strong></div>
    <div class="metric"><span>DETECTOR LOCK</span><strong>{confidence}%</strong></div>
    <div class="metric"><span>INPUT MODE</span><strong>{engine.snapshot?.settings.routine.inputMode || "—"}</strong></div>
  </section>

  <section class="cycle" aria-label="Routine cycle">
    {#each ["TARGET", "CAST", "METER", "TENSION", "COLLECT"] as step, index}
      <div class:complete={active && index < currentStep} class:current={index === currentStep} class="cycle-step"><span>{String(index + 1).padStart(2, "0")}</span>{step}</div>
    {/each}
  </section>

  <section class="actions">
    <button class:stop={active} class="primary-action" onclick={run} disabled={!engine.connected || (!active && !target?.processName)}>
      {#if active}<Square size={20} fill="currentColor" /> STOP & RELEASE INPUT{:else}<Play size={20} fill="currentColor" /> START AUTOMATION{/if}
    </button>
    <button class="sub-action" onclick={openSettings} disabled={active || !engine.snapshot}><Settings2 size={17} /> SETTINGS</button>
    <button class="sub-action" onclick={inspectDiagnostics}><FolderOpen size={17} /> REVIEW DIAGNOSTICS</button>
  </section>

  <section class="operator-note">
    <ShieldCheck size={17} /><p><strong>SAFE CONTROL:</strong> settings are saved only while stopped; <kbd>PAUSE / BREAK</kbd> always stops and releases held input.</p>
  </section>

  <footer><span>LOCAL ONLY</span> no cloud connection <i></i> FiveM must remain visible and foreground</footer>
</main>

{#if showSettings && draft}
  <div class="scrim" role="presentation" onclick={closePanels}></div>
  <div class="drawer" aria-label="Controller settings" aria-modal="true" role="dialog">
    <header class="panel-header"><div><p>FISHING PROFILE</p><h2>Controller envelope</h2></div><button aria-label="Close settings" onclick={closePanels}><X size={18} /></button></header>
    <p class="panel-copy">These values apply to the next run. Keep the proven envelope unless you have a tested reason to tune it.</p>
    <div class="form-grid">
      <label>Pulse threshold <span>Percent</span><input bind:value={draft.fishingLowerTensionPercent} min="25" max="80" type="number" /></label>
      <label>Target tension <span>Percent</span><input bind:value={draft.fishingUpperTensionPercent} min="30" max="85" type="number" /></label>
      <label>Sample interval <span>Milliseconds</span><input bind:value={draft.fishingSampleMilliseconds} min="20" max="200" type="number" /></label>
      <label>Maximum pulse <span>Milliseconds</span><input bind:value={draft.fishingMaximumPulseMilliseconds} min={draft.fishingMinimumPulseMilliseconds} max="120" type="number" /></label>
      <label>Minimum rest <span>Milliseconds</span><input bind:value={draft.fishingMinimumRestMilliseconds} min="20" max="250" type="number" /></label>
      <label>Safety time <span>Seconds</span><input bind:value={draft.maximumDurationSeconds} min="5" max="3600" type="number" /></label>
    </div>
    <label class="select-label">INPUT DELIVERY<select bind:value={draft.inputMode}><option value="Automatic">Automatic — focus FiveM</option><option value="Foreground">Foreground only</option><option value="Application">Experimental — background</option></select></label>
    {#if settingsError}<p class="error"><AlertTriangle size={15} /> {settingsError}</p>{/if}
    <div class="panel-actions"><button class="sub-action" onclick={closePanels}>CANCEL</button><button class="primary-action compact" onclick={saveSettings} disabled={savingSettings}>{savingSettings ? "SAVING…" : "APPLY SETTINGS"} <Check size={17} /></button></div>
  </div>
{/if}

{#if showDiagnostics}
  <div class="scrim" role="presentation" onclick={closePanels}></div>
  <div class="drawer diagnostics" aria-label="Diagnostics" aria-modal="true" role="dialog">
    <header class="panel-header"><div><p>LOCAL EVIDENCE</p><h2>Detection review</h2></div><button aria-label="Close diagnostics" onclick={closePanels}><X size={18} /></button></header>
    <p class="panel-copy">The most recent meter-loss frame and tail of the detector log, kept on this machine for tuning.</p>
    {#if diagnosticsLoading}<div class="empty-state"><RefreshCw size={19} class="spin" /> Loading local diagnostics…</div>
    {:else if diagnosticsError}<p class="error"><AlertTriangle size={15} /> {diagnosticsError}</p>
    {:else if diagnostics}
      {#if diagnostics.latestLoss?.imageData}<figure><img src={diagnostics.latestLoss.imageData} alt="Latest meter loss capture" /><figcaption>{diagnostics.latestLoss.name}</figcaption></figure>
      {:else}<div class="empty-state"><Gauge size={19} /> No saved meter-loss image yet.</div>{/if}
      <div class="log"><div class="log-label">LAST FISHING SAMPLES <span>{diagnostics.recentSamples.length}</span></div>{#if diagnostics.recentSamples.length}<pre>{diagnostics.recentSamples.join("\n")}</pre>{:else}<p>No detector samples have been recorded yet.</p>{/if}</div>
    {:else}<div class="empty-state">Diagnostics are unavailable.</div>{/if}
    <div class="panel-actions"><button class="sub-action" onclick={inspectDiagnostics}><RefreshCw size={15} /> REFRESH</button><button class="primary-action compact" onclick={openDiagnosticsFolder}><FolderOpen size={16} /> OPEN FOLDER</button></div>
  </div>
{/if}
