import { defineConfig, devices } from "@playwright/test";

// Assumes the stack is already running:
//   - frontend  http://localhost:3000
//   - backend   http://localhost:5080
//   - OCR sidecar http://localhost:8000
//   - Postgres (docker compose)
export default defineConfig({
  testDir: "./e2e",
  timeout: 360_000,
  expect: { timeout: 60_000 },
  fullyParallel: false,
  retries: 0,
  reporter: [["list"]],
  use: {
    baseURL: "http://localhost:3000",
    headless: true,
    trace: "on-first-retry",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
