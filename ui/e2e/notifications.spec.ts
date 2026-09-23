import { test, expect } from "@playwright/test";

test("notification preferences persist across settings panels and preview is available", async ({ page }) => {
  await page.setViewportSize({ width: 760, height: 620 });
  await page.goto("/?scenario=history");
  await page.locator('[data-activity="pickpocket"]').click();
  await page.getByRole("button", { name: "Settings", exact: true }).click();
  await expect(page.getByLabel("Top-right popups")).toBeChecked();
  await expect(page.getByLabel("Alert sound")).toBeChecked();
  await expect(page.getByLabel("Shortcut confirmations")).toBeChecked();
  await page.getByLabel("Shortcut confirmations").uncheck();
  await page.getByLabel("Alert sound").uncheck();
  await expect(page.getByLabel("Alert sound")).toBeEnabled();
  await page.getByRole("button", { name: "Preview pickpocket", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "Preview sent" })).toBeVisible();
  await page.getByLabel("Top-right popups").uncheck();
  await expect(page.getByRole("button", { name: "Preview fishing", exact: true })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Cancel", exact: true })).toBeInViewport();
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Settings", exact: true }).click();
  await expect(page.getByLabel("Top-right popups")).not.toBeChecked();
  await expect(page.getByLabel("Alert sound")).not.toBeChecked();
  await expect(page.getByLabel("Shortcut confirmations")).not.toBeChecked();
});

test("notification save failures retain the saved preference", async ({ page }) => {
  await page.goto("/?scenario=save-error");
  await page.locator('[data-activity="fishing"]').click();
  await page.getByRole("button", { name: "Settings", exact: true }).click();
  await page.getByLabel("Alert sound").click();
  await expect(page.getByRole("alert")).toContainText("notification save failed");
  await expect(page.getByLabel("Alert sound")).toBeChecked();
});
