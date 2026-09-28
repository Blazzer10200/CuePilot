import { test, expect } from "@playwright/test";

test("notification preferences persist across settings panels and preview is available", async ({ page }) => {
  await page.setViewportSize({ width: 760, height: 620 });
  await page.goto("/?scenario=history");
  await page.locator('[data-activity="pickpocket"]').click();
  await page.getByRole("button", { name: "Settings", exact: true }).click();
  await expect(page.getByLabel("Popups", { exact: true })).toBeChecked();
  await expect(page.getByLabel("Popup position")).toHaveValue("top-right");
  await expect(page.getByLabel("Alert sound")).toBeChecked();
  await expect(page.getByLabel("Shortcut confirmations")).toBeChecked();
  await page.getByLabel("Popup position").selectOption("bottom-left");
  await page.getByLabel("Shortcut confirmations").uncheck();
  await page.getByLabel("Alert sound").uncheck();
  await expect(page.getByLabel("Alert sound")).toBeEnabled();
  await page.getByRole("button", { name: "Preview result", exact: true }).click();
  await expect(page.getByRole("status").filter({ hasText: "Preview sent" })).toBeVisible();
  await page.getByLabel("Popups", { exact: true }).uncheck();
  await expect(page.getByRole("button", { name: "Preview fishing", exact: true })).toBeDisabled();
  await expect(page.getByLabel("Popup position")).toBeDisabled();
  await expect(page.getByRole("button", { name: "Cancel", exact: true })).toBeInViewport();
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Settings", exact: true }).click();
  await expect(page.getByLabel("Popups", { exact: true })).not.toBeChecked();
  await expect(page.getByLabel("Popup position")).toHaveValue("bottom-left");
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
