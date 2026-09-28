import { describe, expect, it } from "vitest";
import { activities, getActivity } from "./activities";

describe("activity registry", () => {
  it("keeps stable unique identifiers for every activity", () => {
    expect(new Set(activities.map((activity) => activity.id)).size).toBe(activities.length);
  });

  it("exposes fishing as ready", () => {
    expect(getActivity("fishing").availability).toBe("ready");
  });
});
