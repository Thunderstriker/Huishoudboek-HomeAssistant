window.huishoudboek = {
    markReady: function () { document.documentElement.dataset.interactive = "true"; },
    preferences: {
        key: function () { return "huishoudboek:" + new URL(document.baseURI).pathname + ":hide-weekly-review"; },
        readWeeklyReviewHidden: function () {
            try { return localStorage.getItem(this.key()) === "true"; }
            catch { return null; /* Storage can be blocked in a private window or iframe. */ }
        },
        writeWeeklyReviewHidden: function (hidden) {
            try { localStorage.setItem(this.key(), String(hidden)); return true; }
            catch { return false; /* Keep the preference for this session instead. */ }
        },
        dashboardKey: function () { return "huishoudboek:" + new URL(document.baseURI).pathname + ":dashboard-accounts"; },
        readDashboardAccounts: function () {
            let value;
            try { value = localStorage.getItem(this.dashboardKey()); }
            catch { return { available: false, accountIds: null }; }
            try {
                const ids = value === null ? null : JSON.parse(value);
                return { available: true, accountIds: Array.isArray(ids) && ids.every(id => typeof id === "string") ? ids : null };
            } catch { return { available: true, accountIds: null }; }
        },
        writeDashboardAccounts: function (ids) {
            try {
                if (ids === null) localStorage.removeItem(this.dashboardKey());
                else localStorage.setItem(this.dashboardKey(), JSON.stringify(ids));
                return true;
            } catch { return false; }
        }
    },
    openDialog: function (element, reference) {
        element.addEventListener("cancel", function (event) {
            event.preventDefault();
            reference.invokeMethodAsync("CancelDialog").catch(function () { /* Component was already closed. */ });
        });
        if (!element.open) element.showModal();
        const field = element.querySelector('[autofocus]') || element.querySelector('input, select, button');
        if (field) field.focus();
    }
};
