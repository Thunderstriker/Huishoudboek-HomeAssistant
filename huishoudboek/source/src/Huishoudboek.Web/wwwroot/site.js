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
