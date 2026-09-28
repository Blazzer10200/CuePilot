export type ActivityId = "fishing" | "pickpocket";
export type ActivityAvailability = "ready" | "observe" | "calibration" | "preview";

export interface ActivityPreparationItem {
  label: string;
  detail: string;
}

export interface ActivityDefinition {
  id: ActivityId;
  name: string;
  shortName: string;
  eyebrow: string;
  description: string;
  availability: ActivityAvailability;
  statusLabel: string;
  capabilities: readonly string[];
  preparation: readonly ActivityPreparationItem[];
}

export const activities: readonly ActivityDefinition[] = [
  {
    id: "fishing",
    name: "Fishing",
    shortName: "Fishing",
    eyebrow: "Live activity",
    description: "Read cast and collection prompts, regulate the tension meter, and retain local evidence for review.",
    availability: "ready",
    statusLabel: "Ready",
    capabilities: ["Prompt detection", "Meter control", "Local evidence"],
    preparation: [],
  },
  {
    id: "pickpocket",
    name: "Pickpocket",
    shortName: "Pickpocket",
    eyebrow: "Precision timing",
    description: "Track randomized regions and test one timed Space tap with local diagnostics.",
    availability: "calibration",
    statusLabel: "One-tap test",
    capabilities: ["Live timing", "3-minute cooldown", "One-tap calibration"],
    preparation: [],
  },
];

export function getActivity(id: ActivityId): ActivityDefinition {
  const activity = activities.find((candidate) => candidate.id === id);
  if (!activity) throw new Error(`Unknown activity: ${id}`);
  return activity;
}
