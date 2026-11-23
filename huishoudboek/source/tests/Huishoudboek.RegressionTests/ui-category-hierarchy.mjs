// Run only against a disposable local --preview administration, never household data.
import assert from "node:assert/strict";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || "playwright");
const base = process.env.UI_PREVIEW_URL || "http://127.0.0.1:5199/";
assert.match(base, /^http:\/\/(127\.0\.0\.1|localhost):\d+\/$/);
const browser = await chromium.launch({ channel: "msedge", headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, locale: "nl-NL" });
const errors = [];
page.on("pageerror", error => errors.push(error.message));
page.on("dialog", dialog => dialog.accept());
const prefix = "CATEGORY TEST " + Date.now();
const escape = text => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
let labels;
async function ready(path = "budgetten") {
    await page.goto(new URL(path, base).href);
    await page.waitForSelector('html[data-interactive="true"]');
    assert.match(await page.locator(".topbar strong").innerText(), /demo/);
    assert.equal(await page.locator("#blazor-error-ui").isVisible(), false);
}
function row(name) {
    return page.locator("tbody tr").filter({ has: page.locator(".category-name").filter({ hasText: new RegExp("^" + escape(name)) }) });
}
async function save() {
    await page.locator("dialog[open]").getByRole("button", { name: labels.save, exact: true }).click();
    await page.waitForSelector("dialog[open]", { state: "detached" });
    assert.equal(await page.locator("#message.error").count(), 0);
}
async function settings(name) {
    await row(name).getByRole("button", { name: labels.settings, exact: true }).click();
    await page.waitForSelector("dialog[open] #parent");
    assert.equal(await page.locator("#name").inputValue(), name);
}
async function create(name, parent) {
    if (parent) await row(parent).getByRole("button", { name: "+ " + labels.subcategory, exact: true }).click();
    else await page.getByRole("button", { name: "+ " + labels.category, exact: true }).click();
    await page.waitForSelector("dialog[open] #parent");
    assert.equal(await page.locator("#budget").inputValue(), labels.zero);
    if (parent) assert.notEqual(await page.locator("#parent").inputValue(), "");
    else assert.equal(await page.locator("#parent").inputValue(), "");
    await page.locator("#name").fill(name);
    await page.locator("#budget").fill(parent ? "30" : "100");
    await save(); await row(name).waitFor();
}
async function move(name, parentPath) {
    await settings(name);
    if (parentPath) await page.locator("#parent").selectOption({ label: parentPath });
    else await page.locator("#parent").selectOption("");
    await save();
}
async function categoryChoice(path) {
    await ready("transacties");
    const option = page.locator(".advanced-filters select option").filter({ hasText: new RegExp("^" + escape(path) + "$") });
    assert.equal(await option.count(), 1);
    return option.getAttribute("value");
}
try {
    for (const english of [false, true]) {
        await page.setViewportSize({ width: 1440, height: 1000 }); await ready();
        const language = page.locator(".app-shell > .sidebar .language-switch").getByRole("button", { name: english ? "English" : "Nederlands", exact: true });
        if (await language.getAttribute("aria-current") !== "true") {
            await page.setViewportSize({ width: 390, height: 844 });
            await page.locator(".mobile-menu-toggle").click();
            await page.locator(".drawer-nav .language-switch").getByRole("button", { name: english ? "English" : "Nederlands", exact: true }).click();
        }
        await page.waitForSelector('html[data-interactive="true"]');
        labels = english
            ? { category: "Category", subcategory: "Subcategory", settings: "Category settings", save: "Save", cancel: "Cancel", delete: "Delete category", zero: "0.00" }
            : { category: "Categorie", subcategory: "Subcategorie", settings: "Categorie instellen", save: "Opslaan", cancel: "Annuleren", delete: "Categorie verwijderen", zero: "0,00" };
        if (english) await page.setViewportSize({ width: 390, height: 844 });
        const names = `${prefix} ${english ? "EN" : "NL"}`;
        const a = names + " Home", b = names + " Other", child = names + " Electronics", leaf = names + " Lights";
        await create(a); await create(b); await create(child, a); await create(leaf, child);
        await settings(a);
        for (const excluded of [a, `${a} › ${child}`, `${a} › ${child} › ${leaf}`])
            assert.equal(await page.locator("#parent option").filter({ hasText: new RegExp("^" + escape(excluded) + "$") }).count(), 0);
        await page.locator("dialog[open]").getByRole("button", { name: labels.cancel, exact: true }).click();
        await page.waitForSelector("dialog[open]", { state: "detached" });

        const leafId = await categoryChoice(`${a} › ${child} › ${leaf}`);
        await ready("transacties?entry=expense"); await page.waitForSelector("dialog[open] #category");
        await page.locator("#description").fill(leaf);
        await page.locator("#amount").fill("12.34"); await page.locator("#category").selectOption(leafId); await save();

        await ready(); await move(b, a); await move(child, `${a} › ${b}`);
        assert.equal(await categoryChoice(`${a} › ${b} › ${child} › ${leaf}`), leafId);
        await ready(); await move(b, null);
        assert.equal(await categoryChoice(`${b} › ${child} › ${leaf}`), leafId);
        await ready(); await move(child, null);
        assert.equal(await categoryChoice(`${child} › ${leaf}`), leafId);
        await ready(); await move(child, b);
        await ready("categoriseren");
        await page.locator(".category-list").getByRole("button", { name: new RegExp("^" + escape(b) + "\\s+\\d+$") }).click();
        await page.getByRole("button", { name: labels.delete, exact: true }).click();
        await page.waitForSelector("dialog[open]");
        assert.match(await page.locator("dialog[open]").innerText(), english ? /move up one level/ : /één niveau omhoog/);
        await page.locator("dialog[open]").getByRole("button", { name: labels.delete, exact: true }).click();
        await page.waitForSelector("dialog[open]", { state: "detached" });
        assert.equal(await categoryChoice(`${child} › ${leaf}`), leafId);
        await ready(`transacties?allDates=true&categorie=${leafId}`);
        assert.equal(await page.locator("#transactions tbody tr").count(), 1);
        assert.equal(await page.locator("#transactions .booking-link").textContent(), leaf);
        assert.match(await page.locator("#transactions tbody tr td").nth(3).textContent(), /12[,.]34/);
        await ready(); await settings(child);
        assert.equal(await page.locator("#parent").inputValue(), "");
        assert.equal(await page.locator("#budget").inputValue(), english ? "30.00" : "30,00");
        await page.locator("dialog[open]").getByRole("button", { name: labels.cancel, exact: true }).click();
        await page.waitForSelector("dialog[open]", { state: "detached" });
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
        console.log(`PASS ${english ? "English mobile" : "Dutch desktop"}: nested creation, root/subtree moves, deletion promotion and retained booking/budget`);
    }
    assert.deepEqual(errors, [], "No uncaught browser errors");
    console.log("All category hierarchy browser checks passed.");
} finally { await browser.close(); }
