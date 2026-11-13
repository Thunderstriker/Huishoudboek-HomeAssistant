// Copy this file to Home Assistant's /config/www/ and register it as a module resource.
// Uses HA's authenticated ingress WebSocket commands; never an app/API bearer token.
const ingressConnections = new WeakMap();
const widgetNames = {
    nl: { "month-overview": "Maandoverzicht", spending: "Waar gaat mijn geld naartoe?", "freedom-map": "Financiële vrijheid" },
    en: { "month-overview": "Monthly overview", spending: "Where does my money go?", "freedom-map": "Financial freedom" }
};
const messages = {
    nl: {
        loading: "Huishoudboek laden…", refresh: "Vernieuwen",
        access: "Geen toegang. Gebruik een Home Assistant-beheerder met toegang tot Huishoudboek.",
        stopped: "Huishoudboek is gestopt. Start de app en probeer opnieuw.",
        unsupported: "Werk Home Assistant bij. Deze kaart gebruikt de supervisor/ingress-WebSocket-opdrachten (2026.11 of nieuwer).",
        unavailable: "De kaart kon niet worden geladen. Controleer de app, verbinding, eigenaar en maand en probeer opnieuw."
    },
    en: {
        loading: "Loading Huishoudboek…", refresh: "Refresh",
        access: "Access denied. Use a Home Assistant administrator with access to Huishoudboek.",
        stopped: "Huishoudboek is stopped. Start the app and try again.",
        unsupported: "Update Home Assistant. This card uses the supervisor/ingress WebSocket commands (2026.11 or newer).",
        unavailable: "The card could not be loaded. Check the app, connection, owner and month, then try again."
    }
};

class CardFailure extends Error {
    constructor(key) { super(key); this.key = key; }
}

function ingressSlot(hass, addon) {
    let slots = ingressConnections.get(hass.connection);
    if (!slots) { slots = new Map(); ingressConnections.set(hass.connection, slots); }
    const key = `${hass.user.id}:${addon}`;
    if (!slots.has(key)) slots.set(key, { value: null, pending: null });
    return slots.get(key);
}

function setIngressCookie(session) {
    if (typeof session !== "string" || !/^[A-Za-z0-9_-]{1,512}$/.test(session)) throw new CardFailure("unavailable");
    document.cookie = `ingress_session=${session};path=/api/hassio_ingress/;SameSite=Strict${location.protocol === "https:" ? ";Secure" : ""}`;
}

async function ensureIngress(hass, addon) {
    const slot = ingressSlot(hass, addon);
    if (!slot.pending && (!slot.value || Date.now() - slot.value.checked >= 60000)) {
        slot.pending = (async () => {
            const info = await ingressCall(hass, { type: "supervisor/ingress/info", slug: addon });
            if (info.state !== "started") throw new CardFailure("stopped");
            const url = new URL(info.ingress_url, location.origin);
            if (url.origin !== location.origin || url.search || url.hash ||
                !/^\/api\/hassio_ingress\/[A-Za-z0-9_-]{1,256}\/?$/.test(url.pathname)) throw new CardFailure("unavailable");
            let session = slot.value?.session;
            if (session) {
                try { await ingressCall(hass, { type: "supervisor/ingress/validate_session", session }); }
                catch { session = null; }
            }
            if (!session) session = (await ingressCall(hass, { type: "supervisor/ingress/session", slug: addon })).session;
            slot.value = { url: url.href.replace(/\/$/, "") + "/", session, checked: Date.now() };
            return slot.value;
        })().catch(error => {
            slot.value = null;
            if (error instanceof CardFailure) throw error;
            if (error?.code === "unauthorized") throw new CardFailure("access");
            if (error?.code === "unknown_command") throw new CardFailure("unsupported");
            throw new CardFailure("unavailable");
        }).finally(() => { slot.pending = null; });
    }
    return slot.pending ? await slot.pending : slot.value;
}

async function ingressCall(hass, message) {
    let timeout;
    try {
        return await Promise.race([hass.callWS(message), new Promise((_, reject) => {
            timeout = setTimeout(() => reject(new CardFailure("unavailable")), 15000);
        })]);
    } finally { clearTimeout(timeout); }
}

