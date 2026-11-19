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

// Native auto popovers provide one-open-at-a-time behavior, outside-click dismissal
// and a top layer that cannot be clipped by the table's overflow container.
(function () {
    const selector = ".row-actions-menu";
    const items = menu => Array.from(menu.querySelectorAll('[role="menuitem"]:not(:disabled)'));
    const trigger = menu => menu.previousElementSibling;

    function position(menu) {
        const anchor = trigger(menu).getBoundingClientRect();
        const padding = 8, gap = 4;
        const width = document.documentElement.clientWidth, height = window.innerHeight;
        menu.style.maxHeight = Math.max(0, height - padding * 2) + "px";
        const bounds = menu.getBoundingClientRect();
        const top = anchor.bottom + gap + bounds.height <= height - padding
            ? anchor.bottom + gap : anchor.top - gap - bounds.height;
        menu.style.left = Math.max(padding, Math.min(anchor.right - bounds.width, width - bounds.width - padding)) + "px";
        menu.style.top = Math.max(padding, Math.min(top, height - bounds.height - padding)) + "px";
    }

    document.addEventListener("beforetoggle", function (event) {
        if (event.target.matches(selector)) {
            trigger(event.target).setAttribute("aria-expanded", String(event.newState === "open"));
        }
    }, true);

    document.addEventListener("toggle", function (event) {
        const menu = event.target;
        if (!menu.matches(selector) || !menu.matches(":popover-open")) return;
        position(menu);
        if (!menu.contains(document.activeElement)) {
            menu.scrollTop = 0;
            items(menu)[0]?.focus({ preventScroll: true });
        }
    }, true);

    document.addEventListener("click", function (event) {
        const menu = event.target.closest(selector);
        if (menu && event.target.closest('[role="menuitem"]')) menu.hidePopover();
    });

    document.addEventListener("keydown", function (event) {
        const button = event.target.closest(".row-actions-toggle");
        if (button && (event.key === "ArrowDown" || event.key === "ArrowUp")) {
            event.preventDefault();
            const menu = document.getElementById(button.getAttribute("popovertarget"));
            menu.showPopover();
            position(menu);
            const choices = items(menu);
            choices[event.key === "ArrowUp" ? choices.length - 1 : 0]?.focus({ preventScroll: true });
            menu.scrollTop = event.key === "ArrowUp" ? menu.scrollHeight : 0;
            return;
        }
        const menu = event.target.closest(selector);
        if (!menu) return;
        if (event.key === " " && event.target.matches('a[role="menuitem"]')) {
            event.preventDefault();
            event.target.click();
            return;
        }
        if (event.key === "Escape" || event.key === "Tab") {
            if (event.key === "Escape") event.preventDefault();
            menu.hidePopover();
            trigger(menu).focus({ preventScroll: true });
            return;
        }
        const choices = items(menu), index = choices.indexOf(document.activeElement);
        let next;
        if (event.key === "ArrowDown") next = (index + 1) % choices.length;
        else if (event.key === "ArrowUp") next = (index - 1 + choices.length) % choices.length;
        else if (event.key === "Home") next = 0;
        else if (event.key === "End") next = choices.length - 1;
        else return;
        event.preventDefault();
        choices[next]?.focus();
    });

    function updateMenus(event) {
        // Keep the menu attached to its row, including a queued scroll from focusing
        // the trigger. Scrolling inside a long menu must not reposition or dismiss it.
        if (event.target instanceof Element && event.target.closest(selector)) return;
        document.querySelectorAll(selector + ":popover-open").forEach(menu => {
            const anchor = trigger(menu).getBoundingClientRect();
            if (event.type === "resize" || anchor.bottom <= 0 || anchor.top >= window.innerHeight ||
                anchor.right <= 0 || anchor.left >= document.documentElement.clientWidth) menu.hidePopover();
            else position(menu);
        });
    }
    document.addEventListener("scroll", updateMenus, true);
    window.addEventListener("resize", updateMenus);
})();
