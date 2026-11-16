import { defineConfig } from "@playwright/test";

const baseURL = "http://127.0.0.1:6091";
export default defineConfig({
  testDir: "./tests/browser",
  timeout: 45_000,
  retries: 0,
  reporter: "list",
  outputDir: "test-results",
  use: {
    baseURL,
    browserName: "chromium",
    headless: true,
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  webServer: {
    command: "python3 -m http.server 6091 --bind 127.0.0.1 --directory storybook-static",
    url: baseURL + "/iframe.html",
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
