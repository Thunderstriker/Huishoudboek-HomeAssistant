// Run only against a fresh, disposable local --preview administration.
import assert from "node:assert/strict";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || "playwright");
const base = process.env.UI_PREVIEW_URL || "http://127.0.0.1:5199/";
assert.match(base, /^http:\/\/(127\.0\.0\.1|localhost):\d+\/$/);
const browser = await chromium.launch({ channel: "msedge", headless: true });
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, locale: "nl-NL" });
const errors = [];
page.on("pageerror", error => errors.push(error.message));
const contractName = "Test energiecontract";
const contract = page.locator(".contract-card").filter({ has: page.getByRole("heading", { name: contractName, exact: true }) });
const link = within => within.getByRole("link", { name: `Gekoppeld contract: ${contractName}`, exact: true });

async function open(path) {
    await page.goto(new URL(path, base).href);
    await page.waitForSelector('html[data-interactive="true"]');
    assert.match(await page.locator(".topbar strong").innerText(), /demo/);
}

async function follow(source) {
    const href = await source.getAttribute("href");
    const target = new URL(href, base);
    assert.match(target.hash, /^#contract-[0-9a-f-]{36}$/);
    assert.equal(target.searchParams.get("contract"), target.hash.slice("#contract-".length));
    await source.click();
    await page.waitForURL(target.href);
    await contract.locator(":scope > details[open]").waitFor();
    assert.equal(`#${await contract.getAttribute("id")}`, target.hash);
    await page.waitForFunction(id => {
        const bounds = document.getElementById(id)?.getBoundingClientRect();
        return bounds && bounds.bottom > 0 && bounds.top < window.innerHeight;
    }, target.hash.slice(1));
}

async function save(editor) {
    await editor.getByRole("button", { name: "Opslaan", exact: true }).click();
    await editor.waitFor({ state: "hidden" });
    assert.equal(await page.locator("#message.error").count(), 0);
}

async function setStatus(value) {
    await contract.getByRole("button", { name: "Bewerken", exact: true }).click();
    const editor = page.locator("dialog[open]");
    await editor.locator("#status").selectOption(value);
    await save(editor);
}

try {
    await open("abonnementen");
    const seed = page.locator("details.card").filter({ has: page.getByText("Overnemen uit een bestaande vaste post", { exact: true }) });
    await seed.locator(":scope > summary").click();
    await seed.getByRole("combobox").selectOption({ label: "Demo energie · Gezamenlijk (demo)" });
    await seed.getByRole("button", { name: "Contractgegevens aanvullen" }).click();
    const editor = page.locator("dialog[open]");
    await editor.locator("#name").fill(contractName);
    await save(editor);
    await contract.waitFor();

    await open("vaste-posten");
    const monthly = page.locator("#payment-status");
    await link(monthly).waitFor();
    const planning = page.locator("section > details").filter({ has: page.getByText("Planningen beheren", { exact: true }) });
    await planning.locator(":scope > summary").click();
    await follow(link(planning));

    await open("transacties?allDates=true");
    const desktop = page.locator(".desktop-transactions");
    const energyRow = desktop.getByRole("row").filter({ has: page.getByRole("button", { name: "Demo energie", exact: true }) }).first();
    await link(energyRow).waitFor();
    for (const name of ["Demo verzekering", "Demo supermarkt"]) {
        const unlinked = desktop.getByRole("row").filter({ hasText: name }).first();
        await unlinked.waitFor();
        assert.equal(await unlinked.locator(".linked-contract").count(), 0);
    }
    await follow(link(energyRow));

    await setStatus("Cancelled");
    await open("abonnementen");
    assert.equal(await contract.count(), 0);

    await page.setViewportSize({ width: 390, height: 844 });
    await open("transacties?allDates=true");
    const mobile = page.locator(".mobile-transactions");
    const energyCard = mobile.locator(".transaction-item").filter({ hasText: "Demo energie" }).first();
    await energyCard.locator(":scope > summary").click();
    await follow(link(energyCard));
    assert.equal(await contract.locator(".page-heading .badge").innerText(), "Opgezegd");
    await setStatus("Active");
    assert.equal(await page.locator("#blazor-error-ui").isVisible(), false);
    assert.deepEqual(errors, []);
    console.log("Linked contracts: recurring payments, desktop and mobile transactions, and closed contract links passed.");
} finally {
    await browser.close();
}
