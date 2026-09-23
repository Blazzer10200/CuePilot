import { test, expect } from "@playwright/test";

test("shortcut field captures keys and mouse buttons and refuses taken or reserved keys", async ({ page }) => {
  await page.goto("/?scenario=history");
  await page.getByRole("button", { name: "Open Fishing", exact: true }).click();
  await page.getByRole("group", { name: "Workspace tools" }).getByRole("button", { name: "Settings", exact: true }).click();

  const field = page.getByRole("button", { name: "Fishing start and stop shortcut", exact: true });
  const heading = page.locator("section.shortcut-setting header span");
  await expect(field).toContainText("F10");
  await expect(heading).toHaveText("F10");

  await field.click();
  await expect(field).toHaveAttribute("aria-pressed", "true");
  await expect(field).toContainText("Press a key");
  await page.keyboard.press("Control+Shift+KeyG");
  await expect(field).toHaveAttribute("aria-pressed", "false");
  await expect(heading).toHaveText("Ctrl + Shift + G");
  await expect(page.getByText("Unsaved changes")).toBeVisible();

  await field.click();
  await page.keyboard.press("F7");
  await expect(page.getByText("F7 already belongs to Pickpocket Start / Stop.")).toBeVisible();
  await expect(field).toHaveAttribute("aria-pressed", "true");
  await page.keyboard.press("F8");
  await expect(page.getByText("F8 is reserved for the FiveM console.")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(field).toHaveAttribute("aria-pressed", "false");
  await expect(heading).toHaveText("Ctrl + Shift + G");
  await expect(page.getByRole("dialog", { name: "Fishing controls" })).toBeVisible();

  await field.click();
  const box = (await field.boundingBox())!;
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2, { button: "middle" });
  await expect(heading).toHaveText("Middle Mouse");

  await page.getByRole("button", { name: "Reset to F10", exact: true }).click();
  await expect(heading).toHaveText("F10");
  await expect(page.getByRole("button", { name: "Reset to F10", exact: true })).toHaveCount(0);

  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog", { name: "Fishing controls" })).toHaveCount(0);
});
