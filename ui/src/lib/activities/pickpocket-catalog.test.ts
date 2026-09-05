import { describe, expect, it } from "vitest";
import { pickpocketItems, itemGroups, targetChoices, targetHint, changePriority, defaultPriority, defaultItemPriority, changeItemPriority } from "./pickpocket-catalog";

describe("observed pickpocket items", () => {
  it("ranks same-color items by swapping without duplicate or cross-color entries", () => {
    const swapped = changeItemPriority(defaultItemPriority, "Luxury Watch", "Ring");
    expect(swapped.indexOf("Ring")).toBeLessThan(swapped.indexOf("Luxury Watch"));
    expect(new Set(swapped).size).toBe(defaultItemPriority.length);
    expect(changeItemPriority(swapped, "Ring", "Ruby")).toEqual(swapped);
  });
  it("swaps ranked colors, supports omissions, and keeps at least one target", () => {
    expect(changePriority(defaultPriority, 0, "Purple")).toEqual(["Purple", "Red", "Yellow", "Blue", "White"]);
    expect(changePriority(defaultPriority, 1, "")).toEqual(["Yellow", "Purple", "Blue", "White"]);
    expect(changePriority(["Yellow"], 0, "")).toEqual(["Yellow"]);
    expect(changePriority(defaultPriority, 5, "PaleGreen")).toEqual([...defaultPriority, "PaleGreen"]);
    expect(changePriority(defaultPriority, 5, "Yellow")).toEqual(["Red", "Purple", "Blue", "White", "Yellow"]);
    expect(targetHint("Custom", "PrecisionAttempt", ["Purple", "Yellow"])).toContain("Purple → Yellow");
  });
  it("maps observed names to color policies without duplicates", () => {
    expect(pickpocketItems).toContainEqual({ name: "Lucky Charm", color: "Yellow" });
    expect(pickpocketItems).toContainEqual({ name: "Ruby", color: "Red" });
    expect(pickpocketItems).toContainEqual({ name: "Ring", color: "Purple" });
    expect(new Set(pickpocketItems.map(item => item.name)).size).toBe(pickpocketItems.length);
    for (const item of pickpocketItems) expect(targetChoices.some(choice => choice.value === item.color)).toBe(true);
  });
  it("explains color matching and the requested fallback order", () => {
    expect(targetChoices[0]).toEqual({ value: "RarestFirst", label: "Rarest → Common" });
    expect(targetHint("RarestFirst")).toContain("Yellow → Red → Purple → Blue → White");
    expect(targetHint("RarestFirst", "Observe")).toContain("Space stays manual");
    expect(targetHint("RarestFirst", "SingleAttempt")).toContain("wide mode skips tight windows");
    expect(targetHint("PurpleBlueWhite")).toContain("Purple first, then blue, then common");
    expect(targetHint("Yellow")).toContain("Recognized cards follow your Items order");
  });
  it("groups every observed item once in color order", () => {
    expect(itemGroups.map(group => group.color)).toEqual(["Yellow", "Red", "Purple", "Blue", "White", "PaleGreen"]);
    expect(itemGroups.flatMap(group => group.items).map(item => item.name).sort()).toEqual(pickpocketItems.map(item => item.name).sort());
  });
});
