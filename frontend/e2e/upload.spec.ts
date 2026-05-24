import { test, expect } from "@playwright/test";
import path from "path";

const SAMPLE = path.join(process.cwd(), "e2e", "fixtures", "sample.pdf");

test("sign up, run the full pipeline, read bilingual clauses, sign", async ({ page }) => {
  const email = `e2e.${Date.now()}@example.com`;
  const password = "Test123456!";

  await page.goto("/dashboard");

  // Toggle the form into sign-up mode, then create an account.
  // (Email confirmation is disabled, so sign-up logs us straight in.)
  await page.getByTestId("auth-toggle").click();
  await page.getByTestId("auth-email").fill(email);
  await page.getByTestId("auth-password").fill(password);
  await page.getByTestId("auth-submit").click();

  // Authenticated dashboard — open the uploader and upload the sample.
  await page.getByTestId("new-document").click();
  await page.setInputFiles('input[type="file"]', SAMPLE);

  // The pipeline runs upload → OCR → translate → analyze; wait for it to finish.
  await expect(page.getByTestId("open-reader")).toBeVisible({ timeout: 420_000 });
  await page.getByTestId("open-reader").click();
  await expect(page).toHaveURL(/\/documents\//);

  // Reader shows the bilingual clauses + a risk summary.
  await expect(page.getByTestId("risk-summary")).toBeVisible({ timeout: 60_000 });
  await expect(page.getByTestId("reader-clause").first()).toBeVisible({ timeout: 60_000 });

  // Sign the document and verify the audit trail + exports.
  await page.getByTestId("signer-name").fill("Darren Naidoo");
  await page.getByTestId("sign-button").click();

  await expect(page.getByTestId("signed-status")).toBeVisible({ timeout: 60_000 });
  await expect(page.getByTestId("signed-status")).toContainText("Darren Naidoo");
  await expect(page.getByTestId("audit-event").first()).toBeVisible();
  await expect(page.getByTestId("download-signed")).toBeVisible();
  await expect(page.getByTestId("download-export")).toBeVisible();
});
