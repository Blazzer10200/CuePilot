import type { PickpocketObserveStatus } from "../engine.svelte";

export type PickpocketView = "live" | "history" | "reference";

export function cooldownSeconds(status: PickpocketObserveStatus | undefined, now: number) {
  return Math.max(0, Math.ceil(((status?.cooldownUntilUnixMs ?? 0) - now) / 1000));
}

/** The one session badge shared by the workspace header and the live controls. */
export function pickpocketSession(status: PickpocketObserveStatus | undefined, connected: boolean, now: number) {
  const observing = connected && status?.observing === true;
  if (!connected) return { label: "Engine disconnected", tone: "warning" } as const;
  if (cooldownSeconds(status, now) > 0) return { label: "Cooldown · input paused", tone: "warning" } as const;
  if (status?.inputArmed) return { label: "One tap armed", tone: "accent" } as const;
  if (observing && (status?.automatedPressCount ?? 0) > 0) return { label: "Tap sent · input off", tone: "accent" } as const;
  if (observing) return { label: "Observing · input off", tone: "accent" } as const;
  return { label: "Idle · input off", tone: "neutral" } as const;
}
