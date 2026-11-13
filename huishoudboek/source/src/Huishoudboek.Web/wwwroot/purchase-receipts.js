const previews = new Set();

export function preview(inputId) {
    const file = document.getElementById(inputId)?.files?.[0];
    if (!file) return null;
    const url = URL.createObjectURL(file);
    previews.add(url);
    return url;
}

export function release(url) {
    URL.revokeObjectURL(url);
    previews.delete(url);
}

window.addEventListener("pagehide", () => {
    for (const url of previews) URL.revokeObjectURL(url);
    previews.clear();
});
