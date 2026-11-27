// Supplemental checks against a disposable demo. Does not change production CSP.
import assert from "node:assert/strict";
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || "playwright");
const base = process.env.UI_PREVIEW_URL || "http://127.0.0.1:5199/";
assert.match(base, /^http:\/\/(127\.0\.0\.1|localhost):\d+\/$/);
assert.ok(process.env.AXE_SCRIPT, "Set AXE_SCRIPT to an externally installed axe-core/axe.min.js");
const browser = await chromium.launch({ channel: "msedge", headless: true });
// The testing context bypasses CSP solely to inject axe. The normal UI smoke test does not bypass it.
const page = await browser.newPage({ bypassCSP: true, reducedMotion: "reduce" });
const failures = [];
let checks = 0;
try {
    for (const theme of ["light", "dark"]) {
        for (const path of ["", "maand", "jaar", "kalender", "categorietrends", "budgetten", "transacties", "vooruitblik",
            "inzichten", "rekeningen", "aankopen", "beleggingen", "hypotheek", "importeren", "meer", "weekcontrole"]) {
            await page.goto(new URL(path, base).href);
            await page.waitForSelector('html[data-interactive="true"]');
            assert.match(await page.locator(".topbar strong").innerText(), /demo/);
            await page.evaluate(value => huishoudboek.appearance.set(value), theme);
            await page.addScriptTag({ path: process.env.AXE_SCRIPT });
            const result = await page.evaluate(() => axe.run("#content", { runOnly: {
                type: "rule", values: ["color-contrast", "aria-valid-attr-value", "label"]
            } }));
            checks++;
            if (result.violations.length) failures.push({ page: path || "dashboard", theme,
                violations: result.violations.map(v => ({ rule: v.id, nodes: v.nodes.map(n => ({ target: n.target, message: n.failureSummary })) })) });
        }
    }
    assert.deepEqual(failures, [], JSON.stringify(failures, null, 2));
    console.log(`PASS ${checks} page/theme accessibility checks (contrast, labels, ARIA attribute values).`);
} finally { await browser.close(); }
