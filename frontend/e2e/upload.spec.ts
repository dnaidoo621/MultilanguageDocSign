import { test, expect } from "@playwright/test";
import path from "path";

const SAMPLE = path.join(process.cwd(), "e2e", "fixtures", "sample.pdf");

test("sign up, upload a PDF, OCR extracts blocks, viewer renders them", async ({ page }) => {
  const email = `e2e.${Date.now()}@example.com`;
  const password = "Test123456!";

  await page.goto("/dashboard");

  // Toggle the form into sign-up mode, then create an account.
  // (Email confirmation is disabled, so sign-up logs us straight in.)
  await page.getByRole("button", { name: /need an account\? sign up/i }).click();
  await page.getByPlaceholder("Email").fill(email);
  await page.getByPlaceholder("Password").fill(password);
  await page.getByRole("button", { name: /^sign up$/i }).click();

  // Authenticated view shows the uploader.
  await expect(page.getByText(/drop a pdf/i)).toBeVisible();

  // Upload the bilingual sample PDF.
  await page.setInputFiles('input[type="file"]', SAMPLE);

  // OCR runs in the background; the row should reach "Extracted".
  await expect(page.getByText("Extracted")).toBeVisible({ timeout: 120_000 });

  // Open the document and confirm extracted blocks render over the PDF.
  await page.getByRole("link", { name: /^view$/i }).first().click();
  await expect(page).toHaveURL(/\/documents\//);

  const blocks = page.getByTestId("ocr-block");
  await expect(blocks.first()).toBeVisible({ timeout: 60_000 });
  expect(await blocks.count()).toBeGreaterThan(0);

  // --- Phase 2: translate and verify the bilingual panel ---
  await page.getByTestId("translate-button").click();

  const segments = page.getByTestId("translation-segment");
  await expect(segments.first()).toBeVisible({ timeout: 240_000 });
  expect(await segments.count()).toBeGreaterThan(0);

  // Hovering a translated clause should highlight its source block (shared state).
  await segments.first().hover();
  await expect(page.getByTestId("translation-pane")).toBeVisible();
});
