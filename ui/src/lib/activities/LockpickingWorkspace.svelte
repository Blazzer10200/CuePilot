<script lang="ts">
  import { ArrowLeft, CarFront, ChevronRight, CircleDot, Eye, Gauge, KeyRound, MousePointer2, OctagonX, ScanEye, Settings2, ShieldCheck } from "@lucide/svelte";
  import type { ActivityDefinition } from "../activities";
  import type { LockpickingObserveStatus } from "../engine.svelte";

  interface Props {
    activity: ActivityDefinition;
    targetValid: boolean;
    status: LockpickingObserveStatus;
    onback: () => void | Promise<void>;
    onmode: (mode: "observe" | "classC" | "stop") => void | Promise<void>;
    onsettings: () => void | Promise<void>;
  }

  let { activity, targetValid, status, onback, onmode, onsettings }: Props = $props();
  let pending = $state(false);
  const observation = $derived(status.observation);
  const hasHud = $derived(observation.state !== "Hidden");
  const percent = $derived(Math.round(status.confidence * 100));
  const spin = $derived(status.spin);
  const targetLabel = $derived(observation.target?.number ? `Target ${observation.target.number}` : observation.target ? "Target acquired" : "No active target");

  async function setMode(mode: "observe" | "classC" | "stop") {
    if (pending || (mode !== "stop" && !targetValid)) return;
    pending = true;
    try {
      await onmode(mode);
    } finally {
      pending = false;
    }
  }
</script>

<nav class="workspace-path" aria-label="Activity navigation">
  <button onclick={onback} disabled={status.observing}><ArrowLeft size={14} strokeWidth={2} /> Activities</button>
  <ChevronRight size={12} aria-hidden="true" />
  <span><KeyRound size={13} strokeWidth={1.9} /> Vehicle lockpicking</span>
  <em class:live={status.observing}>{status.inputEnabled ? "Class C armed" : status.observing ? "Observing live" : "Ready"}</em>
</nav>