class HuishoudboekCard extends HTMLElement {
    constructor() {
        super();
        this._revision = 0;
        this._refreshAt = 0;
        this._loading = false;
        this._height = 360;
        this.attachShadow({ mode: "open" }).innerHTML = `
            <style>
                :host { display: block; min-width: 0; }
                ha-card { display: block; overflow: hidden; }
                iframe { display: block; width: 100%; border: 0; }
                iframe[hidden], [hidden] { display: none !important; }
                .status { padding: 24px; color: var(--secondary-text-color); font: inherit; }
                .tools { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 16px; border-top: 1px solid var(--divider-color, #ddd); }
                small { color: var(--secondary-text-color); }
                button { font: inherit; cursor: pointer; min-height: 36px; border: 0; border-radius: 6px; padding: 4px 10px; background: transparent; color: var(--primary-color, #087f75); }
                button:focus-visible { outline: 2px solid currentColor; outline-offset: 2px; }
                button:disabled { opacity: .5; cursor: wait; }
            </style>
            <ha-card>
                <div class="status" role="status" aria-live="polite"></div>
                <div class="content"></div>
                <div class="tools"><small>Huishoudboek</small><button type="button"></button></div>
            </ha-card>`;
        this._status = this.shadowRoot.querySelector(".status");
        this._content = this.shadowRoot.querySelector(".content");
        this._button = this.shadowRoot.querySelector("button");
        this._button.addEventListener("click", () => { this._refreshAt = 0; this._tick(); });
        this._wake = () => {
            if (!document.hidden) { this._refreshAt = 0; this._tick(); }
        };
    }

    setConfig(config) {
        if (!/^[a-z0-9]+_huishoudboek$/.test(config.addon || ""))
            throw new Error("Set addon to the full installed app slug, e.g. local_huishoudboek or <repository-id>_huishoudboek.");
        if (!Object.hasOwn(widgetNames.en, config.widget))
            throw new Error("widget must be month-overview, spending or freedom-map.");
        const period = config.period ?? "current";
        if (period !== "current" && (typeof period !== "string" || !/^(19\d{2}|20\d{2}|21\d{2}|2200)-(0[1-9]|1[0-2])$/.test(period)))
            throw new Error("period must be current or a quoted YYYY-MM month (1900–2200).");
        if (config.owner !== undefined && (typeof config.owner !== "string" || !/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(config.owner)))
            throw new Error("owner must be an owner UUID from Huishoudboek; omit it for the whole household.");
        if (!["auto", "light", "dark"].includes(config.theme ?? "auto")) throw new Error("theme must be auto, light or dark.");
        if (!["auto", "nl", "en"].includes(config.language ?? "auto")) throw new Error("language must be auto, nl or en.");
        const interval = config.refresh_interval ?? 300;
        if (!Number.isInteger(interval) || interval < 60 || interval > 3600) throw new Error("refresh_interval must be 60–3600 seconds.");
        this._config = { addon: config.addon, widget: config.widget, period, owner: config.owner,
            theme: config.theme ?? "auto", language: config.language ?? "auto", refresh_interval: interval };
        this._reset();
        this._tick();
    }

    set hass(hass) {
        this._hass = hass;
        const language = this._language();
        const userKey = `${hass?.user?.id ?? ""}:${hass?.user?.is_admin === true}`;
        // Store primitive identity values too: callers may mutate the same hass/user object.
        if (this._connection !== hass?.connection || this._userKey !== userKey || this._lastLanguage !== language) this._reset();
        this._connection = hass?.connection;
        this._userKey = userKey;
        this._lastLanguage = language;
        this._updateTheme();
        this._tick();
    }

    connectedCallback() {
        document.addEventListener("visibilitychange", this._wake);
        window.addEventListener("focus", this._wake);
        this._timer = setInterval(() => this._heartbeat(), 60000);
        this._tick();
    }

    disconnectedCallback() {
        clearInterval(this._timer);
        document.removeEventListener("visibilitychange", this._wake);
        window.removeEventListener("focus", this._wake);
        this._reset();
    }

    getCardSize() { return Math.ceil((this._height + 52) / 50); }
    getGridOptions() { return { columns: 12, rows: "auto", min_columns: 6 }; }

    _language() {
        return this._config?.language !== "auto" && this._config?.language
            ? this._config.language : this._hass?.locale?.language?.startsWith("nl") ? "nl" : "en";
    }

    _theme() {
        return this._config?.theme === "auto" ? (this._hass?.themes?.darkMode ? "dark" : "light") : this._config?.theme ?? "light";
    }

    _showStatus(key) {
        this._status.textContent = messages[this._language()][key] || messages[this._language()].unavailable;
        this._status.hidden = false;
        this._button.textContent = `↻ ${messages[this._language()].refresh}`;
        this._button.disabled = this._loading;
    }

    _clearFrame() {
        this._observer?.disconnect();
        this._observer = null;
        this._frame = null;
        this._content.replaceChildren();
    }

    _reset() {
        this._revision++;
        clearTimeout(this._refreshTimer);
        this._cancelLoad?.();
        this._clearFrame();
        this._loading = false;
        this._button.disabled = false;
        this._refreshAt = 0;
        this._showStatus("loading");
    }

