// Run only against a disposable local --preview administration, never household data.
// Install Playwright outside the repository and set PLAYWRIGHT_MODULE to its index.mjs file URL.
import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import { join } from "node:path";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || "playwright");
const base = process.env.UI_PREVIEW_URL || "http://127.0.0.1:5199/";
assert.match(base, /^http:\/\/(127\.0\.0\.1|localhost):\d+\/$/);
const browser = await chromium.launch({ channel: "msedge", headless: true });
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, locale: "nl-NL", colorScheme: "light" });
const page = await context.newPage();
const errors = [];
page.on("pageerror", error => errors.push(error.message));
let acceptDialogs = true, confirmations = 0;
page.on("dialog", async dialog => { confirmations++; if (acceptDialogs) await dialog.accept(); else await dialog.dismiss(); });
const shots = process.env.UI_SCREENSHOT_DIR;
if (shots) await mkdir(shots, { recursive: true });
async function screenshot(name) { if (shots) await page.screenshot({ path: join(shots, name + ".png"), fullPage: true }); }
async function ready(path = "") {
    await page.goto(new URL(path, base).href);
    await page.waitForSelector('html[data-interactive="true"]');
    assert.match(await page.locator(".topbar strong").innerText(), /demo/);
    assert.equal(await page.locator("#blazor-error-ui").isVisible(), false);
}
async function noOverflow() {
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), "Page must not horizontally overflow");
}
async function check(name, run) { await run(); console.log("PASS " + name); }
try {
    await check("Dashboard shortcuts and source-booking navigation", async () => {
        await ready();
        assert.equal(await page.locator(".dashboard-quick-actions button").count(), 2);
        await screenshot("dashboard-light");
        await page.locator(".recent-list .booking-link").first().click();
        await page.waitForSelector("dialog[open]");
        assert.match(page.url(), /transaction=/);
        await page.getByRole("button", { name: "Sluiten", exact: true }).click();
        await page.waitForSelector("dialog[open]", { state: "detached" });
    });
    await check("Page-aware date controls", async () => {
        for (const path of ["kalender", "categorietrends", "inzichten", "rekeningen"] ) {
            await ready(path); assert.equal(await page.locator(".period-controls").count(), 0);
        }
        await ready("jaar"); assert.equal(await page.locator(".period-controls select").count(), 0);
        assert.equal(await page.locator(".period-controls input.year").count(), 1);
        await ready("maand"); assert.equal(await page.locator(".period-controls select").count(), 1);
    });
    await check("Source-booking links preserve the selected owner", async () => {
        await ready();
        const picker = page.locator(".toolbar > label select");
        const owner = await picker.locator('option:not([value=""])').first().getAttribute("value");
        await picker.selectOption(owner);
        await page.waitForFunction(id => document.querySelector(".recent-list .booking-link")?.href.includes("owner=" + id), owner);
        await page.locator(".recent-list .booking-link").first().click();
        await page.waitForSelector("dialog[open]");
        assert.equal(await picker.inputValue(), owner);
        await page.getByRole("button", { name: "Sluiten", exact: true }).click();
        await page.waitForSelector("dialog[open]", { state: "detached" });
    });
    await check("Field validation, decimal input and Escape draft protection", async () => {
        await ready();
        await page.locator(".dashboard-quick-actions").getByRole("button", { name: "Uitgave toevoegen" }).click();
        await page.waitForSelector("dialog[open]");
        await page.getByRole("button", { name: "Opslaan", exact: true }).click();
        await page.waitForSelector('dialog [aria-invalid="true"]');
        assert.ok(await page.locator(".field-error").count() >= 2);
        await page.locator("#description").fill("UX test draft"); await page.locator("#amount").fill("12.34");
        acceptDialogs = false;
        const previous = confirmations; await Promise.all([page.waitForEvent("dialog"), page.keyboard.press("Escape")]);
        await page.waitForFunction(() => document.querySelector("dialog[open]") !== null);
        await Promise.all([page.waitForEvent("dialog"), page.getByRole("button", { name: "Annuleren", exact: true }).click()]);
        assert.ok(confirmations > previous); assert.equal(await page.locator("dialog[open]").count(), 1);
        acceptDialogs = true;
        await Promise.all([page.waitForEvent("dialog"), page.getByRole("button", { name: "Annuleren", exact: true }).click()]);
        await page.waitForSelector("dialog[open]", { state: "detached" });
    });
    await check("Custom transaction filters validate ranges and update bank summaries", async () => {
        await ready("transacties");
        await page.getByRole("combobox", { name: "Periode", exact: true }).selectOption("custom");
        const fields = page.locator(".advanced-filters");
        await fields.getByLabel("Minimaal bankbedrag (€)").fill("bad");
        await page.getByRole("alert").filter({ hasText: "Gebruik positieve bedragen" }).waitFor();
        await fields.getByLabel("Minimaal bankbedrag (€)").fill("10,50");
        await fields.getByLabel("Maximaal bankbedrag (€)").fill("100.00");
        await fields.getByRole("combobox", { name: "Bankrichting", exact: true }).selectOption("debit");
        assert.equal(await page.locator(".summary-totals > div").count(), 3);
        await page.getByRole("button", { name: "Filters wissen", exact: true }).click();
        await page.waitForFunction(() => document.querySelectorAll(".filter-chips button").length === 1);
        assert.equal(await page.locator(".filter-chips").getByRole("button").count(), 1);
    });
    await check("Forecast chart is responsive and its exact table is retained", async () => {
        await ready("vooruitblik");
        await page.waitForFunction(() => window.Chart && Chart.getChart(document.querySelector(".cash-timeline canvas")));
        await page.locator(".cash-timeline").getByRole("button", { name: "90 dagen", exact: true }).click();
        await screenshot("forecast-light");
        assert.equal(await page.locator("#daily-forecast-table table").count(), 1);
    });
    await check("Main app dark theme persists across reload and updates charts", async () => {
        await page.locator(".theme-picker select").selectOption("dark");
        await page.waitForSelector('html[data-theme="dark"]');
        await ready(); assert.equal(await page.locator("html").getAttribute("data-theme"), "dark");
        await screenshot("dashboard-dark");
        await ready("vooruitblik"); await screenshot("forecast-dark");
    });
    await check("Mobile navigation uses a drawer and a persistent bottom bar", async () => {
        await page.setViewportSize({ width: 390, height: 844 }); await ready();
        assert.equal(await page.locator(".app-shell > .sidebar").isVisible(), false);
        assert.equal(await page.locator(".mobile-bottom-nav").isVisible(), true);
        await page.locator(".mobile-menu-toggle").click(); await page.waitForSelector("dialog[open] .drawer-nav");
        assert.equal(await page.locator(".drawer-nav").getByRole("button", { name: "English", exact: true }).count(), 1);
        await page.locator(".drawer-nav").getByRole("button", { name: "Overzichten", exact: true }).click();
        await page.locator(".drawer-nav").getByRole("link", { name: "Financiële kalender", exact: true }).click();
        await page.waitForSelector(".cash-calendar"); await noOverflow(); await screenshot("calendar-mobile-dark");
        await page.locator(".mobile-bottom-nav").getByRole("link", { name: "Uitgave toevoegen", exact: true }).click();
        await page.waitForSelector("dialog[open] #amount");
        await page.getByRole("button", { name: "Annuleren", exact: true }).click();
    });
    await check("Four-stage bank import has mobile reviews and explicit saving", async () => {
        await ready("importeren");
        assert.equal(await page.locator(".import-stepper button").count(), 4);
        const date = (await page.locator("#from").inputValue()).replaceAll("-", "");
        const csv = "Datum;Naam / Omschrijving;Mededelingen;Bedrag (EUR);Af Bij;Saldo na mutatie\n" +
            `${date};UX TEST SHOP ${Date.now()};Test only;12,34;Af;100,00\ninvalid;UX INVALID;Test only;2,00;Af;98,00\n`;
        await page.locator("#importFile").setInputFiles({ name: "ux-test.csv", mimeType: "text/csv", buffer: Buffer.from(csv) });
        await page.getByRole("button", { name: "Doorgaan naar herkenning", exact: true }).click();
        assert.equal(await page.locator(".card details[open]").count(), 0);
        await page.locator("#preview").click(); await page.waitForSelector("#previewPanel");
        assert.equal(await page.locator(".import-review tbody tr").count(), 2);
        assert.equal(await page.locator(".import-review tbody tr").first().evaluate(el => getComputedStyle(el).display), "block");
        assert.equal(await page.locator('input[type="checkbox"]:disabled').count() > 0, true);
        await noOverflow(); await screenshot("import-mobile-dark");
        await page.locator("#commit").click(); await page.waitForSelector("dialog[open]");
        await page.getByRole("button", { name: "Import opslaan", exact: true }).click();
        await page.getByRole("button", { name: "Verder: categorieën controleren", exact: true }).waitFor();
        await page.getByRole("button", { name: "Verder: categorieën controleren", exact: true }).click();
        await page.waitForURL(/categoriseren/);
    });
    await check("Mobile work screens do not overflow", async () => {
        for (const path of ["", "transacties", "budgetten", "maand", "jaar", "categorietrends", "vooruitblik"]) {
            await ready(path); await noOverflow();
        }
    });
    assert.deepEqual(errors, [], "No uncaught browser errors");
    console.log("All UI smoke checks passed.");
} catch (error) {
    await screenshot("failure"); console.error(errors); throw error;
} finally { await browser.close(); }
