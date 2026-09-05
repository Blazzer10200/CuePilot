import { describe, expect, it } from "vitest";
import { attemptReport, outcomeLabel, timingStep } from "./history";

describe("pickpocket history helpers", () => {
  it("moves later by reducing advance and clamps both directions", () => {
    expect(timingStep(17, "later")).toBe(16);
    expect(timingStep(0, "later")).toBe(0);
    expect(timingStep(20, "earlier")).toBe(20);
  });
  it("labels incomplete results unknown and keeps observed and planned facts separate", () => {
    const report = attemptReport({ id:"a", endedAtUnixMs:0, outcome:"Ended", color:"Yellow", widthPixels:4, offsetPixels:null, automaticPresses:1, itemName:"TNT Recipe", yellowAdvanceMs:17 });
    expect(outcomeLabel("Ended")).toBe("Unknown");
    expect(report).toContain("Observed outcome: Unknown");
    expect(report).toContain("Planned item: TNT Recipe");
    expect(report).toContain("not independently verified");
  });
});
