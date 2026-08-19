export type ActivityId = "fishing" | "vehicle-lockpicking";
export type ActivityAvailability = "ready" | "observe" | "calibration";

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
    id: "vehicle-lockpicking",
    name: "Vehicle lockpicking",
    shortName: "Lockpicking",
    eyebrow: "Observe-only calibration",
    description: "A dedicated visual reader for the vehicle lockpicking minigame. Automated input remains unavailable while label calibration is verified.",
    availability: "calibration",
    statusLabel: "Observe only",
    capabilities: ["HUD tracking", "Local calibration", "Safe observation"],
    preparation: [
      {
        label: "Stage states",
        detail: "Capture every prompt, lock position, success state, and failure state the minigame can display.",
      },
      {
        label: "Background range",
        detail: "Include bright, dark, moving, and obstructed game scenes so recognition is not tied to one backdrop.",
      },
      {
        label: "Input cadence",
        detail: "Record which keys or buttons are required and the safe timing window for each interaction.",
      },
    ],
  },
];

export function getActivity(id: ActivityId): ActivityDefinition {
  const activity = activities.find((candidate) => candidate.id === id);
  if (!activity) throw new Error(`Unknown activity: ${id}`);
  return activity;
}
