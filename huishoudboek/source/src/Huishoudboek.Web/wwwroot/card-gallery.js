import "./huishoudboek-card.js";

// This adapter is not a HA authentication fallback and is never enabled by card YAML.
// The server exposes the gallery only in --preview mode and still checks peer + Host.
if (document.documentElement.dataset.localCardPreview !== "true" ||
    !(location.hostname === "localhost" || location.hostname === "[::1]" || /^127(?:\.\d{1,3}){3}$/.test(location.hostname))) {
    throw new Error("The card gallery is available only in the explicit loopback preview.");
}

// Only the surrounding native ha-card surface is simulated. Configuration, frames,
// resizing, theme handling and refresh controls come from the production card class.
if (!customElements.get("ha-card")) customElements.define("ha-card", class extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: "open" }).innerHTML = `<style>:host { display: block; background: var(--ha-card-background, white); color: var(--primary-text-color); border: 1px solid var(--divider-color, #ddd); border-radius: 12px; box-shadow: 0 2px 6px #0000000a; }</style><slot></slot>`;
    }
});

class LocalPreviewCard extends customElements.get("huishoudboek-card") {
    async _heartbeat() { this._tick(); }

    async _load() {
        const revision = this._revision;
        this._loading = true;
        this._clearFrame();
        this._showStatus("loading");
        try {
            // Replace only the ingress transport; the normal card never takes this path.
            await this._renderFrame(new URL("/", location.href).href, revision);
            if (revision !== this._revision) return;
            this._status.hidden = true;
            this._scheduleRefresh(this._config.refresh_interval);
        } catch (error) {
            if (revision !== this._revision) return;
            this._clearFrame();
            this._showStatus(error.key);
            this._scheduleRefresh(60);
        } finally {
            if (revision === this._revision) { this._loading = false; this._button.disabled = false; }
        }
    }
}
customElements.define("huishoudboek-preview-card", LocalPreviewCard);

const controls = document.querySelector("#gallery-controls");
const field = name => controls.elements.namedItem(name);
const stage = document.querySelector("#preview-stage");
const error = document.querySelector("#gallery-error");
const parameters = new URL(location.href).searchParams;
for (const name of ["theme", "viewport", "period", "month", "owner"]) {
    const value = parameters.get(name);
    if (value !== null) {
        const element = field(name);
        if (element instanceof HTMLSelectElement) {
            if ([...element.options].some(option => option.value === value)) element.value = value;
            // Do not widen a previously selected, now-deleted owner to the whole household.
            else if (name === "owner" && value !== "") {
                const unavailable = new Option(value, value, true, true);
                element.add(unavailable);
            }
        } else if (element instanceof HTMLInputElement && /^\d{4}-(0[1-9]|1[0-2])$/.test(value)) element.value = value;
    }
}

const hass = {
    connection: { connected: true }, user: { id: "local-demo", is_admin: true },
    locale: { language: field("language").value }, themes: { darkMode: false }
};
const widgets = ["month-overview", "spending", "freedom-map"];
const cards = widgets.map(widget => {
    const card = document.createElement("huishoudboek-preview-card");
    card.dataset.widget = widget;
    document.querySelector("#preview-cards").append(card);
    return card;
});

function saveControls() {
    const url = new URL(location.href);
    for (const name of ["theme", "viewport", "language", "period", "month", "owner"]) {
        if (field(name).value) url.searchParams.set(name, field(name).value);
        else url.searchParams.delete(name);
    }
    history.replaceState(null, "", url);
}

function updateAppearance() {
    document.documentElement.dataset.theme = field("theme").value;
    stage.dataset.viewport = field("viewport").value;
    hass.themes.darkMode = field("theme").value === "dark";
    for (const card of cards) card.hass = hass;
}

function updateCards() {
    field("month").disabled = field("period").value === "current";
    error.hidden = true;
    try {
        const period = field("period").value === "current" ? "current" : field("month").value;
        for (const [index, card] of cards.entries()) {
            card.setConfig({ addon: "local_huishoudboek", widget: widgets[index], period,
                owner: field("owner").value || undefined, language: field("language").value, theme: "auto" });
            card.hass = hass;
        }
        document.querySelector("#gallery-yaml").textContent = widgets.map(widget =>
            `type: custom:huishoudboek-card\naddon: abcdef12_huishoudboek\nwidget: ${widget}\nperiod: "${period}"\n` +
            (field("owner").value ? `owner: "${field("owner").value}"\n` : "") +
            `theme: auto\nlanguage: ${field("language").value}`).join("\n\n# ---\n\n");
    } catch {
        // A bad filter must not leave the previous owner's financial data on screen.
        for (const card of cards) { card._config = null; card._reset(); }
        document.querySelector("#gallery-yaml").textContent = "";
        error.hidden = false;
    }
}

controls.addEventListener("submit", event => {
    event.preventDefault();
    if (!controls.reportValidity()) return;
    updateCards();
    saveControls();
});
controls.addEventListener("change", event => {
    if (event.target.name === "language") {
        saveControls();
        location.reload();
        return;
    }
    if (event.target.name === "theme" || event.target.name === "viewport") updateAppearance();
    else updateCards();
    saveControls();
});
updateAppearance();
updateCards();
