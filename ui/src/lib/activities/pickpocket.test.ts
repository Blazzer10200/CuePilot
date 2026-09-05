import { describe, expect, it } from "vitest";
import { barPosition, knownItemCount, readTarget, referenceAt } from "./pickpocket";

describe("pickpocket reference workspace", () => {
  it("places the live blue grab in its own bar geometry", () => {
    expect(knownItemCount).toBe(10);
    expect(referenceAt(3547.6, "live")).toMatchObject({ state: "Grabbed", markerX: 792 });
    expect(referenceAt(2600, "live").direction).toBe("left");
    expect(barPosition(514, 514, 763)).toBe(0);
    expect(barPosition(1277, 514, 763)).toBe(100);
    expect(readTarget("Purple", "live")).toBe("White");
  });
  it("restores a known target and rejects stale storage values", () => {
    expect(readTarget("Red")).toBe("Red");
    expect(readTarget(null)).toBe("Purple");
    expect(readTarget("Ruby")).toBe("Purple");
  });
  it("separates preparation, motion and the recorded success", () => {
    expect(referenceAt(0).state).toBe("Preparing");
    expect(referenceAt(600).state).toBe("Active");
    expect(referenceAt(1083.3).state).toBe("Grabbed");
    expect(referenceAt(2000).markerX).toBe(896);
  });
  it("interpolates the recorded crossing inside the actual purple interval", () => {
    const marker = referenceAt(1070).markerX;
    expect(marker).toBeGreaterThanOrEqual(888);
    expect(marker).toBeLessThan(903);
    expect(barPosition(marker)).toBeGreaterThan(37.5);
  });
  it("clamps a scrub beyond the recording and handles invalid time", () => {
    expect(referenceAt(-10)).toEqual(referenceAt(0));
    expect(referenceAt(Number.NaN)).toEqual(referenceAt(0));
    expect(referenceAt(99999)).toEqual(referenceAt(2000));
  });
  it("keeps target choices within the selected clip", () => {
    expect(readTarget("Blue", "miss")).toBe("Blue");
    expect(readTarget("Red", "miss")).toBe("White");
    expect(readTarget("Blue", "grab")).toBe("Purple");
  });
  it("replays the second clip's reversal and missed result", () => {
    expect(referenceAt(0, "miss").state).toBe("Preparing");
    expect(referenceAt(1000, "miss").direction).toBe("right");
    expect(referenceAt(2400, "miss")).toMatchObject({ state: "Active", direction: "left" });
    expect(referenceAt(3083.333, "miss")).toMatchObject({ state: "Missed", markerX: 1016 });
    expect(referenceAt(99999, "miss")).toEqual(referenceAt(3900, "miss"));
  });
  it("keeps the third clip's successful result and moved white region separate", () => {
    expect(readTarget("Purple", "part")).toBe("White");
    expect(referenceAt(1900, "part")).toMatchObject({ state: "Active", markerX: 1136 });
    expect(referenceAt(2000, "part")).toMatchObject({ state: "Grabbed", markerX: 1160 });
    expect(referenceAt(99999, "part")).toEqual(referenceAt(2800, "part"));
  });
});
