import type { PickpocketRecentAttempt } from "../engine.svelte";

export const outcomeLabel = (value: string) => value === "Grabbed" ? "Picked up" : value === "Missed" ? "Missed" : "Unknown";
export function attemptReport(attempt: PickpocketRecentAttempt) {
  return ["CuePilot · selected attempt", `Attempt: ${attempt.id}`, `Date: ${new Date(attempt.endedAtUnixMs).toISOString()}`,
    `Observed outcome: ${outcomeLabel(attempt.outcome)}`, `Planned item: ${attempt.itemName ?? "Unknown"}`,
    "Item identity describes the planned target; inventory acquisition was not independently verified.",
    `Color: ${attempt.color ?? "Unknown"}`, `Red / yellow advance: ${attempt.redAdvanceMs ?? "Unknown"} / ${attempt.yellowAdvanceMs ?? "Unknown"} ms`,
    `Mode / policy: ${attempt.inputMode ?? "Unknown"} / ${attempt.targetPolicy ?? "Unknown"}`,
    `Engine: ${attempt.engineVersion ?? "Unknown"}`, `Session: ${attempt.sessionId ?? "Unavailable"}`,
    `Automatic presses: ${attempt.automaticPresses}`, `Result offset: ${attempt.offsetPixels ?? "Unknown"} px (positive = past center; visual position, not input latency)`].join("\n");
}
export function timingStep(current: number, direction: "earlier" | "later") {
  return Math.max(0, Math.min(20, current + (direction === "earlier" ? 1 : -1)));
}