    _tick() {
        if (!this.isConnected || !this._config || !this._hass || document.hidden) return;
        if (this._hass.connection?.connected === false) {
            this._reset();
            this._showStatus("unavailable");
            return;
        }
        if (!this._loading && Date.now() >= this._refreshAt) void this._load();
    }

    async _heartbeat() {
        this._tick();
        if (!this.isConnected || document.hidden || !this._frame || this._loading) return;
        const revision = this._revision;
        try {
            const ingress = await ensureIngress(this._hass, this._config.addon);
            if (revision === this._revision && this.isConnected) setIngressCookie(ingress.session);
        }
        catch (error) {
            if (revision !== this._revision) return;
            this._clearFrame();
            this._showStatus(error.key);
            this._scheduleRefresh(60);
        }
    }

    async _load() {
        const revision = this._revision;
        this._loading = true;
        this._clearFrame();
        this._showStatus("loading");
        try {
            // Deliberately retain the app's administrator-only policy.
            if (!this._hass.user?.is_admin) throw new CardFailure("access");
            const ingress = await ensureIngress(this._hass, this._config.addon);
            if (revision !== this._revision || !this.isConnected) return;
            // A late response from a removed card/previous user must not replace the browser cookie.
            setIngressCookie(ingress.session);
            await this._renderFrame(ingress.url, revision);
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

    _scheduleRefresh(seconds) {
        clearTimeout(this._refreshTimer);
        this._refreshAt = Date.now() + seconds * 1000;
        this._refreshTimer = setTimeout(() => this._tick(), seconds * 1000);
    }

    _renderFrame(base, revision) {
        const url = new URL(`kaarten/${this._config.widget}`, base);
        url.searchParams.set("period", this._config.period);
        url.searchParams.set("theme", this._theme());
        url.searchParams.set("language", this._language());
        if (this._config.owner) url.searchParams.set("owner", this._config.owner);
        const frame = document.createElement("iframe");
        frame.title = widgetNames[this._language()][this._config.widget];
        // Widgets are static HTML/CSS. No scripts, forms, top navigation or Blazor circuits.
        frame.setAttribute("sandbox", "allow-same-origin allow-popups allow-popups-to-escape-sandbox");
        frame.referrerPolicy = "no-referrer";
        frame.hidden = true;
        this._frame = frame;
        return new Promise((resolve, reject) => {
            const finish = error => {
                clearTimeout(timeout);
                frame.removeEventListener("load", loaded);
                if (this._cancelLoad === cancel) this._cancelLoad = null;
                if (error) reject(error); else resolve();
            };
            const cancel = () => finish(new CardFailure("unavailable"));
            const loaded = () => {
                if (revision !== this._revision) { cancel(); return; }
                try {
                    const root = frame.contentDocument?.querySelector("[data-huishoudboek-widget]");
                    if (!root || root.dataset.huishoudboekWidget !== this._config.widget || root.dataset.cardProtocol !== "1") {
                        ingressSlot(this._hass, this._config.addon).value = null;
                        throw new CardFailure("unavailable");
                    }
                    // A marker alone is not enough: refuse broken/old asset layouts too.
                    if (!["site.css", "Huishoudboek.Web.bundle.scp.css", "dashboard-cards.css"].every(name =>
                        [...frame.contentDocument.styleSheets].some(sheet => sheet.href?.endsWith(`/${name}`))))
                        throw new CardFailure("unavailable");
                    for (const link of root.querySelectorAll("a")) link.rel = "noopener noreferrer";
                    this._updateTheme();
                    this._observer = new ResizeObserver(() => this._resize(root));
                    this._observer.observe(root);
                    this._resize(root);
                    frame.hidden = false;
                    // A hidden iframe has no measurable layout until it is displayed.
                    this._resize(root);
                    finish();
                } catch (error) { finish(error); }
            };
            const timeout = setTimeout(cancel, 20000);
            this._cancelLoad = cancel;
            frame.addEventListener("load", loaded);
            frame.src = url.href;
            this._content.replaceChildren(frame);
        });
    }

    _resize(root) {
        if (!this._frame || !root.isConnected || root.ownerDocument !== this._frame.contentDocument) return;
        this._height = Math.max(120, Math.ceil(root.getBoundingClientRect().height));
        this._frame.style.height = `${this._height}px`;
    }

    _updateTheme() {
        const html = this._frame?.contentDocument?.documentElement;
        if (html) html.dataset.theme = this._theme();
    }
}

if (!customElements.get("huishoudboek-card")) customElements.define("huishoudboek-card", HuishoudboekCard);
