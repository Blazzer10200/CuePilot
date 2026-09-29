import { test, expect } from "@playwright/test";

// A real keyboard change fires native input/change events, with a microtask checkpoint between the bind listener and the delegated onchange handler.
test("Live selects save the value the user picked", async ({ page }) => {
  await page.goto("/?scenario=history");
  await page.locator('[data-activity="pickpocket"]').click();
  const saved = page.getByRole("status").filter({ hasText: "Saved." });
  for (const [label, key] of [["Aim for", "ArrowDown"], ["Run mode", "ArrowUp"]]) {
    const select = page.getByLabel(label);
    const before = await select.inputValue();
    await select.focus();
    await page.keyboard.press(key);
    await expect(saved).toBeVisible();
    const chosen = await select.inputValue();
    expect(chosen, `${label} must keep the picked value`).not.toBe(before);
    await page.getByRole("button", { name: "Timing", exact: true }).click();
    await page.getByRole("button", { name: "Run", exact: true }).click();
    await expect(page.getByLabel(label), `${label} must still show what was saved`).toHaveValue(chosen);
  }
  await page.getByRole("button", { name: "Timing", exact: true }).click();
  const yellow = page.getByLabel("Yellow · earlier by");
  await expect(yellow).toHaveValue("17");
  await yellow.focus();
  await page.keyboard.press("ArrowDown");
  await expect(yellow).toHaveValue("18");
  await page.getByRole("button", { name: "Run", exact: true }).click();
  await page.getByRole("button", { name: "Timing", exact: true }).click();
  await expect(page.getByLabel("Yellow · earlier by")).toHaveValue("18");
});
