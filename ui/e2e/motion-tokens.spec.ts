import { test, expect } from "@playwright/test";

// Fails if any rendered element uses a transition/animation duration outside the motion token set.
const allowedMs = new Set([0, 80, 120, 160, 180, 240, 340, 1000, 2400, 8000]);
// 160 = exit of 240 (⅔ rule). 120 also covers exit of 180.
// Linear frame-following transitions on tracked markers are exempt (see EXEMPT below).
const EXEMPT = [".pp-marker", ".marker"];

const toMs = (value: string) =>
  value.split(",").map((v) => v.trim()).filter(Boolean).map((v) => (v.endsWith("ms") ? parseFloat(v) : parseFloat(v) * 1000));

for (const scenario of ["history", "running", "armed", "cooldown", "disconnected"]) {
  test.fixme(`motion tokens only · ${scenario}`, async ({ page }) => {
    await page.goto(`/?scenario=${scenario}`);
    const screens: Array<string | null> = [null, "Pickpocket", "Fishing"];
    for (const activity of screens) {
      if (activity) await page.getByRole("button", { name: `Open ${activity}`, exact: true }).click();
      const offenders = await page.evaluate(({ exempt }) => {
        const out: string[] = [];
        for (const el of Array.from(document.querySelectorAll<HTMLElement>("*"))) {
          if (exempt.some((sel) => el.matches(sel))) continue;
          for (const pseudo of [null, "::before", "::after"] as const) {
            const cs = getComputedStyle(el, pseudo);
            for (const prop of ["transitionDuration", "animationDuration"] as const) {
              const raw = cs[prop];
              if (raw && raw !== "0s") out.push(`${el.className || el.tagName}${pseudo ?? ""} ${prop}=${raw}`);
            }
          }
        }
        return out;
      }, { exempt: EXEMPT });
      const bad = offenders.filter((line) => toMs(line.split("=")[1]).some((ms) => !allowedMs.has(Math.round(ms))));
      expect(bad, `off-token durations on ${activity ?? "Home"}`).toEqual([]);
      if (activity) await page.getByRole("button", { name: "Activity library", exact: true }).click();
    }
  });
}
