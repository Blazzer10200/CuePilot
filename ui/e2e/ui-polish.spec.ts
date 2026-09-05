import { test, expect } from "@playwright/test";

for (const viewport of [{ width: 760, height: 620 }, { width: 820, height: 700 }, { width: 900, height: 700 }, { width: 1180, height: 760 }]) {
  test(`activity library remains readable at ${viewport.width}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await page.goto("/?scenario=history");
    for (const activity of ["Pickpocket", "Fishing", "Lockpicking"]) {
      await expect(page.getByRole("button", { name: `Open ${activity}`, exact: true })).toBeInViewport({ ratio: 1 });
    }
    await expect(page.locator("#activity-description-vehicle-lockpicking")).toBeVisible();
    expect(await page.evaluate(() => ({ width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight }))).toEqual(viewport);
    await page.screenshot({ path: `test-results/library-${viewport.width}.png` });
  });
}

test("diagnostics returns focus to its opener in every workspace", async ({ page }) => {
  await page.goto("/?scenario=history");
  for (const activity of [null, "Pickpocket", "Fishing", "Lockpicking"]) {
    if (activity) await page.getByRole("button", { name: `Open ${activity}`, exact: true }).click();
    const opener = page.getByRole("button", { name: "About and diagnostics", exact: true });
    await opener.focus();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("button", { name: "Close diagnostics", exact: true })).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(opener).toBeFocused();
    if (activity) await page.getByRole("button", { name: "Activities", exact: true }).click();
  }
});

test("disconnected library communicates connection state", async ({ page }) => {
  await page.goto("/?scenario=disconnected");
  const status = page.getByLabel("Activity library status");
  await expect(status).toHaveClass(/offline/);
  await expect(status).toContainText("Connecting to engine");
  await expect(status).toContainText("Waiting for local connection");
  await expect(status).not.toContainText("Engine connected");
});

test("save failure is visible on Run and remains visible across controls", async ({ page }) => {
  await page.goto("/?scenario=save-error");
  await page.getByRole("button", { name: "Open Pickpocket", exact: true }).click();
  await page.getByRole("combobox", { name: "Aim for", exact: true }).selectOption("Widest");
  await expect(page.getByRole("combobox", { name: "Aim for", exact: true })).toHaveValue("RarestFirst");
  for (const view of ["Run", "Timing", "Items", "Priority"]) {
    await page.getByRole("button", { name: view, exact: true }).click();
    await expect(page.getByRole("status").filter({ hasText: "Could not save. Previous values restored." })).toBeVisible();
  }
});

test("shared workspace tools retain context and keyboard focus", async ({ page }) => {
  await page.goto("/?scenario=history");
  for (const [name, diagnostic] of [["Pickpocket", "pickpocket"], ["Fishing", "fishing"], ["Lockpicking", "lockpicking"]]) {
    await page.getByRole("button", { name: `Open ${name}`, exact: true }).click();
    const tools = page.getByRole("group", { name: "Workspace tools" });
    const target = tools.getByRole("button", { name: "Change FiveM window" });
    await target.click();
    await expect(page.getByRole("dialog", { name: "Available FiveM windows" })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(target).toBeFocused();
    await tools.getByRole("button", { name: "Settings", exact: true }).click();
    await page.keyboard.press("Escape");
    await expect(tools.getByRole("button", { name: "Settings", exact: true })).toBeFocused();
    await tools.getByRole("button", { name: "Open diagnostics" }).click();
    await expect(page.getByRole("combobox", { name: "Diagnostic activity" })).toHaveValue(diagnostic);
    await page.keyboard.press("Escape");
    await expect(tools.getByRole("button", { name: "Open diagnostics" })).toBeFocused();
    await page.getByRole("button", { name: "Activities", exact: true }).click();
  }
});

for (const [scenario, label] of [["history", "Idle · input off"], ["running", "Observing · input off"], ["armed", "One tap armed"], ["tap-sent", "Tap sent · input off"], ["cooldown", "Cooldown · input paused"], ["disconnected", "Engine disconnected"]]) {
  test(`pickpocket clearly reports ${scenario}`, async ({ page }) => {
    await page.goto(`/?scenario=${scenario}`);
    await page.getByRole("button", { name: "Open Pickpocket", exact: true }).click();
    await expect(page.locator(".input-badge")).toHaveText(label);
    if (["running", "armed", "tap-sent"].includes(scenario)) {
      await expect(page.getByRole("group", { name: "Workspace tools" }).getByRole("button", { name: "Settings" })).toBeDisabled();
    }
  });
}

test("intermediate window size and reference view remain usable", async ({ page }) => {
  await page.setViewportSize({ width: 820, height: 700 });
  await page.goto("/?scenario=history");
  for (const name of ["Pickpocket", "Fishing", "Lockpicking"]) {
    await page.getByRole("button", { name: `Open ${name}`, exact: true }).click();
    expect(await page.evaluate(() => document.documentElement.scrollHeight), `${name} fits at 820×700`).toBe(700);
    await page.screenshot({ path: `test-results/${name}-820.png`, animations: "disabled" });
    await page.getByRole("button", { name: "Activities", exact: true }).click();
  }
  await page.getByRole("button", { name: "Open Pickpocket", exact: true }).click();
  await page.getByRole("button", { name: "Reference", exact: true }).click();
  await expect(page.getByRole("button", { name: "Back to live workspace" })).toBeInViewport();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(820);
});
