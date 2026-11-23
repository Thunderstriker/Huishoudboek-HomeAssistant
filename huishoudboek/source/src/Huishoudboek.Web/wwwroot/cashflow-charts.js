import "./chart.umd.min.js";

export function create(canvas, text) {
    const money = new Intl.NumberFormat(text.locale, { style: "currency", currency: "EUR" });
    const chart = new Chart(canvas, {
        type: "line",
        data: { labels: [], datasets: [
            { label: text.actual, data: [], showLine: false, pointRadius: 5, backgroundColor: "#32836b" },
            { label: text.forecast, data: [], borderColor: "#32836b", borderDash: [6, 3], pointRadius: 0, borderWidth: 2 },
            { label: text.lowest, data: [], borderColor: "#b18a28", borderDash: [2, 3], pointRadius: 0, borderWidth: 1.5 },
            { label: text.buffer, data: [], borderColor: "#b25173", borderDash: [8, 4], pointRadius: 0, borderWidth: 1.5 },
            { label: text.income, data: [], showLine: false, pointStyle: "triangle", pointRadius: 6, backgroundColor: "#32836b" },
            { label: text.payments, data: [], showLine: false, pointStyle: "rectRot", pointRadius: 5, backgroundColor: "#be6432" }
        ] },
        options: {
            responsive: true, maintainAspectRatio: false, animation: false, locale: text.locale,
            interaction: { mode: "index", intersect: false },
            plugins: { legend: { position: "bottom", labels: { usePointStyle: true, boxWidth: 10, padding: 12 } },
                tooltip: { callbacks: { label: context => {
                    const notes = context.dataset.notes?.[context.dataIndex];
                    if (notes?.length) return `${context.dataset.label}: ${money.format(context.dataset.amounts[context.dataIndex])} · ${notes.join(", ")}`;
                    return `${context.dataset.label}: ${money.format(context.parsed.y)}`;
                } } } },
            scales: { x: { grid: { display: false }, ticks: { maxRotation: 0, maxTicksLimit: 8 } },
                y: { ticks: { maxTicksLimit: 6, callback: value => money.format(value) } } }
        }
    });
    function theme() {
        const dark = document.documentElement.dataset.theme === "dark";
        const ink = dark ? "#bdcec5" : "#607188";
        chart.options.plugins.legend.labels.color = ink;
        chart.options.scales.x.ticks.color = chart.options.scales.y.ticks.color = ink;
        chart.options.scales.y.grid.color = dark ? "#34483e" : "#e2e7dc";
        chart.update("none");
    }
    const observer = new MutationObserver(theme);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    theme();
    return {
        update(data) {
            chart.data.labels = data.labels;
            const sets = chart.data.datasets;
            sets[0].data = data.closing.map((v, i) => i === 0 ? v : null);
            sets[1].data = data.closing;
            sets[2].data = data.lowest;
            sets[3].data = data.labels.map(() => data.buffer);
            sets[4].data = data.income.map((items, i) => items.length ? data.closing[i] : null);
            sets[4].notes = data.income;
            sets[4].amounts = data.incomeAmounts;
            sets[5].data = data.payments.map((items, i) => items.length ? data.closing[i] : null);
            sets[5].notes = data.payments;
            sets[5].amounts = data.paymentAmounts;
            sets[1].segment = { borderColor: ctx => data.lowest[ctx.p1DataIndex] < data.buffer ? "#b25173" : "#32836b" };
            chart.update("none");
        },
        destroy() { observer.disconnect(); chart.destroy(); }
    };
}
