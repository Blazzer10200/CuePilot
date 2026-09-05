import { test, expect } from "@playwright/test";

for (const viewport of [{width:760,height:620},{width:1180,height:760}]) {
  test(`history and timing at ${viewport.width}`, async ({page}) => {
    await page.setViewportSize(viewport);
    await page.goto("/?scenario=history");
    expect(await page.evaluate(() => document.documentElement.scrollHeight), "home must fit without document scrolling").toBe(viewport.height);
    await page.locator('[data-activity="pickpocket"]').click();
    await page.getByRole("button", {name:"History",exact:true}).click();
    await expect(page.getByRole("heading", {name:"Attempt history"})).toBeVisible();
    if (viewport.height === 620) expect(await page.evaluate(() => document.documentElement.scrollHeight), "history must fit at minimum size").toBe(viewport.height);
    await expect(page.getByRole("navigation", {name:"History pages"})).toContainText("1 / 3");
    await page.getByRole("button", {name:"Next",exact:true}).click();
    await expect(page.getByRole("navigation", {name:"History pages"})).toContainText("2 / 3");
    await page.getByRole("combobox", {name:"Outcome"}).selectOption("Missed");
    await expect(page.getByRole("navigation", {name:"History pages"})).toContainText("1 / 1");
    await page.getByRole("button", {name:"Live",exact:true}).click();
    await expect.poll(() => page.evaluate(() => scrollY)).toBe(0);
    await page.getByRole("button", {name:"Timing",exact:true}).click();
    expect(await page.evaluate(() => document.documentElement.scrollHeight), "timing view must fit without document scrolling").toBe(viewport.height);
    await expect(page.getByLabel("Yellow · earlier by")).toHaveValue("17");
    await page.getByRole("button", {name:"yellow press later"}).click();
    await expect(page.getByRole("status").filter({hasText:"Yellow:"})).toContainText("Saved");
    await expect(page.getByLabel("Yellow · earlier by")).toHaveValue("16");
    await expect(page.getByText("Yellow: 17 → 16 ms. Press later by 1 ms. Saved.")).toBeVisible();
    await expect(page.getByText("current trial",{exact:false})).toHaveCount(0);
    await page.screenshot({path:`test-results/pickpocket-${viewport.width}.png`});
  });
  test(`pages and settings fit at ${viewport.width}`, async ({page}) => {
    await page.setViewportSize(viewport);
    await page.goto("/?scenario=history");
    for (const activity of ["fishing","vehicle-lockpicking","pickpocket"]) {
      await page.locator(`[data-activity="${activity}"]`).click();
      await expect(page.locator("main")).toBeVisible();
      expect(await page.evaluate(() => ({ height:document.documentElement.scrollHeight, viewport:innerHeight })), `${activity} must fit without document scrolling`).toEqual({height:viewport.height,viewport:viewport.height});
      await page.screenshot({path:`test-results/${activity}-${viewport.width}.png`});
      await page.getByRole("button",{name:"Activities",exact:true}).click();
    }
    await page.locator('[data-activity="fishing"]').click();
    await page.getByRole("button",{name:"Settings",exact:true}).click();
    await expect(page.getByRole("button",{name:"Cancel",exact:true})).toBeInViewport();
    await page.getByRole("button",{name:"Advanced",exact:true}).click();
    await expect(page.getByRole("button",{name:"Cancel",exact:true})).toBeInViewport();
    await page.keyboard.press("Escape");
    await expect(page.getByRole("button",{name:"Settings",exact:true})).toBeFocused();
  });
}
test("save failures restore values and target setup retains activity",async({page})=>{
  await page.goto("/?scenario=save-error");
  await page.locator('[data-activity="pickpocket"]').click();
  await page.getByRole("button",{name:"Timing",exact:true}).click();
  await page.getByRole("button",{name:"yellow press later"}).click();
  await expect(page.getByLabel("Yellow · earlier by")).toHaveValue("17");
  await page.getByRole("button",{name:"Change FiveM window"}).click();
  await expect(page.getByRole("dialog",{name:"Available FiveM windows"})).toBeVisible();
  await expect(page.getByRole("navigation",{name:"Activity navigation"})).toContainText("Pickpocket");
});
