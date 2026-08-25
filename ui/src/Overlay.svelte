<script lang="ts">
  import { onMount } from "svelte";
  import { fly } from "svelte/transition";
  import { invoke } from "@tauri-apps/api/core";
  import { getCurrentWindow, PhysicalPosition, PhysicalSize, primaryMonitor, type Monitor } from "@tauri-apps/api/window";
  import { AlertTriangle, Check, Radio, ShieldAlert } from "@lucide/svelte";
  import "./overlay.css";

  type Tone = "routine" | "success" | "warning" | "critical";
  type Notice = { context: string; message: string; detail?: string; tone: Tone; duration: number };
  type StatusPayload = { state?: string; detail?: string };
  type OverlayPoll = { sequence: number; notification: unknown | null };

  const windowHandle = getCurrentWindow();
  let notice = $state<Notice | null>(null);
  let noticeTimer: ReturnType<typeof setTimeout> | null = null;
  let lastState = $state<string | null>(null);
  let reduceMotion = $state(false);
  let lastSequence = 0;

  const stateNotices: Record<string, Omit<Notice, "duration">> = {
    Casting: { context: "CAST", message: "Casting the line", tone: "routine" },
    Armed: { context: "METER WATCH", message: "Watching for the meter", tone: "routine" },
    Regulating: { context: "TENSION CONTROL", message: "Managing tension", tone: "routine" },
    Collecting: { context: "COLLECT", message: "Securing the catch", tone: "success" },
    Stowing: { context: "NEXT CAST", message: "Preparing the next cast", tone: "success" },
  };

  function iconFor(tone: Tone) {
    if (tone === "critical") return ShieldAlert;
    if (tone === "warning") return AlertTriangle;
    if (tone === "success") return Check;
    return Radio;
  }

  async function placeOnPrimaryMonitor() {
    const monitor: Monitor | null = await primaryMonitor();
    if (!monitor) return;

    const width = 430;
    const height = 112;
    const margin = 42;
    const area = monitor.workArea;
    await windowHandle.setSize(new PhysicalSize(width, height));
    await windowHandle.setPosition(new PhysicalPosition(
      area.position.x + area.size.width - width - margin,
      area.position.y + Math.round((area.size.height - height) * 0.52),
    ));
  }

  async function showNotice(next: Notice) {
    if (noticeTimer) clearTimeout(noticeTimer);
    notice = next;
    await windowHandle.show();
    await placeOnPrimaryMonitor();
    // Windows can reinsert a hidden transparent window into the normal z-order
    // when it is shown. Reassert topmost after the show/reposition transaction,
    // while keeping the window non-focusable and click-through.
    await windowHandle.setAlwaysOnTop(true);
    await windowHandle.setIgnoreCursorEvents(true);
    noticeTimer = setTimeout(() => {
      notice = null;
      noticeTimer = null;
      void windowHandle.hide();
    }, next.duration);
  }

  function consume(payload: unknown) {
    if (!payload || typeof payload !== "object") return;
    const message = payload as { name?: string; payload?: unknown };
    if (message.name === "status") {
      const status = (message.payload ?? {}) as StatusPayload;
      if (!status.state || status.state === lastState) return;
      lastState = status.state;
      const mapped = stateNotices[status.state];
      if (mapped) void showNotice({ ...mapped, duration: mapped.tone === "success" ? 2600 : 1900 });
      return;
    }
    if (message.name === "fault") {
      const detail = String((message.payload as { detail?: string })?.detail ?? "Automation paused");
      void showNotice({ context: "CONTROL SAFEGUARD", message: "Automation paused", detail, tone: "critical", duration: 6000 });
      return;
    }
    if (message.name === "shortcut") {
      const shortcut = (message.payload as { key?: string })?.key ?? "shortcut";
      const isEmergency = shortcut === "Pause / Break";
      void showNotice({
        context: isEmergency ? "EMERGENCY RELEASE" : "CONTROL INPUT",
        message: isEmergency ? "Stop requested" : `${shortcut} received`,
        detail: isEmergency ? "Input release is being requested." : "CuePilot is processing the command.",
        tone: isEmergency ? "critical" : "routine",
        duration: isEmergency ? 4200 : 1500,
      });
      return;
    }
    if (message.name === "bridge_state") {
      const state = message.payload as { connected?: boolean; detail?: string };
      if (state.connected === false) {
        void showNotice({ context: "LOCAL ENGINE", message: "Connection interrupted", detail: state.detail, tone: "warning", duration: 4500 });
      }
    }
  }

  onMount(() => {
    const motionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
    const syncMotionPreference = () => reduceMotion = motionQuery.matches;
    syncMotionPreference();
    motionQuery.addEventListener("change", syncMotionPreference);
    void windowHandle.setIgnoreCursorEvents(true);
    void placeOnPrimaryMonitor();
    void windowHandle.setAlwaysOnTop(true);
    const pollOverlay = async () => {
      try {
        const result = await invoke<OverlayPoll>("overlay_poll");
        if (result.sequence > lastSequence && result.notification) {
          lastSequence = result.sequence;
          consume(result.notification);
        }
      } catch (error) {
        console.error("Overlay notification polling failed", error);
      }
    };
    void pollOverlay();
    const pollTimer = setInterval(() => void pollOverlay(), 100);
    return () => {
      motionQuery.removeEventListener("change", syncMotionPreference);
      if (noticeTimer) clearTimeout(noticeTimer);
      clearInterval(pollTimer);
    };
  });
</script>

<svelte:head><title>CuePilot Overlay</title></svelte:head>

{#if notice}
  {@const Icon = iconFor(notice.tone)}
  <section class:warning={notice.tone === "warning"} class:critical={notice.tone === "critical"} class:success={notice.tone === "success"} class="overlay-card" role="status" aria-live="polite" transition:fly={{ x: 20, duration: reduceMotion ? 0 : 220 }}>
    <div class="overlay-card__icon"><Icon size={17} strokeWidth={2.1} /></div>
    <div class="overlay-card__copy">
      <span>{notice.context}</span>
      <strong>{notice.message}</strong>
      {#if notice.detail}<small>{notice.detail}</small>{/if}
    </div>
    <i class="overlay-card__edge"></i>
  </section>
{/if}
