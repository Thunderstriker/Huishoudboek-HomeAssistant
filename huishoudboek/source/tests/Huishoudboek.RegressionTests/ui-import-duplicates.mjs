// Run only against a disposable local --preview administration, never household data.
import assert from "node:assert/strict";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || "playwright");
const base = process.env.UI_PREVIEW_URL || "http://127.0.0.1:5199/";
assert.match(base, /^http:\/\/(127\.0\.0\.1|localhost):\d+\/$/);
const browser = await chromium.launch({ channel: "msedge", headless: true });
const context = await browser.newContext({ viewport: { width: 390, height: 844 }, locale: "nl-NL" });
const page = await context.newPage();
const errors = [];
page.on("pageerror", error => errors.push(error.message));
page.on("dialog", dialog => dialog.accept());
const prefix = "DUPLICATE TEST " + Date.now();
let date, account;
async function ready(path = "importeren") {
    await page.goto(new URL(path, base).href);
    await page.waitForSelector('html[data-interactive="true"]');
    assert.match(await page.locator(".topbar strong").innerText(), /demo/);
}
function csv(rows) {
    return "Datum;Naam / Omschrijving;Mededelingen;Bedrag (EUR);Af Bij\n" +
        rows.map(([name, amount]) => `${date};${name};Test only;${amount};Af`).join("\n") + "\n";
}
async function preview(rows) {
    await ready();
    const yesterday = new Date(Date.now() - 24 * 3600 * 1000).toISOString().slice(0, 10);
    date ||= yesterday.replaceAll("-", "");
    account ||= await page.locator('#importAccount option:not([value=""])').first().getAttribute("value");
    await page.locator("#importAccount").selectOption(account);
    await page.locator("#importFile").setInputFiles({ name: prefix + ".csv", mimeType: "text/csv", buffer: Buffer.from(csv(rows)) });
    await page.locator("#import-options summary").click();
    await page.locator("#from").fill(yesterday);
    await page.locator("#through").fill(yesterday);
    await page.locator("#preview").click();
    await page.waitForSelector("#previewPanel");
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
}
async function save(expected) {
    await page.locator("#commit").click();
    await page.getByRole("button", { name: "Import opslaan", exact: true }).click();
    await page.getByRole("button", { name: "Verder: categorieën controleren", exact: true }).waitFor();
    assert.match(await page.locator("dialog[open]").innerText(), new RegExp(expected + " transacties opgeslagen"));
    await page.getByRole("button", { name: "Verder: categorieën controleren", exact: true }).click();
    await page.waitForURL(/categoriseren/);
}
async function selected(expected) {
    await page.waitForFunction(count => document.querySelectorAll('.import-review input[type="checkbox"]:checked').length === count, expected);
}
const shop = [prefix + " Shop", "12,34"];
const other = [prefix + " Other", "20,00"];
try {
    await preview([shop, shop]);
    assert.match(await page.locator(".import-duplicate-summary").innerText(), /2 nieuw · 0 al geïmporteerd · 0 te controleren/);
    assert.equal(await page.locator('.import-review input[type="checkbox"]:checked').count(), 2);
    await save(2);
    console.log("PASS Initial identical payments are both imported");

    await preview([shop, shop]);
    assert.match(await page.locator(".import-duplicate-summary").innerText(), /0 nieuw · 2 al geïmporteerd · 0 te controleren/);
    assert.equal(await page.locator('.import-review input[type="checkbox"]:checked').count(), 0);
    assert.equal(await page.locator('.import-status[data-status="AlreadyImported"]').count(), 2);
    const exceptions = page.getByLabel(/Alleen controlepunten/);
    await exceptions.check();
    await page.waitForFunction(() => document.querySelectorAll(".import-review tbody tr").length === 0);
    await exceptions.uncheck();
    await page.waitForFunction(() => document.querySelectorAll(".import-review tbody tr").length === 2);
    await page.locator('.import-review input[type="checkbox"]').first().check();
    await page.locator("#commit").click();
    assert.match(await page.locator("dialog[open]").innerText(), /u voegt 1 regel/);
    await page.getByRole("button", { name: "Annuleren", exact: true }).click();
    await page.getByRole("button", { name: "Alleen nieuwe regels selecteren", exact: true }).click();
    await selected(0);
    assert.equal(await page.locator('.import-review input[type="checkbox"]:checked').count(), 0);
    const coverage = page.getByLabel(/^Ik bevestig dat dit bestand/);
    assert.equal(await coverage.isEnabled(), true);
    await page.locator("#commit").click();
    await page.getByRole("button", { name: "Import opslaan", exact: true }).click();
    await page.getByRole("button", { name: "Terug naar dashboard", exact: true }).waitFor();
    assert.match(await page.locator("dialog[open]").innerText(), /0 transacties opgeslagen/);
    assert.match(await page.locator("dialog[open]").innerText(), /2 overeenkomende eerdere boekingen niet opnieuw toegevoegd/);
    await page.getByRole("button", { name: "Terug naar dashboard", exact: true }).click();
    await page.waitForURL(base);
    console.log("PASS Repeated import finishes without adding payments and warns on an override");

    await preview([shop, shop, shop, other]);
    assert.match(await page.locator(".import-duplicate-summary").innerText(), /2 nieuw · 2 al geïmporteerd · 0 te controleren/);
    assert.equal(await page.locator('.import-review input[type="checkbox"]:checked').count(), 2);
    await page.getByLabel(/^Ik bevestig dat dit bestand/).check();
    await save(2);
    console.log("PASS Overlapping exports import only additional occurrences and allow complete coverage");

    await preview([[shop[0].toUpperCase(), shop[1]], [prefix + " New", "30,00"]]);
    assert.match(await page.locator(".import-duplicate-summary").innerText(), /1 nieuw · 0 al geïmporteerd · 1 te controleren/);
    assert.equal(await page.getByLabel(/^Ik bevestig dat dit bestand/).isEnabled(), false);
    await page.locator('.import-review tbody tr').filter({ has: page.locator('[data-status="NeedsReview"]') }).locator('input[type="checkbox"]').check();
    await page.waitForFunction(() => !document.querySelector('#previewPanel > label.check input').disabled);
    assert.equal(await page.getByLabel(/^Ik bevestig dat dit bestand/).isEnabled(), true);
    await page.locator("#commit").click();
    assert.match(await page.locator("dialog[open]").innerText(), /u voegt 1 regel/);
    await page.getByRole("button", { name: "Annuleren", exact: true }).click();
    await page.getByRole("button", { name: "Alleen nieuwe regels selecteren", exact: true }).click();
    await selected(1);
    assert.equal(await page.locator('.import-review input[type="checkbox"]:checked').count(), 1);
    assert.equal(await page.getByLabel(/^Ik bevestig dat dit bestand/).isEnabled(), false);
    await save(1);
    console.log("PASS Uncertain matches stay excluded and cannot silently establish statement coverage");

    await page.setViewportSize({ width: 1440, height: 1000 });
    // Exercise the English import independently of the navigation's language controls.
    await context.addCookies([{ name: ".AspNetCore.Culture", value: "c=en-GB|uic=en-GB", url: base }]);
    await preview([shop, shop, shop, other]);
    assert.match(await page.locator(".import-duplicate-summary").innerText(), /0 new · 4 already imported · 0 to review/);
    assert.equal(await page.getByRole("button", { name: "Select new rows only", exact: true }).count(), 1);
    assert.equal(await page.getByRole("link", { name: "View existing entry", exact: true }).count(), 4);
    console.log("PASS English import controls and matching status are translated");
    assert.deepEqual(errors, [], "No uncaught browser errors");
    console.log("All import duplicate browser checks passed.");
} finally { await browser.close(); }
