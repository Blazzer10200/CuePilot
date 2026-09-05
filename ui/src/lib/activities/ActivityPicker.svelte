<script lang="ts">
  import { onMount, tick } from "svelte";
  import { ChevronRight, Hand, KeyRound, Layers3, ShieldCheck, Waves } from "@lucide/svelte";
  import { activities, type ActivityId } from "../activities";

  interface Props {
    engineConnected: boolean;
    targetValid: boolean;
    focusActivity: ActivityId | null;
    onselect: (activityId: ActivityId) => void | Promise<void>;
  }

  let { engineConnected, targetValid, focusActivity, onselect }: Props = $props();
  let activityCardNodes: Partial<Record<ActivityId, HTMLButtonElement>> = {};
  const readyCount = activities.filter((activity) => activity.availability === "ready").length;
  const observeCount = activities.filter((activity) => activity.availability === "observe" || activity.availability === "calibration").length;
  const previewCount = activities.filter((activity) => activity.availability === "preview").length;

  onMount(() => {
    if (focusActivity) void tick().then(() => activityCardNodes[focusActivity]?.focus());
  });
</script>

<section class="activity-intro" aria-labelledby="activity-heading">
  <div>
    <p class="eyebrow"><Layers3 size={14} strokeWidth={1.9} /> Activity library</p>
    <h1 id="activity-heading">Choose your activity</h1>
    <p>Open a workspace to set up, run, or review an activity.</p>
  </div>
  <aside class="library-status" class:offline={!engineConnected} aria-label="Activity library status" aria-live="polite">
    <span><i aria-hidden="true"></i>{engineConnected ? "Engine connected" : "Connecting to engine…"}</span>
    <small>{!engineConnected ? "Waiting for local connection" : targetValid ? "FiveM window selected" : "Select a FiveM window in a workspace"}</small>
  </aside>
</section>

<section class="activity-grid" aria-label="Available activities">
  {#each activities as activity, index (activity.id)}
    <button
      class:ready={activity.availability === "ready"}
      class:observe={activity.availability === "observe"}
      class:calibration={activity.availability === "calibration"}
      class:preview={activity.availability === "preview"}
      class="activity-card"
      data-activity={activity.id}
      bind:this={activityCardNodes[activity.id]}
      onclick={() => onselect(activity.id)}
      aria-label={`Open ${activity.shortName}`}
      aria-describedby={`activity-status-${activity.id} activity-description-${activity.id}`}
    >
      <span class="activity-card__topline">
        <span class="activity-card__number" aria-hidden="true">0{index + 1}</span>
        <span class="activity-card__status" id={`activity-status-${activity.id}`}><i aria-hidden="true"></i>{activity.statusLabel}</span>
      </span>
      <span class="activity-card__icon" aria-hidden="true">
        {#if activity.id === "fishing"}<Waves size={25} strokeWidth={1.65} />{:else if activity.id === "pickpocket"}<Hand size={25} strokeWidth={1.65} />{:else}<KeyRound size={25} strokeWidth={1.65} />{/if}
      </span>
      <span class="activity-card__copy">
        <small>{activity.eyebrow}</small>
        <strong>{activity.name}</strong>
        <span id={`activity-description-${activity.id}`}>{activity.description}</span>
      </span>
      <span class="activity-card__capabilities" aria-label="Capabilities">
        {#each activity.capabilities as capability}<span>{capability}</span>{/each}
      </span>
      <span class="activity-card__action">Open {activity.shortName}<ChevronRight size={15} strokeWidth={1.9} /></span>
    </button>
  {/each}
</section>

<section class="activity-note" aria-label="Shared safety">
  <ShieldCheck size={16} strokeWidth={1.8} />
  <p><strong>One safe core.</strong> Activities share the selected FiveM window, local capture, emergency release, and bounded input delivery.</p>
</section>

<footer class="status-footer activity-home__footer">
  <div class="safety-summary"><ShieldCheck size={15} strokeWidth={1.9} /><p><strong>Local by design</strong><i></i>No gameplay imagery leaves this PC</p></div>
  <div class="system-status" aria-label="Activity availability"><span><i></i>{readyCount} automation ready</span><b aria-hidden="true"></b><span>{observeCount} in calibration</span>{#if previewCount}<b aria-hidden="true"></b><span>{previewCount} preview</span>{/if}</div>
</footer>
