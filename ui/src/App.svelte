<script lang="ts">
  import { onMount, tick } from "svelte";
  import { fade, fly } from "svelte/transition";
  import { invoke } from "@tauri-apps/api/core";
  import { getCurrentWindow } from "@tauri-apps/api/window";
  import {
    Activity, AlertTriangle, ArrowLeft, Check, ChevronDown, ChevronRight, Crosshair, FolderOpen,
    Gauge, GripHorizontal, Maximize2, Minus, Monitor, Radio, RefreshCw, ScanEye, Settings2,
    ShieldCheck, Terminal, Waves, X,
  } from "@lucide/svelte";
  import { EngineClient, type FishingDebugSnapshot, type FishingSetupVerification, type HotkeyBinding, type LockpickingObserveStatus, type RoutineSettings, type RoutineState, type TargetCandidate } from "./lib/engine.svelte";
  import { getActivity, type ActivityId } from "./lib/activities";
  import ActivityPicker from "./lib/activities/ActivityPicker.svelte";
  import LockpickingWorkspace from "./lib/activities/LockpickingWorkspace.svelte";

  const developmentBuild = import.meta.env.DEV;
  const applicationName = developmentBuild ? "CuePilot Dev" : "CuePilot";
  const brandIcon = developmentBuild ? "/cuepilot-dev-icon.png" : "/cuepilot-icon.png";

  interface DiagnosticsSnapshot {
    recentSamples: string[];
    latestSample: {
      name: string;
      imageData: string | null;
      metadata: {
        eventName: "meter-lock" | "meter-loss";
        tracked: boolean;
        evidence: {
          darkDisk: number;
          diskContrast: number;
          ringStrength: number;
          lmbPrompt: number;
        } | null;
      } | null;
    } | null;
    debugSession: {
      directory: string;
      manifest: FishingDebugSnapshot;
      recentEvents: Array<{
        sequence: number;
        elapsedMilliseconds: number;
        category: string;
        eventName: string;
      }>;
      frames: Array<{
        label: string;
        score: number;
        elapsedMilliseconds: number;
        imageName: string;
        imageData: string | null;
        imageAvailable: boolean;
      }>;
    } | null;
  }

  interface DiagnosticEntry {
    raw: string;
    parsed: boolean;
    elapsedMilliseconds: number;
    visible: boolean;
    tensionPercent: number;
    progressPercent: number;
    caught: boolean;
    failed: boolean;
    confidencePercent: number;
    inputDown: boolean;
    eventName: string;
    pulseMilliseconds: number;
  }

  const deliveryOptions: Array<{ value: RoutineSettings["inputMode"]; label: string; description: string }> = [
    { value: "Automatic", label: "Automatic", description: "Focus FiveM before delivery" },
    { value: "Foreground", label: "Foreground only", description: "Require FiveM to stay active" },
  ];
  const shortcutOptions = ["F6", "F7", "F8", "F9", "F10", "F11", "F12"];

  const engine = new EngineClient();
  const stoppedLockpicking: LockpickingObserveStatus = {
    observing: false,
    state: "Stopped",
    detail: "Connect the local engine to begin observation.",
    sampleCount: 0,
    confidence: 0,
    captureBackend: "None",
    captureMilliseconds: 0,
    accumulatedFrames: 1,
    spin: null,
    inputEnabled: false,
    vehicleClass: "",
    actionCount: 0,
    spinInputActive: false,
    evidenceDirectory: "",
    observation: { state: "Hidden", confidence: 0, hudCenterX: 0, hudCenterY: 0, hudRadius: 0, target: null, visibleTargetCount: 0, predictedAction: "WAIT", reason: "Lockpicking HUD not found." },
  };
  let selectedActivity = $state<ActivityId | null>(null);
  let homeFocusActivity = $state<ActivityId | null>(null);
  let runPending = $state<"start" | "stop" | null>(null);
  let selectingTarget = $state(false);
  let targetPickerOpen = $state(false);
  let targetCandidates = $state<TargetCandidate[]>([]);
  let targetDiscoveryError = $state<string | null>(null);
  let setupVerification = $state<FishingSetupVerification | null>(null);
  let verifyingSetup = $state(false);
  let targetButton = $state<HTMLButtonElement | null>(null);
  let targetPickerElement = $state<HTMLDivElement | null>(null);
  let targetOptionNodes = $state<HTMLButtonElement[]>([]);
  let showSettings = $state(false);
  let savingSettings = $state(false);
  let settingsError = $state<string | null>(null);
  let draft = $state<RoutineSettings | null>(null);
  let shortcutDraft = $state<HotkeyBinding | null>(null);
  let lockpickingShortcutDraft = $state<HotkeyBinding | null>(null);
  let showDiagnostics = $state(false);
  let diagnostics = $state<DiagnosticsSnapshot | null>(null);
  let diagnosticsLoading = $state(false);
  let diagnosticsError = $state<string | null>(null);
  let deliveryOpen = $state(false);
  let deliveryTrigger = $state<HTMLButtonElement | null>(null);
  let deliveryOptionNodes = $state<HTMLButtonElement[]>([]);
  let settingsButton = $state<HTMLButtonElement | null>(null);
  let diagnosticsButton = $state<HTMLButtonElement | null>(null);
  let settingsCloseButton = $state<HTMLButtonElement | null>(null);
  let diagnosticsCloseButton = $state<HTMLButtonElement | null>(null);
  let activePanel = $state<HTMLDivElement | null>(null);
  let reduceMotion = $state(false);
  let notice = $state<{ message: string; tone: "success" | "info" } | null>(null);
  let noticeTimer: ReturnType<typeof setTimeout> | null = null;

  const active = $derived(!["Stopped", "Faulted"].includes(engine.status.state));
  const currentActivity = $derived(selectedActivity ? getActivity(selectedActivity) : null);
  const lockpickingActivity = getActivity("vehicle-lockpicking");
  const fishingSelected = $derived(selectedActivity === "fishing");
  const target = $derived(engine.snapshot?.settings.routine.targetWindow);
  const targetValid = $derived(engine.snapshot?.targetValid ?? false);
  const confidence = $derived(Math.round(engine.status.confidence * 100));
  const currentStep = $derived(cycleStep(engine.status.state));
  const gaugeLabel = $derived(
    engine.status.state === "Faulted"
      ? "Signal lost"
      : selectingTarget
      ? "Scanning"
      : active && confidence === 0
        ? "Searching"
        : confidence >= 80
          ? "Locked"
          : confidence >= 45
            ? "Tracking"
            : confidence > 0
              ? "Weak signal"
              : "Standby",
  );
  const hero = $derived(getHeroMessage({
    connected: engine.connected,
    selectingTarget,
    state: engine.status.state,
    targetValid,
    hasTarget: !!target?.processName,
  }));
  const settingsDirty = $derived(
    !!draft
    && !!shortcutDraft
    && !!lockpickingShortcutDraft
    && !!engine.snapshot
    && (JSON.stringify(draft) !== JSON.stringify(engine.snapshot.settings.routine)
      || JSON.stringify(shortcutDraft) !== JSON.stringify(engine.snapshot.settings.startStop)
      || JSON.stringify(lockpickingShortcutDraft) !== JSON.stringify(engine.snapshot.settings.lockpickingStartStop)),
  );
  const selectedDelivery = $derived(
    deliveryOptions.find((option) => option.value === draft?.inputMode) ?? deliveryOptions[0],
  );
  const diagnosticEntries = $derived(parseDiagnosticSamples(diagnostics?.recentSamples ?? []));
  const debugSnapshot = $derived(engine.status.debug ?? engine.snapshot?.debug ?? diagnostics?.debugSession?.manifest ?? null);

  onMount(() => {
    const motionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
    const syncMotionPreference = () => reduceMotion = motionQuery.matches;
    syncMotionPreference();
    motionQuery.addEventListener("change", syncMotionPreference);
    void engine.connect().catch((error: unknown) => engine.error = String(error));
    return () => {
      motionQuery.removeEventListener("change", syncMotionPreference);
      if (noticeTimer) clearTimeout(noticeTimer);
      void engine.disconnect();
    };
  });

  function getHeroMessage({
    connected,
    selectingTarget,
    state,
    targetValid,
    hasTarget,
  }: {
    connected: boolean;
    selectingTarget: boolean;
    state: RoutineState;
    targetValid: boolean;
    hasTarget: boolean;
  }) {
    if (!connected) return {
      context: "RESTORING LOCAL LINK",
      title: "Connecting the engine",
      detail: "CuePilot is restoring its local control link. Your saved target and settings stay on this PC.",
    };
    if (selectingTarget) return {
      context: "TARGET ACQUISITION",
      title: "Finding your FiveM window",
      detail: "Scanning this desktop for an available FiveM target…",
    };
    if (state === "Faulted") return {
      context: "CONTROL SAFEGUARD",
      title: "Automation paused",
      detail: "CuePilot released input. Review the target, then start again when everything is ready.",
    };
    if (state !== "Stopped") {
      const live = {
        Casting: ["CAST", "Casting the line", "CuePilot is sending the bounded cast action."],
        Armed: ["METER WATCH", "Watching for the meter", "The reader is waiting for a confirmed tension prompt."],
        Regulating: ["TENSION CONTROL", "Managing tension", "Live detector confidence guides the next safe pulse."],
        Collecting: ["COLLECT", "Collecting the catch", "Completing the current fishing cycle."],
        Stowing: ["NEXT CAST", "Preparing the next cast", "The completed cycle is settling before CuePilot continues."],
      } as const;
      const [context, title, detail] = live[state];
      return { context, title, detail };
    }
    if (hasTarget && !targetValid) return {
      context: "TARGET NEEDS ATTENTION",
      title: "FiveM target unavailable",
      detail: "The saved window is not ready. Select its current FiveM window before starting automation.",
    };
    if (!targetValid) return {
      context: "WELCOME BACK",
      title: "Select your FiveM target",
      detail: "Choose the game window once, then CuePilot will keep capture and input safely scoped to it.",
    };
    return {
      context: "WELCOME BACK · TARGET RESTORED",
      title: "Ready to fish",
      detail: "Your saved FiveM target is ready. Use your Start / Stop shortcut from the fishing activity.",
    };
  }

  function cycleStep(state: RoutineState) {
    return ({ Stopped: 0, Casting: 1, Armed: 2, Regulating: 3, Collecting: 4, Stowing: 4, Faulted: 0 } as Record<RoutineState, number>)[state];
  }

  function cycleStepState(index: number): "ready" | "pending" | "active" | "complete" | "fault" {
    if (engine.status.state === "Faulted") return index === currentStep ? "fault" : "pending";
    if (!active) return index === 0 ? (targetValid ? "ready" : "active") : "pending";
    if (index < currentStep) return "complete";
    return index === currentStep ? "active" : "pending";
  }

  function evidencePercent(value: number | undefined) {
    return `${Math.round((value ?? 0) * 100)}%`;
  }

  function parseDiagnosticSamples(samples: string[]): DiagnosticEntry[] {
    return samples
      .map((sample) => sample.trim())
      .filter((sample) => sample.length > 0 && !sample.toLowerCase().startsWith("elapsed_ms,"))
      .map((sample) => {
        const fields = sample.split(",");
        const elapsedMilliseconds = Number(fields[0]);
        const tensionPercent = Number(fields[2]);
        const progressPercent = Number(fields[3]);
        const confidencePercent = Number(fields[6]);
        const pulseMilliseconds = Number(fields[9]);
        const parsed = fields.length >= 10
          && [elapsedMilliseconds, tensionPercent, progressPercent, confidencePercent, pulseMilliseconds]
            .every(Number.isFinite);
        return {
          raw: sample,
          parsed,
          elapsedMilliseconds: parsed ? elapsedMilliseconds : 0,
          visible: fields[1] === "1",
          tensionPercent: parsed ? tensionPercent : 0,
          progressPercent: parsed ? progressPercent : 0,
          caught: fields[4] === "1",
          failed: fields[5] === "1",
          confidencePercent: parsed ? confidencePercent : 0,
          inputDown: fields[7]?.toLowerCase() === "down",
          eventName: fields[8]?.trim() || "sample",
          pulseMilliseconds: parsed ? pulseMilliseconds : 0,
        };
      });
  }

  function diagnosticEventLabel(eventName: string) {
    return ({
      sample: "Meter sample",
      reacquire: "Signal reacquired",
      pulse_start: "Tension pulse",
      pulse_end: "Pulse released",
    } as Record<string, string>)[eventName]
      ?? eventName.replaceAll("_", " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
  }

  function diagnosticStateLabel(entry: DiagnosticEntry) {
    if (entry.failed) return "Failed";
    if (entry.caught) return "Caught";
    if (!entry.visible) return "Meter lost";
    if (entry.eventName === "reacquire") return "Locked";
    if (entry.inputDown) return "LMB held";
    return "Tracking";
  }

  function formatDiagnosticTime(milliseconds: number) {
    const seconds = milliseconds / 1_000;
    return `${seconds.toFixed(seconds < 1 ? 2 : seconds < 10 ? 1 : 0)}s`;
  }

  function formatDiagnosticMetric(value: number) {
    return `${Math.round(value)}%`;
  }

  function announce(message: string, tone: "success" | "info" = "success") {
    if (noticeTimer) clearTimeout(noticeTimer);
    notice = { message, tone };
    noticeTimer = setTimeout(() => {
      notice = null;
      noticeTimer = null;
    }, 2800);
  }

  function cloneRoutine(routine: RoutineSettings): RoutineSettings {
    return { ...routine, targetWindow: { ...routine.targetWindow } };
  }

  function cloneHotkey(binding: HotkeyBinding): HotkeyBinding {
    return { ...binding };
  }

  function hotkeyDisplay(binding: HotkeyBinding | null | undefined) {
    if (!binding) return "F10";
    const parts = [binding.control && "Ctrl", binding.shift && "Shift", binding.alt && "Alt", binding.key]
      .filter(Boolean);
    return parts.join(" + ");
  }

  function sameHotkey(left: HotkeyBinding, right: HotkeyBinding) {
    return left.key.toLowerCase() === right.key.toLowerCase()
      && left.control === right.control
      && left.shift === right.shift
      && left.alt === right.alt;
  }

  async function selectActivity(activityId: ActivityId) {
    if (runPending) return;
    if (active) {
      runPending = "stop";
      try {
        await engine.command("stop");
        announce("Input released before switching activities", "info");
      } catch (error) {
        engine.error = String(error);
        return;
      } finally {
        runPending = null;
      }
    }

    closeTargetPicker(false);
    closePanels();
    homeFocusActivity = activityId;
    selectedActivity = activityId;
    window.scrollTo({ top: 0, behavior: reduceMotion ? "auto" : "smooth" });
  }

  async function returnToActivities() {
    if (runPending) return;
    if (active) {
      runPending = "stop";
      try {
        await engine.command("stop");
        announce("Input released safely", "info");
      } catch (error) {
        engine.error = String(error);
        return;
      } finally {
        runPending = null;
      }
    }

    closeTargetPicker(false);
    closePanels();
    selectedActivity = null;
    window.scrollTo({ top: 0, behavior: reduceMotion ? "auto" : "smooth" });
  }

  async function findTarget() {
    if (selectingTarget) return;
    selectingTarget = true;
    targetPickerOpen = false;
    targetOptionNodes = [];
    targetDiscoveryError = null;
    engine.error = null;
    try {
      targetCandidates = await engine.discoverTargets();
      if (targetCandidates.length === 1) {
        const candidate = targetCandidates[0];
        await engine.selectTarget(candidate.processId);
        announce(candidate.isSelected ? "FiveM target confirmed" : "FiveM target locked");
        return;
      }

      targetPickerOpen = true;
    } catch (error) {
      targetDiscoveryError = String(error);
      engine.error = null;
      targetPickerOpen = true;
    } finally {
      selectingTarget = false;
      if (targetPickerOpen) {
        await tick();
        (targetOptionNodes[0] ?? targetPickerElement)?.focus();
      }
    }
  }

  async function chooseTarget(candidate: TargetCandidate) {
    if (selectingTarget) return;
    selectingTarget = true;
    targetDiscoveryError = null;
    engine.error = null;
    try {
      await engine.selectTarget(candidate.processId);
      setupVerification = null;
      targetPickerOpen = false;
      announce(candidate.isSelected ? "FiveM target confirmed" : "FiveM target locked");
      await tick();
      targetButton?.focus();
    } catch (error) {
      targetDiscoveryError = String(error);
      engine.error = null;
    } finally {
      selectingTarget = false;
    }
  }

  async function verifySetup() {
    if (verifyingSetup || active || !engine.connected) return;
    verifyingSetup = true;
    engine.error = null;
    try {
      setupVerification = await engine.verifySetup();
      announce(setupVerification?.ready ? "Setup verified without sending input" : "Setup needs attention", setupVerification?.ready ? "success" : "info");
    } catch (error) {
      engine.error = String(error);
    } finally {
      verifyingSetup = false;
    }
  }

  function closeTargetPicker(restoreFocus = true) {
    targetPickerOpen = false;
    targetDiscoveryError = null;
    if (restoreFocus) void tick().then(() => targetButton?.focus());
  }

  async function openSettings() {
    if (!engine.snapshot) return;
    draft = cloneRoutine(engine.snapshot.settings.routine);
    shortcutDraft = cloneHotkey(engine.snapshot.settings.startStop);
    lockpickingShortcutDraft = cloneHotkey(engine.snapshot.settings.lockpickingStartStop);
    settingsError = null;
    closeTargetPicker(false);
    deliveryOpen = false;
    showSettings = true;
    await tick();
    settingsCloseButton?.focus();
  }

  async function saveSettings() {
    if (!draft || !shortcutDraft || !lockpickingShortcutDraft || !engine.snapshot) return;
    if (draft.fishingUpperTensionPercent < draft.fishingLowerTensionPercent + 5) {
      settingsError = "Target tension must be at least 5% above the pulse threshold.";
      return;
    }
    if (sameHotkey(shortcutDraft, lockpickingShortcutDraft)) {
      settingsError = "Fishing and Lockpicking must use different Start / Stop shortcuts.";
      return;
    }
    savingSettings = true;
    settingsError = null;
    try {
      await engine.saveSettings({
        ...engine.snapshot.settings,
        startStop: cloneHotkey(shortcutDraft),
        lockpickingStartStop: cloneHotkey(lockpickingShortcutDraft),
        routine: cloneRoutine(draft),
      });
      closePanels();
      announce("Controller profile updated");
    } catch (error) {
      settingsError = String(error);
    } finally {
      savingSettings = false;
    }
  }

  async function inspectDiagnostics() {
    if (diagnosticsLoading) return;
    const refreshing = showDiagnostics && diagnostics !== null;
    showDiagnostics = true;
    if (!refreshing) {
      await tick();
      diagnosticsCloseButton?.focus();
    }
    diagnosticsLoading = true;
    diagnosticsError = null;
    try {
      diagnostics = await invoke<DiagnosticsSnapshot>("diagnostics_snapshot");
      if (refreshing) announce("Diagnostics refreshed", "info");
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
    const returnFocus = showSettings ? settingsButton : showDiagnostics ? diagnosticsButton : null;
    deliveryOpen = false;
    showSettings = false;
    showDiagnostics = false;
    void tick().then(() => returnFocus?.focus());
  }

  function handleWindowClick(event: MouseEvent) {
    if (targetPickerOpen && event.target instanceof Element && !event.target.closest(".target-card")) {
      closeTargetPicker(false);
    }
    if (deliveryOpen && event.target instanceof Element && !event.target.closest(".delivery-select")) {
      deliveryOpen = false;
    }
  }

  function handleWindowKeydown(event: KeyboardEvent) {
    if (event.key !== "Escape") return;
    if (targetPickerOpen) {
      closeTargetPicker();
      return;
    }
    if (deliveryOpen) {
      deliveryOpen = false;
      deliveryTrigger?.focus();
      return;
    }
    closePanels();
  }

  function trapPanelFocus(event: KeyboardEvent) {
    if (event.key !== "Tab" || !activePanel) return;
    const focusable = Array.from(activePanel.querySelectorAll<HTMLElement>(
      'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])',
    )).filter((element) => element.getClientRects().length > 0 && element.getAttribute("aria-hidden") !== "true");
    if (!focusable.length) {
      event.preventDefault();
      activePanel.focus();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const focused = document.activeElement;
    const focusIsInside = focused instanceof Node && activePanel.contains(focused);
    if (event.shiftKey && (!focusIsInside || focused === first)) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && (!focusIsInside || focused === last)) {
      event.preventDefault();
      first.focus();
    }
  }

  async function toggleDelivery() {
    deliveryOpen = !deliveryOpen;
    if (!deliveryOpen) return;
    await tick();
    const selectedIndex = Math.max(0, deliveryOptions.findIndex((option) => option.value === draft?.inputMode));
    deliveryOptionNodes[selectedIndex]?.focus();
  }

  async function openDeliveryFromKeyboard(event: KeyboardEvent) {
    if (!['ArrowDown', 'ArrowUp'].includes(event.key)) return;
    event.preventDefault();
    if (!deliveryOpen) await toggleDelivery();
  }

  function chooseDelivery(value: RoutineSettings["inputMode"]) {
    if (!draft) return;
    draft.inputMode = value;
    deliveryOpen = false;
    deliveryTrigger?.focus();
  }

  function handleDeliveryOptionKeydown(event: KeyboardEvent, index: number) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      chooseDelivery(deliveryOptions[index].value);
      return;
    }

    let nextIndex = index;
    if (event.key === "ArrowDown") nextIndex = (index + 1) % deliveryOptions.length;
    else if (event.key === "ArrowUp") nextIndex = (index - 1 + deliveryOptions.length) % deliveryOptions.length;
    else if (event.key === "Home") nextIndex = 0;
    else if (event.key === "End") nextIndex = deliveryOptions.length - 1;
    else if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      deliveryOpen = false;
      deliveryTrigger?.focus();
      return;
    } else if (event.key === "Tab") {
      deliveryOpen = false;
      return;
    } else {
      return;
    }

    event.preventDefault();
    deliveryOptionNodes[nextIndex]?.focus();
  }

  async function minimize() { await getCurrentWindow().minimize(); }
  async function maximize() { await getCurrentWindow().toggleMaximize(); }
  async function close() { await getCurrentWindow().close(); }

  async function startDragging(event: PointerEvent) {
    if (event.button !== 0 || (event.target as Element).closest("button")) return;
    await getCurrentWindow().startDragging();
  }