<section class="lockpick-hero lockpick-hero--live" aria-labelledby="lockpick-heading">
  <div class="lockpick-hero__copy">
    <p class="eyebrow"><CarFront size={14} strokeWidth={1.9} /> Vehicle access reader</p>
    <h1 id="lockpick-heading">Vehicle lockpicking</h1>
    <p>Observe numbered targets and collect local calibration evidence. Automated Class C input is unavailable in this release.</p>
  </div>
  <div class="lockpick-hero__actions">
    {#if status.observing}
      <button class="lockpick-observe-button stop" onclick={() => setMode("stop")} disabled={pending}><OctagonX size={16} strokeWidth={1.9} /> Stop safely</button>
    {:else}
      <button class="lockpick-observe-button secondary" aria-label="Lockpicking settings" onclick={onsettings} disabled={pending}><Settings2 size={16} strokeWidth={1.9} /></button>
      <button class="lockpick-observe-button secondary" onclick={() => setMode("observe")} disabled={pending || !targetValid}><Eye size={16} strokeWidth={1.9} /> Observe only</button>
    {/if}
  </div>
</section>

<section class="lockpick-live-layout">
  <article class="lockpick-viewport" aria-label="Lockpicking visual debug view">
    <header>
      <div><p>Visual debug</p><h2>{hasHud ? "HUD acquired" : "Waiting for minigame"}</h2></div>
      <span class:ready={observation.target?.phase === "Ready"}>{percent}%</span>
    </header>
    <div class:acquired={hasHud} class="lockpick-screen">
      <span class="lockpick-screen__guide">FiveM frame</span>
      {#if hasHud}
        <i class="lockpick-hud-boundary" style={`left:${observation.hudCenterX * 100}%;top:${observation.hudCenterY * 100}%;width:${observation.hudRadius * 200}%;aspect-ratio:1`}></i>
      {/if}
      {#if observation.target}
        <i class:ready={observation.target.phase === "Ready"} class="lockpick-target-marker" style={`left:${observation.target.centerX * 100}%;top:${observation.target.centerY * 100}%;--approach-scale:${observation.target.approachRatio || 1}`}><MousePointer2 size={12} strokeWidth={2} /></i>
      {:else if spin?.cursorVisible}
        <i class="lockpick-target-marker" style={`left:${spin.cursorX * 100}%;top:${spin.cursorY * 100}%;--approach-scale:1`}><MousePointer2 size={12} strokeWidth={2} /></i>
      {/if}
      <div class="lockpick-screen__readout">
        <strong>{observation.state.toUpperCase()} · {observation.predictedAction}</strong>
        <span>{spin ? `Cursor ${Math.round(spin.angleDegrees)}° · ${spin.radiusRatio.toFixed(2)}× radius · ${Math.round(spin.angularVelocityDegreesPerSecond)}°/s` : `${targetLabel}${observation.target ? ` · ${Math.round(observation.target.confidence * 100)}% · glow ${Math.round(observation.target.fillDensity * 100)}%` : ""}`}</span>
      </div>
    </div>
  </article>

  <aside class="lockpick-readiness lockpick-readiness--live" aria-label="Lockpicking observe status">
    <p class="card-kicker"><Gauge size={14} strokeWidth={1.9} /> Live reader</p>
    <strong>{status.state}</strong>
    <span>{status.detail}</span>
    <div class="readiness-list">
      <p class:ready={targetValid}><CircleDot size={13} /><span>FiveM target</span><b>{targetValid ? "Ready" : "Select target"}</b></p>
      <p class:ready={hasHud}><ScanEye size={13} /><span>HUD boundary</span><b>{hasHud ? "Locked" : "Searching"}</b></p>
      <p class:ready={observation.target?.phase === "Ready"}><MousePointer2 size={13} /><span>Predicted action</span><b>{observation.predictedAction}</b></p>
      <p class:ready={status.inputEnabled}><ShieldCheck size={13} /><span>Input mode</span><b>{status.inputEnabled ? `Class ${status.vehicleClass}` : "Observe only"}</b></p>
    </div>
    <dl class="lockpick-telemetry">
      <div><dt>Samples</dt><dd>{status.sampleCount}</dd></div>
      <div><dt>Capture</dt><dd>{status.captureMilliseconds.toFixed(1)} ms</dd></div>
      <div><dt>Actions</dt><dd>{status.actionCount}</dd></div>
      <div><dt>Input</dt><dd>{status.spinInputActive ? "Rotating" : status.inputEnabled ? "Armed" : "Off"}</dd></div>
      {#if spin}
        <div><dt>Spin burst</dt><dd>{spin.capturedFrames}/30</dd></div>
        <div><dt>Cursor angle</dt><dd>{spin.cursorVisible ? `${Math.round(spin.angleDegrees)}°` : "—"}</dd></div>
        <div><dt>Radius</dt><dd>{spin.cursorVisible ? `${spin.radiusRatio.toFixed(2)}×` : "—"}</dd></div>
        <div><dt>CW travel</dt><dd>{Math.round(spin.clockwiseTravelDegrees)}°</dd></div>
      {:else}
        <div><dt>Frame batch</dt><dd>{status.accumulatedFrames}</dd></div>
        <div><dt>Targets</dt><dd>{observation.visibleTargetCount}</dd></div>
        <div><dt>Approach</dt><dd>{observation.target?.approachRatio ? `${observation.target.approachRatio.toFixed(2)}×` : "—"}</dd></div>
        <div><dt>Ready ETA</dt><dd>{observation.target?.timeToReadyMilliseconds != null ? `${Math.round(observation.target.timeToReadyMilliseconds)} ms` : "—"}</dd></div>
      {/if}
    </dl>
  </aside>
</section>

<section class="lockpick-next-step lockpick-next-step--observe">
  <div><ScanEye size={16} strokeWidth={1.8} /><p><strong>{status.inputEnabled ? "Class C safety envelope" : "Calibration capture"}</strong><span>{status.inputEnabled ? "Clicks require a verified READY target. SPIN requires two matching frames and is bounded to 2.8 seconds." : "Observe mode records the numbered sequence and high-rate SPIN telemetry without sending input."}</span></p></div>
  <span class="lockpick-next-step__count">{status.inputEnabled ? `${status.actionCount} actions` : "Input off"}</span>
</section>

<footer class="status-footer">
  <div class="safety-summary"><ShieldCheck size={15} strokeWidth={1.9} /><p><strong>Safe by default</strong><i></i>Observation never sends input<i></i><kbd>Pause / Break</kbd> emergency stop</p></div>
  <div class="system-status" aria-label="System status"><span><i></i>Local only</span><b aria-hidden="true"></b><span>{status.captureBackend}</span></div>
</footer>
