<script lang="ts">
  import { onMount } from "svelte";
  import { listen, type UnlistenFn } from "@tauri-apps/api/event";
  import { invoke } from "@tauri-apps/api/core";
  import { Fish, Hand } from "@lucide/svelte";
  import "./overlay.css";

  type Notice = { activity: string; title: string; detail: string; durationMs: number };
  let notice = $state<Notice | null>(null);
  let revision = $state(0);

  onMount(() => {
    let disposed = false;
    let unlisten: UnlistenFn | undefined;
    void listen<Notice>("notification-message", ({ payload }) => {
      if (disposed) return;
      notice = payload;
      revision += 1;
    }).then(async (stop) => {
      if (disposed) { stop(); return; }
      unlisten = stop;
      await invoke("notification_ready");
    }).catch(error => console.error("Notification listener failed", error));
    return () => { disposed = true; unlisten?.(); };
  });
</script>

<svelte:head><title>CuePilot Notification</title></svelte:head>

{#if notice}
  {#key revision}
    <section class="overlay-card" role="status" aria-live="polite" aria-atomic="true">
      <div class="overlay-card__icon">
        {#if notice.activity === "Fishing"}<Fish size={21} strokeWidth={1.8} />{:else}<Hand size={21} strokeWidth={1.8} />{/if}
      </div>
      <div class="overlay-card__copy">
        <span>CuePilot <b>·</b> {notice.activity}</span>
        <strong>{notice.title}</strong>
        <small>{notice.detail}</small>
      </div>
      <i class="overlay-card__edge" style={`--duration: ${notice.durationMs}ms`}></i>
    </section>
  {/key}
{/if}
