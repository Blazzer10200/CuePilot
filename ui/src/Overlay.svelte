<script lang="ts">
  import { onMount } from "svelte";
  import { listen, type UnlistenFn } from "@tauri-apps/api/event";
  import { invoke } from "@tauri-apps/api/core";
  import { CircleAlert, CircleCheck, CirclePause, CircleX, Fish, Hand, Minimize2, OctagonX, Plug, Unplug } from "@lucide/svelte";
  import { notice as noticeMotion } from "./lib/motion";
  import "./overlay.css";

  type Notice = {
    activity: string; icon: keyof typeof icons; tone: "info" | "success" | "warning" | "critical";
    title: string; detail: string; durationMs: number; edge: "left" | "right";
  };
  const icons = { fish: Fish, hand: Hand, tray: Minimize2, stop: OctagonX, pause: CirclePause, check: CircleCheck, miss: CircleX, alert: CircleAlert, unplug: Unplug, plug: Plug };

  // One entry at a time; keying by id lets a replacement cross-fade with the
  // card it replaces, and each card keeps its own data while it leaves.
  let cards = $state<(Notice & { id: number })[]>([]);
  let nextId = 0;

  onMount(() => {
    let disposed = false;
    let unlisten: UnlistenFn[] = [];
    void Promise.all([
      listen<Notice>("notification-message", ({ payload }) => { cards = [{ ...payload, id: ++nextId }]; }),
      // Rust hides the window once the exit has played, so a hidden window
      // never holds a stale card that could flash on the next show.
      listen("notification-dismiss", () => { cards = []; }),
    ]).then(async (stops) => {
      if (disposed) { stops.forEach(stop => stop()); return; }
      unlisten = stops;
      await invoke("notification_ready");
    }).catch(error => console.error("Notification listener failed", error));
    return () => { disposed = true; unlisten.forEach(stop => stop()); };
  });
</script>

<svelte:head><title>CuePilot Notification</title></svelte:head>

{#each cards as card (card.id)}
  {@const Icon = icons[card.icon] ?? Hand}
  <section class="overlay-card {card.tone}" role="status" aria-live="polite" aria-atomic="true"
    in:noticeMotion={{ edge: card.edge }} out:noticeMotion={{ edge: card.edge, exit: true }}>
    <div class="overlay-card__icon"><Icon size={18} strokeWidth={2} /></div>
    <div class="overlay-card__copy">
      <span>CuePilot <b>·</b> {card.activity}</span>
      <strong>{card.title}</strong>
      <small>{card.detail}</small>
    </div>
    <i class="overlay-card__edge" style={`--duration: ${card.durationMs}ms`}></i>
  </section>
{/each}
