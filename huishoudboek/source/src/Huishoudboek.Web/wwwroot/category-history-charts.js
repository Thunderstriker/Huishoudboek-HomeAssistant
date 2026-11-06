import "./chart.umd.min.js";

// All chart data stays local. Return a plain object for Blazor's IJSObjectReference.
export function createHistory(trendCanvas, comparisonCanvas, text) {
    const money = new Intl.NumberFormat(text.locale, { style: "currency", currency: "EUR", maximumFractionDigits: 2 });
    const compact = new Intl.NumberFormat(text.locale, { style: "currency", currency: "EUR", notation: "compact", maximumFractionDigits: 1 });
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
    const animation = () => reduceMotion.matches ? false : { duration: 650, easing: "easeOutQuart" };
    let payload = null;
    let signature = "";

    function gradient(context, top, bottom) {
        const { ctx, chartArea } = context.chart;
        if (!chartArea) return top;
        const fill = ctx.createLinearGradient(0, chartArea.top, 0, chartArea.bottom);
        fill.addColorStop(0, top);
        fill.addColorStop(1, bottom);
        return fill;
    }

    const crosshair = {
        id: "historyCrosshair",
        afterDatasetsDraw(chart) {
            const active = chart.getActiveElements();
            if (!active.length) return;
            const { ctx, chartArea } = chart;
            const x = active[0].element.x;
            ctx.save();
            ctx.beginPath();
            ctx.setLineDash([3, 5]);
            ctx.moveTo(x, chartArea.top);
            ctx.lineTo(x, chartArea.bottom);
            ctx.strokeStyle = "#a7bdd359";
            ctx.lineWidth = 1;
            ctx.stroke();
            ctx.restore();
        }
    };

    function options(dark) {
        return {
            responsive: true, maintainAspectRatio: false, locale: text.locale,
            animation: animation(),
            interaction: { mode: "index", intersect: false },
            layout: { padding: { top: 12, right: 8 } },
            plugins: {
                legend: { display: false },
                tooltip: {
                    backgroundColor: dark ? "#edf6ff" : "#14283e",
                    titleColor: dark ? "#14283e" : "#f0f7ff",
                    bodyColor: dark ? "#354c65" : "#d0dfed",
                    padding: 14, cornerRadius: 10, boxPadding: 5, usePointStyle: true,
                    callbacks: { label: context => `${context.dataset.label}: ${money.format(context.parsed.y)}` }
                }
            },
            scales: {
                x: { grid: { display: false }, border: { display: false },
                    ticks: { color: dark ? "#a7bdd3" : "#607188", maxRotation: 0, autoSkip: true, maxTicksLimit: 12, font: { size: 11 } } },
                y: { beginAtZero: true, border: { display: false },
                    ticks: { color: dark ? "#a7bdd3" : "#607188", maxTicksLimit: 5, padding: 10, callback: value => compact.format(value), font: { size: 11 } },
                    grid: { color: context => context.tick.value === 0 ? (dark ? "#a7bdd34d" : "#c5d2df") : (dark ? "#a7bdd312" : "#edf2f7"), drawTicks: false } }
            }
        };
    }

    const trendOptions = options(true);
    trendOptions.plugins.tooltip.callbacks.title = items => payload?.fullMonths[items[0]?.dataIndex] ?? "";
    trendOptions.plugins.tooltip.callbacks.afterBody = items => {
        const index = items[0]?.dataIndex;
        return [payload?.partial[index] ? text.partial : null, payload && !payload.covered[index] ? text.unconfirmed : null].filter(Boolean);
    };
    const trend = new Chart(trendCanvas, {
        type: "bar", plugins: [crosshair],
        data: { labels: [], datasets: [
            { type: "line", label: text.movingAverage, data: [], borderColor: "#b4a1ff", borderWidth: 3,
                backgroundColor: context => gradient(context, "#ac94ff26", "#ac94ff00"),
                fill: true, tension: .35, cubicInterpolationMode: "monotone", spanGaps: false,
                pointRadius: 0, pointHoverRadius: 5, pointHoverBackgroundColor: "#e4dbff", pointHoverBorderColor: "#b4a1ff", order: 0 },
            { label: text.spending, data: [],
                backgroundColor: context => {
                    if (payload?.partial[context.dataIndex]) return "#7dddc077";
                    return payload?.spending[context.dataIndex] < 0
                        ? gradient(context, "#c3afff", "#8161bb") : gradient(context, "#7cebd0", "#159e8a");
                },
                hoverBackgroundColor: "#d3fff2", borderColor: "#edc789",
                borderWidth: context => payload?.partial[context.dataIndex] ? 2 : 0,
                borderSkipped: false, borderRadius: 5, maxBarThickness: 30, categoryPercentage: .72, order: 1 }
        ] },
        options: trendOptions
    });

    const comparisonOptions = options(false);
    comparisonOptions.plugins.tooltip.callbacks.label = context => {
        const dates = context.datasetIndex === 0 ? payload?.previousMonths : payload?.latestMonths;
        return `${dates?.[context.dataIndex] ?? context.dataset.label}: ${money.format(context.parsed.y)}`;
    };
    const comparison = new Chart(comparisonCanvas, {
        type: "line",
        data: { labels: [], datasets: [
            { label: text.previous, data: [], borderColor: "#9cabbf", backgroundColor: "#9cabbf", borderDash: [5, 5],
                borderWidth: 2, pointRadius: 3, pointHoverRadius: 5, tension: .35, cubicInterpolationMode: "monotone" },
            { label: text.latest, data: [], borderColor: "#7956bd", borderWidth: 3,
                backgroundColor: context => gradient(context, "#9975da33", "#9975da00"),
                pointBackgroundColor: "#7956bd", pointBorderColor: "#fff", pointBorderWidth: 2,
                pointRadius: 4, pointHoverRadius: 6, tension: .35, cubicInterpolationMode: "monotone", fill: true }
        ] },
        options: comparisonOptions
    });

    return {
        update(data) {
            const next = JSON.stringify(data);
            if (next === signature) return;
            signature = next;
            payload = data;
            trend.data.labels = data.months;
            trend.data.datasets[0].data = data.average;
            trend.data.datasets[1].data = data.spending;
            comparison.data.labels = data.comparisonMonths ?? [];
            comparison.data.datasets[0].data = data.previous ?? [];
            comparison.data.datasets[1].data = data.latest ?? [];
            trend.options.animation = comparison.options.animation = animation();
            trend.update();
            comparison.update();
            // A previously hidden empty-state canvas can become visible after a filter change.
            trend.resize();
            comparison.resize();
        },
        destroy() { trend.destroy(); comparison.destroy(); }
    };
}
