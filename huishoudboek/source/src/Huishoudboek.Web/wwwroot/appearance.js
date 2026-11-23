// Applied in the head before painting. Only a non-financial display preference is stored.
(function () {
    const key = "huishoudboek:" + new URL(document.baseURI).pathname + ":theme";
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    let preference = "auto";
    try { preference = localStorage.getItem(key) || "auto"; } catch { /* Private/embedded storage may be blocked. */ }
    if (!["auto", "light", "dark"].includes(preference)) preference = "auto";
    function apply() { document.documentElement.dataset.theme = preference === "auto" ? (media.matches ? "dark" : "light") : preference; }
    apply();
    media.addEventListener("change", () => { if (preference === "auto") apply(); });
    window.huishoudboekAppearance = {
        read: () => preference,
        set: value => {
            if (!["auto", "light", "dark"].includes(value)) return false;
            preference = value; apply();
            try { localStorage.setItem(key, value); return true; } catch { return false; }
        }
    };
})();