</script>

<svelte:head><title>{applicationName}</title><link rel="icon" type="image/png" sizes="128x128" href={brandIcon} /><meta name="theme-color" content="#071518" /></svelte:head>
<svelte:window onkeydown={handleWindowKeydown} onclick={handleWindowClick} />

<main class:running={active} class:activity-home={selectedActivity === null} class:activity-workspace={selectedActivity !== null} inert={showSettings || showDiagnostics} aria-hidden={showSettings || showDiagnostics}>
  <div class="titlebar" role="group" aria-label="Window controls" onpointerdown={startDragging}>
    <div class="brand">
      <div class="mark" aria-hidden="true"><img src={brandIcon} alt="" /></div>
      <span>CUEPILOT{#if developmentBuild}<strong>DEV</strong>{/if}</span><small>{currentActivity ? currentActivity.shortName : "Activity console"}</small>
    </div>
    <div class="drag-hint" aria-hidden="true"><GripHorizontal size={16} /> DRAG WINDOW</div>
    <div class="top-actions">
      <div class:offline={!engine.connected} class="title-signal"><Radio size={13} /> LOCAL ENGINE {engine.connected ? "ONLINE" : "CONNECTING"}</div>
      <button aria-label="Minimize" onclick={minimize}><Minus size={15} /></button>
      <button aria-label="Maximize" onclick={maximize}><Maximize2 size={14} /></button>
      <button class="close" aria-label="Close" onclick={close}><X size={15} /></button>
    </div>
  </div>

  {#if selectedActivity === null}
    <ActivityPicker engineConnected={engine.connected} {targetValid} focusActivity={homeFocusActivity} onselect={selectActivity} />
  {:else if fishingSelected}
    <nav class="workspace-path" aria-label="Activity navigation">
      <button onclick={returnToActivities} disabled={!!runPending}><ArrowLeft size={14} strokeWidth={2} /> Activities</button>
      <ChevronRight size={12} aria-hidden="true" />
      <span><Waves size={13} strokeWidth={1.9} /> Fishing</span>
      <em>Ready</em>
    </nav>

  <section class="hero" aria-labelledby="state-heading">
    <div class="hero-copy">
      {#key hero.title}
        <div class="hero-state" in:fly={{ y: reduceMotion ? 0 : 6, duration: reduceMotion ? 0 : 220 }} out:fade={{ duration: reduceMotion ? 0 : 110 }}>
          <p class="eyebrow"><Activity size={14} strokeWidth={1.9} /> {hero.context}</p>
          <h1 id="state-heading" class="state-title">{hero.title}</h1>
          <p class="detail" aria-live="polite">{hero.detail}</p>
        </div>
      {/key}
      {#if engine.error}<p class="error" transition:fly={{ y: reduceMotion ? 0 : 4, duration: reduceMotion ? 0 : 160 }}><AlertTriangle size={15} strokeWidth={1.9} /> {engine.error}</p>{/if}
    </div>
    <div
      class:running={active}
      class:capturing={selectingTarget}
      class:searching={active && confidence === 0}
      class:lost={engine.status.state === "Faulted" || (active && confidence === 0 && currentStep >= 2)}
      class:weak={confidence > 0 && confidence < 45}
      class:good={confidence >= 45 && confidence < 80}
      class:strong={confidence >= 80}
      class="control-orbit"
      style={`--lock-progress: ${confidence * 3.6}deg`}
      aria-label={`Detector lock ${confidence}%, ${gaugeLabel}`}
    >
      <div class="orbit-progress"></div>
      <div class="orbit-ticks" aria-hidden="true"></div>
      <div class="orbit-ring"></div>
      <div class="orbit-ring orbit-ring--inner"></div>
      <div class="orbit-core"><strong>{confidence}%</strong><span>{gaugeLabel}</span></div>
    </div>
  </section>

  <section class="instrument" aria-label="Live engine telemetry">
    <article class="target-card">
      <header class="target-card__header">
        <div class="card-kicker"><Crosshair size={14} strokeWidth={1.9} /> Game target</div>
        <div class="target-card__actions">
          <button
            class:loading={verifyingSetup}
            class="target-button target-button--verify"
            onclick={verifySetup}
            disabled={active || verifyingSetup || selectingTarget || !!runPending || !engine.connected}
          >
            {#if verifyingSetup}<RefreshCw size={14} strokeWidth={1.9} class="spin" /> Checking…{:else}<ScanEye size={14} strokeWidth={1.9} /> Verify setup{/if}
          </button>
          <button
            class:loading={selectingTarget}
            class="target-button"
            bind:this={targetButton}
            aria-expanded={targetPickerOpen}
            aria-controls="target-picker"
            onclick={findTarget}
            disabled={active || selectingTarget || verifyingSetup || !!runPending || !engine.connected}
          >
            {#if selectingTarget}
              <RefreshCw size={15} strokeWidth={1.9} class="spin" /> Scanning…
            {:else}
              <Crosshair size={15} strokeWidth={1.9} /> Select FiveM target
            {/if}
          </button>
        </div>
      </header>
      {#if targetPickerOpen}
        <div
          id="target-picker"
          class="target-picker"
          bind:this={targetPickerElement}
          role="dialog"
          aria-label="Available FiveM windows"
          tabindex="-1"
          transition:fly={{ y: reduceMotion ? 0 : -5, duration: reduceMotion ? 0 : 170 }}
        >
          <header class="target-picker__header">
            <div>
              <span>FiveM windows</span>
              <strong>{targetCandidates.length === 0 ? "Nothing found" : `${targetCandidates.length} available`}</strong>
            </div>
            <button aria-label="Close target picker" onclick={() => closeTargetPicker()}><X size={14} strokeWidth={2} /></button>
          </header>

          {#if targetDiscoveryError}
            <div class="target-picker__empty target-picker__empty--error">
              <AlertTriangle size={17} strokeWidth={1.8} />
              <div><strong>Scan unavailable</strong><span>{targetDiscoveryError}</span></div>
            </div>
          {:else if targetCandidates.length === 0}
            <div class="target-picker__empty">
              <Monitor size={18} strokeWidth={1.7} />
              <div><strong>FiveM isn’t visible yet</strong><span>Start FiveM or restore its window, then scan again.</span></div>
            </div>
          {:else}
            <div class="target-options">
              {#each targetCandidates as candidate, index (candidate.processId)}
                <button
                  class:selected={candidate.isSelected}
                  class="target-option"
                  bind:this={targetOptionNodes[index]}
                  onclick={() => chooseTarget(candidate)}
                  disabled={selectingTarget}
                >
                  <span class="target-option__icon"><Monitor size={15} strokeWidth={1.8} /></span>
                  <span class="target-option__copy">
                    <strong>{candidate.windowTitle}</strong>
                    <small>{candidate.processName} · PID {candidate.processId}</small>
                  </span>
                  <span class="target-option__state">
                    {#if candidate.isSelected}<em><Check size={10} strokeWidth={2.3} /> Current</em>{/if}
                    {#if candidate.isForeground}<em>Foreground</em>{/if}
                    {#if candidate.isMinimized}<em>Minimized</em>{/if}
                    <ChevronRight size={14} strokeWidth={1.8} />
                  </span>
                </button>
              {/each}
            </div>
          {/if}

          <footer class="target-picker__footer">
            <span>Selection stays inside this app.</span>
            <button onclick={findTarget} disabled={selectingTarget}><RefreshCw size={13} strokeWidth={1.9} /> Scan again</button>
          </footer>
        </div>
      {/if}
      <div class="target-copy">
        <strong>{target?.processName || "No target selected"}</strong>
        <span class:invalid={!!target?.processName && !targetValid}>{targetValid ? (target?.windowTitle || "FiveM target ready.") : (engine.snapshot?.targetValidation || "Select FiveM before you start.")}</span>
      </div>
      {#if setupVerification}
        <div class:ready={setupVerification.ready} class="setup-check" aria-live="polite">
          <strong>{setupVerification.ready ? "Setup verified" : "Setup needs attention"}</strong>
          <span>{setupVerification.detail}</span>
          <small>Target {setupVerification.target.passed ? "ready" : "blocked"} · Input {setupVerification.input.passed ? "ready" : "blocked"} · Capture {setupVerification.capture.passed ? "ready" : "blocked"}</small>
        </div>
      {/if}
    </article>
    <aside class="telemetry" aria-label="Compact telemetry">
      <div class="metric">
        <span>Samples</span>
        <strong>{engine.status.sampleCount.toLocaleString()}</strong>
        <small>Detector frames</small>
      </div>
      <i aria-hidden="true"></i>
      <div class="metric">
        <span>Input mode</span>
        <strong>{engine.snapshot?.settings.routine.inputMode || "—"}</strong>
        <small>{engine.connected ? "Local delivery" : "Waiting for engine"}</small>
      </div>
    </aside>
  </section>

  <section class="cycle" aria-label="Routine cycle">
    {#each ["TARGET", "CAST", "METER", "TENSION", "COLLECT"] as step, index}
      {@const stepState = cycleStepState(index)}
      <div
        class:complete={stepState === "complete"}
        class:current={stepState === "active" || stepState === "ready" || stepState === "fault"}
        class:live={stepState === "active"}
        class:ready={stepState === "ready"}
        class:fault={stepState === "fault"}
        class="cycle-step"
        aria-current={stepState === "active" || stepState === "ready" || stepState === "fault" ? "step" : undefined}
        aria-label={`${step}: ${stepState}`}
      >
        <span class="step-index">{String(index + 1).padStart(2, "0")}</span>
        <span class="step-marker" aria-hidden="true">{#if stepState === "complete"}<Check size={8} strokeWidth={2.6} />{/if}</span>
        <span class="step-label">{step}</span>
      </div>
    {/each}
  </section>

  <section class="actions">
    <button class="sub-action" bind:this={settingsButton} onclick={openSettings} disabled={active || !!runPending || !engine.snapshot}><Settings2 size={16} strokeWidth={1.8} /> Settings</button>
    <button class="sub-action" bind:this={diagnosticsButton} onclick={inspectDiagnostics}><FolderOpen size={16} strokeWidth={1.8} /> Diagnostics</button>
  </section>

  <footer class="status-footer">
    <div class="safety-summary">
      <ShieldCheck size={15} strokeWidth={1.9} />
      <p><strong>Safe control</strong><i></i><kbd>{hotkeyDisplay(engine.snapshot?.settings.startStop)}</kbd> toggles start / stop from FiveM<i></i><kbd>Pause / Break</kbd> emergency stop</p>
    </div>
    <div class="system-status" aria-label="System status">
      <span><i></i>Local only</span>
      <b aria-hidden="true"></b>
      <span>FiveM foreground required</span>
    </div>
  </footer>
  {:else}
    <LockpickingWorkspace
      activity={lockpickingActivity}
      {targetValid}
      status={engine.snapshot?.lockpicking ?? stoppedLockpicking}
      onback={returnToActivities}
      onmode={async (mode) => { await engine.setLockpicking(mode); }}
      onsettings={openSettings}
    />
  {/if}
</main>

{#if showSettings && draft && shortcutDraft && lockpickingShortcutDraft}
  <div class="scrim" role="presentation" aria-hidden="true" onclick={closePanels} transition:fade={{ duration: reduceMotion ? 0 : 160 }}></div>
  <div class="drawer settings-drawer" bind:this={activePanel} aria-labelledby="settings-title" aria-describedby="settings-description" aria-modal="true" role="dialog" tabindex="-1" onkeydown={trapPanelFocus} transition:fly={{ x: reduceMotion ? 0 : 18, duration: reduceMotion ? 0 : 220 }}>
    <header class="panel-header">
      <div><p class="panel-kicker"><Settings2 size={12} strokeWidth={2} class="icon" /> {fishingSelected ? "Fishing profile" : "Lockpicking profile"}</p><h2 id="settings-title">{fishingSelected ? "Fishing controls" : "Lockpicking controls"}</h2></div>
      <button class="panel-close" bind:this={settingsCloseButton} aria-label="Close settings" onclick={closePanels}><X size={14} strokeWidth={2.2} class="icon" /></button>
    </header>
    <p class="panel-copy" id="settings-description">{fishingSelected ? "Choose your in-game toggle, then tune the tension window and timing cadence." : "Choose the in-game toggle used to start or safely stop the calibrated Class C controller."}</p>
    <p class:visible={settingsDirty} class="settings-change-note" aria-live="polite"><i></i>{settingsDirty ? "Unsaved changes" : "Profile is up to date"}</p>

    {#if fishingSelected}
      <section class="settings-group shortcut-setting" aria-labelledby="shortcut-heading">
        <header class="settings-group__header">
          <div><p>Global control</p><h3 id="shortcut-heading">Fishing Start / Stop shortcut</h3></div>
          <span>{hotkeyDisplay(shortcutDraft)}</span>
        </header>
        <div class="shortcut-control">
          <div><strong>Toggle Fishing from FiveM</strong><small>Press once to start. Press again to stop and release input.</small></div>
          <select aria-label="Fishing start and stop shortcut" bind:value={shortcutDraft.key}>
            {#each shortcutOptions as key}<option value={key}>{key}</option>{/each}
          </select>
        </div>
      </section>
    {:else}
      <section class="settings-group shortcut-setting" aria-labelledby="lockpicking-shortcut-heading">
        <header class="settings-group__header">
          <div><p>Global control</p><h3 id="lockpicking-shortcut-heading">Lockpicking Start / Stop shortcut</h3></div>
          <span>{hotkeyDisplay(lockpickingShortcutDraft)}</span>
        </header>
        <div class="shortcut-control">
          <div><strong>Toggle Class C from FiveM</strong><small>Press once to arm Class C. Press again to stop and release input.</small></div>
          <select aria-label="Lockpicking start and stop shortcut" bind:value={lockpickingShortcutDraft.key}>
            {#each shortcutOptions as key}<option value={key}>{key}</option>{/each}
          </select>
        </div>
      </section>
    {/if}

    {#if fishingSelected}
    <section class="settings-group" aria-labelledby="tension-heading">
      <header class="settings-group__header">
        <div><p>Control window</p><h3 id="tension-heading">Tension envelope</h3></div>
        <span>{draft.fishingLowerTensionPercent}–{draft.fishingUpperTensionPercent}%</span>
      </header>
      <div class="form-grid form-grid--tension">
        <label><span class="field-label">Pulse threshold</span><span class="field-control"><input aria-label="Pulse threshold percent" bind:value={draft.fishingLowerTensionPercent} min="25" max="80" type="number" /><small>%</small></span></label>
        <label><span class="field-label">Target tension</span><span class="field-control"><input aria-label="Target tension percent" bind:value={draft.fishingUpperTensionPercent} min="30" max="85" type="number" /><small>%</small></span></label>
      </div>
    </section>

    <section class="settings-group" aria-labelledby="timing-heading">
      <header class="settings-group__header">
        <div><p>Timing guardrails</p><h3 id="timing-heading">Control cadence</h3></div>
        <span>{draft.fishingSampleMilliseconds} ms sample</span>
      </header>
      <div class="form-grid">
        <label><span class="field-label">Sample interval</span><span class="field-control"><input aria-label="Sample interval milliseconds" bind:value={draft.fishingSampleMilliseconds} min="20" max="200" type="number" /><small>ms</small></span></label>
        <label><span class="field-label">Maximum pulse</span><span class="field-control"><input aria-label="Maximum pulse milliseconds" bind:value={draft.fishingMaximumPulseMilliseconds} min={draft.fishingMinimumPulseMilliseconds} max="120" type="number" /><small>ms</small></span></label>
        <label><span class="field-label">Minimum rest</span><span class="field-control"><input aria-label="Minimum rest milliseconds" bind:value={draft.fishingMinimumRestMilliseconds} min="20" max="250" type="number" /><small>ms</small></span></label>
        <label><span class="field-label">Safety time</span><span class="field-control"><input aria-label="Safety time seconds" bind:value={draft.maximumDurationSeconds} min="5" max="3600" type="number" /><small>s</small></span></label>
      </div>
    </section>

    <section class="settings-group settings-group--delivery" aria-label="Delivery behavior">
      <div class="delivery-select">
        <span class="select-caption" id="delivery-label">Input delivery</span>
        <button
          class="delivery-trigger"
          class:open={deliveryOpen}
          type="button"
          aria-haspopup="listbox"
          aria-expanded={deliveryOpen}
          aria-controls="delivery-menu"
          aria-labelledby="delivery-label delivery-value"
          bind:this={deliveryTrigger}
          onclick={toggleDelivery}
          onkeydown={openDeliveryFromKeyboard}
        >
          <span class="delivery-trigger__copy"><strong id="delivery-value">{selectedDelivery.label}</strong><small>{selectedDelivery.description}</small></span>
          <ChevronDown size={15} strokeWidth={2} class="delivery-chevron" />
        </button>
        {#if deliveryOpen}
          <div id="delivery-menu" class="delivery-menu" role="listbox" aria-label="Input delivery" transition:fly={{ y: reduceMotion ? 0 : -4, duration: reduceMotion ? 0 : 150 }}>
            {#each deliveryOptions as option, index}
              <button
                type="button"
                role="option"
                aria-selected={option.value === draft.inputMode}
                class:selected={option.value === draft.inputMode}
                bind:this={deliveryOptionNodes[index]}
                onclick={() => chooseDelivery(option.value)}
                onkeydown={(event) => handleDeliveryOptionKeydown(event, index)}
              >
                <span><strong>{option.label}</strong><small>{option.description}</small></span>
                {#if option.value === draft.inputMode}<Check size={15} strokeWidth={2.2} />{/if}
              </button>
            {/each}
          </div>
        {/if}
      </div>
      <label class="toggle-setting">
        <input type="checkbox" bind:checked={draft.collectOnTimeout} />
        <span class="toggle-track" aria-hidden="true"><i></i></span>
        <span class="toggle-copy"><strong>Collect at safety timeout</strong><small>Attempt collection when the bounded fishing timer expires.</small></span>
      </label>
    </section>
    {/if}
    {#if settingsError}<p class="error" transition:fly={{ y: reduceMotion ? 0 : 4, duration: reduceMotion ? 0 : 150 }}><AlertTriangle size={15} strokeWidth={1.9} /> {settingsError}</p>{/if}
    <div class="panel-actions"><button class="sub-action" onclick={closePanels}>Cancel</button><button class:dirty={settingsDirty} class="primary-action compact" onclick={saveSettings} disabled={savingSettings || !settingsDirty}>{#if savingSettings}<RefreshCw size={15} class="spin" /> Saving…{:else if settingsDirty}Apply changes <Check size={16} strokeWidth={2.1} />{:else}No changes to apply <Check size={16} strokeWidth={2.1} />{/if}</button></div>
  </div>
{/if}

{#if showDiagnostics}
  <div class="scrim" role="presentation" aria-hidden="true" onclick={closePanels} transition:fade={{ duration: reduceMotion ? 0 : 160 }}></div>
  <div class="drawer diagnostics" bind:this={activePanel} aria-labelledby="diagnostics-title" aria-describedby="diagnostics-description" aria-busy={diagnosticsLoading} aria-modal="true" role="dialog" tabindex="-1" onkeydown={trapPanelFocus} transition:fly={{ x: reduceMotion ? 0 : 18, duration: reduceMotion ? 0 : 220 }}>
    <header class="panel-header diagnostics-header"><div><p class="panel-kicker"><Gauge size={12} strokeWidth={2} class="icon" /> Local evidence</p><h2 id="diagnostics-title">Detection review</h2></div><button class="panel-close" bind:this={diagnosticsCloseButton} aria-label="Close diagnostics" onclick={closePanels}><X size={14} strokeWidth={2.2} class="icon" /></button></header>
    <p class="panel-copy" id="diagnostics-description">Every Start attempt records a bounded local session with detector decisions, rejection reasons, and decisive frames.</p>
    {#if diagnosticsLoading && !diagnostics}
      <div class="empty-state diagnostics-empty"><RefreshCw size={16} class="spin" /> Loading diagnostics…</div>
    {:else if diagnosticsError && !diagnostics}
      <p class="error diagnostics-error"><AlertTriangle size={14} /> {diagnosticsError}</p>
    {:else if diagnostics}
      <div class:refreshing={diagnosticsLoading} class="diagnostics-content">
        {#if diagnosticsError}<p class="error diagnostics-error"><AlertTriangle size={14} /> {diagnosticsError}</p>{/if}
        {#if debugSnapshot}
          <section class="debug-session" aria-labelledby="debug-session-title" data-debug-session={debugSnapshot.sessionId}>
            <header class="debug-session__header">
              <div>
                <p class="panel-kicker"><Radio size={11} class="icon" /> Instrumented run</p>
                <h3 id="debug-session-title">Debug session</h3>
              </div>
              <span class:live={debugSnapshot.active} class="debug-session__state">{debugSnapshot.active ? "Recording" : debugSnapshot.outcome}</span>
            </header>
            <div class="debug-session__summary">
              <span data-debug-stage={debugSnapshot.stage}><small>Stage</small><strong>{debugSnapshot.stage}</strong></span>
              <span data-debug-capture={debugSnapshot.captureHealth}><small>Capture</small><strong>{debugSnapshot.captureHealth}</strong></span>
              <span><small>Events</small><strong>{debugSnapshot.eventCount}</strong></span>
              <span><small>Frames</small><strong>{debugSnapshot.savedFrameCount}</strong></span>
            </div>
            <div class="debug-decisions">
              <article class:accepted={debugSnapshot.prompt.accepted} data-debug-prompt-reason={debugSnapshot.prompt.reason}>
                <header><span>CAST / COLLECT</span><strong>{evidencePercent(debugSnapshot.prompt.confidence)}</strong></header>
                <p>{debugSnapshot.prompt.reason}</p>
              </article>
              <article class:accepted={debugSnapshot.meter.accepted} data-debug-meter-reason={debugSnapshot.meter.reason}>
                <header><span>TENSION METER</span><strong>{evidencePercent(debugSnapshot.meter.confidence)}</strong></header>
                <p>{debugSnapshot.meter.reason}</p>
              </article>
            </div>
            <footer><code>{debugSnapshot.sessionId}</code><span>{debugSnapshot.lastEvent}</span></footer>
          </section>
        {/if}

        {#if diagnostics.debugSession?.frames?.length}
          <div class="diagnostic-section">
            <div class="diagnostic-section__head"><h3><ScanEye size={12} class="icon" /> Decisive session frames</h3></div>
            <div class="debug-frames">
              {#each diagnostics.debugSession.frames as frame}
                <figure>
                  {#if frame.imageData}
                    <img src={frame.imageData} alt={frame.label.replaceAll("-", " ")} />
                  {:else}
                    <div class="debug-frame-unavailable">Frame kept in the local diagnostics folder.</div>
                  {/if}
                  <figcaption><span>{frame.label.replaceAll("-", " ")}</span><strong>{evidencePercent(frame.score)}</strong></figcaption>
                </figure>
              {/each}
            </div>
          </div>
        {/if}

        <div class="diagnostic-section">
          <div class="diagnostic-section__head">
            <h3><ScanEye size={12} class="icon" /> Latest detection sample</h3>
          </div>
          {#if diagnostics.latestSample?.imageData}
            <figure class="sample-frame">
              <img src={diagnostics.latestSample.imageData} alt="Latest annotated meter detection capture" />
              {#if diagnostics.latestSample.metadata?.evidence}
                <div class="evidence-strip" aria-label="Detection evidence">
                  <span><small>Disk</small><strong>{evidencePercent(diagnostics.latestSample.metadata.evidence.darkDisk)}</strong></span>
                  <span><small>Contrast</small><strong>{evidencePercent(diagnostics.latestSample.metadata.evidence.diskContrast)}</strong></span>
                  <span><small>Ring</small><strong>{evidencePercent(diagnostics.latestSample.metadata.evidence.ringStrength)}</strong></span>
                  <span><small>LMB</small><strong>{evidencePercent(diagnostics.latestSample.metadata.evidence.lmbPrompt)}</strong></span>
                  <span><small>Tracked</small><strong>{diagnostics.latestSample.metadata.tracked ? "Yes" : "No"}</strong></span>
                </div>
              {/if}
              <figcaption>
                <span>{diagnostics.latestSample.name}</span>
                <small>{diagnostics.latestSample.metadata?.eventName === "meter-lock" ? "Confirmed detector lock" : "First confirmed detector loss"}</small>
              </figcaption>
            </figure>
          {:else}
            <div class="empty-state diagnostics-empty diagnostics-sample-empty">
              <ChevronRight size={14} class="icon" /> No saved detector sample yet.
            </div>
          {/if}
        </div>

        <section class="log-viewer" aria-labelledby="sample-log-title">
          <header class="log-viewer__header">
            <div><h3 id="sample-log-title"><Terminal size={13} strokeWidth={1.9} class="icon" /> Fishing activity</h3><p>Latest detector and input events</p></div>
            <p class="log-count">{diagnosticEntries.length} {diagnosticEntries.length === 1 ? "sample" : "samples"}</p>
          </header>
          {#if diagnosticEntries.length}
            <div class="log-viewer__content">
              {#each diagnosticEntries as entry, index}
                {#if entry.parsed}
                  <article class:active={entry.inputDown} class:success={entry.caught} class:failure={entry.failed} class:missing={!entry.visible} class="log-entry">
                    <header>
                      <span class="log-entry__time">{formatDiagnosticTime(entry.elapsedMilliseconds)}</span>
                      <strong>{diagnosticEventLabel(entry.eventName)}</strong>
                      <span class="log-entry__state">{diagnosticStateLabel(entry)}</span>
                    </header>
                    <div class="log-entry__metrics">
                      <span><small>Tension</small><strong>{formatDiagnosticMetric(entry.tensionPercent)}</strong></span>
                      <span><small>Progress</small><strong>{formatDiagnosticMetric(entry.progressPercent)}</strong></span>
                      <span><small>Confidence</small><strong>{formatDiagnosticMetric(entry.confidencePercent)}</strong></span>
                      {#if entry.pulseMilliseconds > 0}<span><small>Pulse</small><strong>{entry.pulseMilliseconds} ms</strong></span>{/if}
                    </div>
                  </article>
                {:else}
                  <article class="log-entry log-entry--raw">
                    <header><span class="log-entry__time">{String(index + 1).padStart(2, "0")}</span><strong>Unparsed detector event</strong></header>
                    <code>{entry.raw}</code>
                  </article>
                {/if}
              {/each}
            </div>
          {:else}
            <div class="empty-state diagnostics-empty">No detector samples have been recorded yet.</div>
          {/if}
        </section>
      </div>
    {:else}
      <div class="empty-state diagnostics-empty">Diagnostics are unavailable.</div>
    {/if}
    <div class="panel-actions panel-actions--compact">
      <button class="sub-action sub-action--mini" onclick={openDiagnosticsFolder}><FolderOpen size={15} class="icon" /> Open folder</button>
      <button class:loading={diagnosticsLoading} class="sub-action sub-action--mini" onclick={inspectDiagnostics} disabled={diagnosticsLoading}>{#if diagnosticsLoading}<RefreshCw size={15} class="icon spin" /> Refreshing…{:else}<RefreshCw size={15} class="icon" /> Refresh{/if}</button>
    </div>
  </div>
{/if}

{#if notice}
  <div class:info={notice.tone === "info"} class="status-toast" role="status" aria-live="polite" transition:fly={{ y: reduceMotion ? 0 : 8, duration: reduceMotion ? 0 : 180 }}>
    <span>{#if notice.tone === "success"}<Check size={14} strokeWidth={2.4} />{:else}<Radio size={14} strokeWidth={2} />{/if}</span>
    <p>{notice.message}</p>
  </div>
{/if}
