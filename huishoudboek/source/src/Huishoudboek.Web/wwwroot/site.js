window.huishoudboek = {
    markReady: function () { document.documentElement.dataset.interactive = "true"; },
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
