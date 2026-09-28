// ui/src/lib/motion.ts — the only place Svelte transition timings live.
// Usage:
//   <div in:enter={"drawer"} out:leave={"drawer"}>…</div>
//   <b class="rail__dot"><i class="live-ring" use:phaseLock></i></b>
import { fade, fly, type TransitionConfig } from "svelte/transition";
import { cubicIn, cubicOut } from "svelte/easing";

export const dur = {
  press: 80,
  fast: 120,
  base: 180,
  slow: 240,
  enter: 340,
  pulse: 2400,
} as const;

export type MotionKind = "menu" | "inline" | "swap" | "toast" | "drawer" | "scrim";

const spec: Record<MotionKind, { ms: number; x?: number; y?: number }> = {
  menu: { ms: dur.base, y: -4 },   // target picker, delivery menu
  inline: { ms: dur.base, y: 4 },  // inline error lines
  swap: { ms: dur.slow, y: 4 },    // hero state title/detail
  toast: { ms: dur.slow, y: 8 },   // status toast
  drawer: { ms: dur.slow, x: 16 }, // settings + diagnostics drawers
  scrim: { ms: dur.slow },         // scrim behind drawers (fade only)
};

const reduced = () =>
  typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;

/** Exits run at two-thirds of the enter duration and accelerate out. */
export const exitMs = (ms: number) => Math.round((ms * 2) / 3);

function make(node: Element, kind: MotionKind, exit: boolean): TransitionConfig {
  const s = spec[kind];
  const duration = reduced() ? 0 : exit ? exitMs(s.ms) : s.ms;
  const easing = exit ? cubicIn : cubicOut;
  if (s.x == null && s.y == null) return fade(node, { duration, easing });
  return fly(node, { x: s.x ?? 0, y: s.y ?? 0, duration, easing });
}

export const enter = (node: Element, kind: MotionKind) => make(node, kind, false);
export const leave = (node: Element, kind: MotionKind) => make(node, kind, true);

/** Desktop notice card: slides in from its screen edge and back out toward it.
 *  The exit (160 ms) must stay under EXIT_MS in notifications.rs, which hides the window. */
export function notice(node: Element, { edge, exit = false }: { edge: "left" | "right"; exit?: boolean }): TransitionConfig {
  const duration = reduced() ? 0 : exit ? exitMs(dur.slow) : dur.slow;
  return fly(node, { x: edge === "left" ? -14 : 14, duration, easing: exit ? cubicIn : cubicOut });
}

/** Aligns every .live-ring to one global clock, no matter when it mounts. */
export function phaseLock(node: HTMLElement, period: number = dur.pulse) {
  node.style.animationDelay = `-${Math.round(performance.now() % period)}ms`;
}

/** Tweens a number on the same duration/curve as the gauge ring (--dur-slow, cubicOut). */
export function tweenNumber(from: number, to: number, onFrame: (value: number) => void, ms: number = dur.slow) {
  if (reduced() || from === to) { onFrame(to); return () => {}; }
  const start = performance.now();
  let frame = 0;
  const step = (now: number) => {
    const t = Math.min(1, (now - start) / ms);
    onFrame(Math.round(from + (to - from) * cubicOut(t)));
    if (t < 1) frame = requestAnimationFrame(step);
  };
  frame = requestAnimationFrame(step);
  return () => cancelAnimationFrame(frame);
}
