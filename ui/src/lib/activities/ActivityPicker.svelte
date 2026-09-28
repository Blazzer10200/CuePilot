<script lang="ts">
  import { onMount, tick } from "svelte";
  import { ArrowRight, Hand, ShieldCheck, Waves } from "@lucide/svelte";
  import { activities, type ActivityDefinition, type ActivityId } from "../activities";

  interface Props {
    engineConnected: boolean;
    targetValid: boolean;
    focusActivity: ActivityId | null;
    shortcuts: Record<ActivityId, string>;
    onselect: (activityId: ActivityId) => void | Promise<void>;
  }

  let { engineConnected, targetValid, focusActivity, shortcuts, onselect }: Props = $props();
  let activityCardNodes: Partial<Record<ActivityId, HTMLButtonElement>> = $state({});
  const readyCount = activities.filter((activity) => activity.availability === "ready").length;
  const calibrationCount = activities.filter((activity) => activity.availability === "observe" || activity.availability === "calibration").length;
  const previewCount = activities.filter((activity) => activity.availability === "preview").length;
  const availabilitySummary = [
    [readyCount, "ready"],
    [calibrationCount, "in calibration"],
    [previewCount, "preview"],
  ].filter(([count]) => count).map(([count, label]) => `${count} ${label}`).join(" · ");
  const statusTone: Record<ActivityDefinition["availability"], string> = { ready: "success", observe: "warning", calibration: "warning", preview: "accent" };

  onMount(() => {
    if (focusActivity) void tick().then(() => activityCardNodes[focusActivity]?.focus());
  });
</script>

<section class="activity-intro" aria-labelledby="activity-heading">
  <div>
    <h1 id="activity-heading">Activities</h1>
    <p>Open a workspace to set up, run, or review. Target, capture, and emergency stop are shared across all of them.</p>
  </div>
  <aside class="library-status" class:offline={!engineConnected} aria-label="Activity library status" aria-live="polite">
    <div>
      <span>Engine</span>
      <strong><i class={engineConnected ? "success" : "warning"} aria-hidden="true"></i>{engineConnected ? "Connected" : "Connecting to engine…"}</strong>
    </div>
    <div>
      <span>FiveM window</span>
      <strong><i class={engineConnected && targetValid ? "success" : "warning"} aria-hidden="true"></i>{!engineConnected ? "Waiting for local connection" : targetValid ? "Selected" : "Not selected"}</strong>
    </div>
    <div>
      <span>Emergency stop</span>
      <kbd>Pause / Break</kbd>
    </div>
  </aside>
</section>

<section class="activity-grid" aria-label="Available activities">
  {#each activities as activity, index (activity.id)}
    <button
      class:ready={activity.availability === "ready"}
      class="activity-card"
      data-activity={activity.id}
      style={`--i:${index}`}
      bind:this={activityCardNodes[activity.id]}
      onclick={() => onselect(activity.id)}
      aria-label={`Open ${activity.shortName}`}
      aria-describedby={`activity-status-${activity.id} activity-description-${activity.id}`}
    >
      <span class="activity-card__topline">
        <span class="activity-card__icon" aria-hidden="true">
          {#if activity.id === "fishing"}<Waves size={22} strokeWidth={1.7} />{:else}<Hand size={22} strokeWidth={1.7} />{/if}
        </span>
        <span class={`status-pill ${statusTone[activity.availability]}`} id={`activity-status-${activity.id}`}><i aria-hidden="true"></i>{activity.statusLabel}</span>
      </span>
      <strong class="activity-card__name">{activity.name}</strong>
      <span class="activity-card__description" id={`activity-description-${activity.id}`}>{activity.description}</span>
      <span class="activity-card__capabilities" aria-label="Capabilities">
        {#each activity.capabilities as capability}<span>{capability}</span>{/each}
      </span>
      <span class="activity-card__footer">
        <span class="activity-card__shortcut">Shortcut <kbd>{shortcuts[activity.id]}</kbd></span>
        <span class="activity-card__action">Open<ArrowRight size={15} strokeWidth={1.9} /></span>
      </span>
    </button>
  {/each}
</section>

<footer class="home-footer">
  <p><ShieldCheck size={15} strokeWidth={1.9} /><strong>Local by design.</strong> No gameplay imagery leaves this PC.</p>
  <span aria-label="Activity availability">{availabilitySummary}</span>
</footer>
