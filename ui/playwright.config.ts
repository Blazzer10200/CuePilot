import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e", outputDir: "./test-results", workers: 1,
  use: { baseURL: "http://127.0.0.1:1425", channel: "msedge", headless: true, trace: "retain-on-failure", screenshot: "only-on-failure", reducedMotion: "reduce" },
  webServer: { command: "npm run dev -- --host 127.0.0.1 --port 1425", url: "http://127.0.0.1:1425", reuseExistingServer: false },
});
