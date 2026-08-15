<script lang="ts">
  import { onMount, tick } from "svelte";
  import { ChevronRight, KeyRound, Layers3, ShieldCheck, Waves } from "@lucide/svelte";
  import { activities, type ActivityId } from "../activities";

  interface Props {
    engineConnected: boolean;
    targetValid: boolean;
    focusActivity: ActivityId | null;
    onselect: (activityId: ActivityId) => void | Promise<void>;
  }

  let { engineConnected, targetValid, focusActivity, onselect }: Props = $props();
  let activityCardNodes: Partial<Record<ActivityId, HTMLButtonElement>> = {};

  onMount(() => {
    if (focusActivity) void tick().then(() => activityCardNodes[focusActivity]?.focus());
  });
</script>

<section class="activity-intro" aria-labelledby="activity-heading">
  <div>
    <p class="eyebrow"><Layers3 size={14} strokeWidth={1.9} /> Activity library</p>
    <h1 id="activity-heading">What are you running?</h1>
    <p>Choose a focused reader. Each activity keeps its own detection flow while target selection and safety remain shared.</p>
  </div>
  <aside class="library-status" aria-label="Activity library status">
    <span><i></i>{engineConnected ? "Engine ready" : "Connecting engine"}</span>
    <small>{targetValid ? "FiveM target restored" : "Choose an activity to select a target"}</small>
  </aside>
</section>

<section class="activity-grid" aria-label="Available activities">
  {#each activities as activity, index (activity.id)}
    <button
      class:ready={activity.availability === "ready"}
      class:observe={activity.availability === "observe"}
      class="activity-card"
      data-activity={activity.id}
      bind:this={activityCardNodes[activity.id]}
      onclick={() => onselect(activity.id)}
      aria-describedby={`activity-description-${activity.id}`}
    >
      <span class="activity-card__topline">
        <span class="activity-card__number">0{index + 1}</span>
        <span class="activity-card__status"><i></i>{activity.statusLabel}</span>
      </span>
      <span class="activity-card__icon" aria-hidden="true">
        {#if activity.id === "fishing"}<Waves size={25} strokeWidth={1.65} />{:else}<KeyRound size={25} strokeWidth={1.65} />{/if}
      </span>
      <span class="activity-card__copy">
        <small>{activity.eyebrow}</small>
        <strong>{activity.name}</strong>
        <span id={`activity-description-${activity.id}`}>{activity.description}</span>
      </span>
      <span class="activity-card__capabilities" aria-label="Capabilities">
        {#each activity.capabilities as capability}<span>{capability}</span>{/each}
      </span>
      <span class="activity-card__action">Open activity<ChevronRight size={15} strokeWidth={1.9} /></span>
    </button>
  {/each}
</section>

<section class="activity-note" aria-label="Shared safety">
  <ShieldCheck size={16} strokeWidth={1.8} />
  <p><strong>One safe core.</strong> Activities share the selected FiveM window, local capture, emergency release, and bounded input delivery.</p>
</section>

<footer class="status-footer activity-home__footer">
  <div class="safety-summary"><ShieldCheck size={15} strokeWidth={1.9} /><p><strong>Local by design</strong><i></i>No gameplay imagery leaves this PC</p></div>
  <div class="system-status" aria-label="System status"><span><i></i>{activities.length} available activities</span><b aria-hidden="true"></b><span>{activities.length} configured</span></div>
</footer>
