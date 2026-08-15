import { describe, expect, it } from "vitest";
import { activities, getActivity } from "./activities";

describe("activity registry", () => {
  it("keeps stable unique identifiers for every activity", () => {
    expect(new Set(activities.map((activity) => activity.id)).size).toBe(activities.length);
  });

  it("exposes fishing as ready while Class C lockpicking remains in live calibration", () => {
    expect(getActivity("fishing").availability).toBe("ready");
    expect(getActivity("vehicle-lockpicking").availability).toBe("calibration");
  });

  it("defines the evidence needed to calibrate vehicle lockpicking", () => {
    const lockpicking = getActivity("vehicle-lockpicking");

    expect(lockpicking.preparation.map((item) => item.label)).toEqual([
      "Stage states",
      "Background range",
      "Input cadence",
    ]);
    expect(lockpicking.statusLabel).toBe("Observe only");
    expect(lockpicking.capabilities).toContain("Local calibration");
    expect(lockpicking.capabilities).toContain("Safe observation");
  });
});
