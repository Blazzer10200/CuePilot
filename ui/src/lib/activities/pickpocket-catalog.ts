import { pickpocketRecordings, type PickpocketTarget } from "./pickpocket";
import type { PickpocketColor } from "../engine.svelte";

export const defaultPriority: PickpocketColor[] = ["Yellow", "Red", "Purple", "Blue", "White"];
export function changePriority(order: PickpocketColor[], index: number, color: PickpocketColor | ""): PickpocketColor[] {
  if (!color) return order.length > 1 ? order.filter((_, i) => i !== index) : [...order];
  const next = [...order];
  const existing = next.indexOf(color);
  if (existing >= 0) {
    if (index >= next.length) { next.splice(existing, 1); next.push(color); }
    else [next[index], next[existing]] = [next[existing], next[index]];
  } else if (index >= next.length) next.push(color);
  else next[index] = color;
  return next;
}

// Each observed item appears once. The engine confirms recorded card labels before applying this order.
const recorded = Object.values(pickpocketRecordings).flatMap(clip => clip.targets.map(item => ({ name: item.name, color: item.id as PickpocketTarget })));
export const pickpocketItems = [...new Map([...recorded,
  { name: "Lucky Charm", color: "Yellow" as const },
  { name: "Broken Hard Drive", color: "PaleGreen" as const },
].map(item => [item.name, item])).values()].sort((a, b) => a.name.localeCompare(b.name));

export const itemGroups = (["Yellow", "Red", "Purple", "Blue", "White", "PaleGreen"] as const).map(color => ({
  color,
  label: color === "White" ? "White · common" : color === "PaleGreen" ? "Green · separate target" : color,
  items: pickpocketItems.filter(item => item.color === color),
})).filter(group => group.items.length > 0);
export const defaultItemPriority = itemGroups.flatMap(group => group.items.map(item => item.name));
export function changeItemPriority(order: string[], current: string, replacement: string): string[] {
  const a = pickpocketItems.find(item => item.name === current), b = pickpocketItems.find(item => item.name === replacement);
  if (!a || !b || a.color !== b.color) return [...order];
  const next = [...order], from = next.indexOf(current), to = next.indexOf(replacement);
  if (from >= 0 && to >= 0) [next[from], next[to]] = [next[to], next[from]];
  return next;
}

export const targetChoices = [
  { value: "RarestFirst", label: "Rarest → Common" },
  { value: "Custom", label: "My priority order" },
  { value: "PurpleBlueWhite", label: "Purple → Blue → Common" },
  { value: "Widest", label: "Widest available region" },
  { value: "Yellow", label: "Yellow · charm & recipes" },
  { value: "Red", label: "Red · Ruby" },
  { value: "Purple", label: "Purple · watches & rings" },
  { value: "Blue", label: "Blue · pocket watch & medicine" },
  { value: "White", label: "Common · white regions" },
  { value: "PaleGreen", label: "Green · electronic parts" },
] as const;

export function targetHint(policy: string, mode = "PrecisionAttempt", order: PickpocketColor[] = defaultPriority) {
  if (policy === "Custom") return order.map(color => color === "PaleGreen" ? "Green" : color).join(" → ") + ". Color order in Priority; same-color choices in Items.";
  if (policy === "RarestFirst") return "Yellow → Red → Purple → Blue → White. " + (mode === "Observe" ? "Best color is tracked; Space stays manual." : mode === "SingleAttempt" ? "Best color stays selected; wide mode skips tight windows." : "Best color stays selected; thin targets wait for the return.");
  if (policy === "PurpleBlueWhite") return "Purple first, then blue, then common. Wait for the return if the best target has passed.";
  if (policy === "Widest") return "Choose the widest upcoming region from the current randomized layout.";
  return "Target this color only. If it is absent, wait. Recognized cards follow your Items order; unreadable cards use the widest region.";
}
