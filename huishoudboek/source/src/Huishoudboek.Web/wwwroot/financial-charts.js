import "./chart.umd.min.js";

// Blazor wraps these return values when C# requests IJSObjectReference; return plain objects.
export function create(spendingCanvas, forecastCanvas, text) {
    const money = new Intl.NumberFormat(text.locale, { style: "currency", currency: "EUR" });
    const percent = new Intl.NumberFormat(text.locale, { style: "percent", maximumFractionDigits: 0 });
    const spendingChart = new Chart(spendingCanvas, {
        type: "doughnut",
        data: { labels: [], datasets: [{ data: [], borderWidth: 2, borderColor: "#fff" }] },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            cutout: "74%",
            plugins: {
                legend: { display: false },
                tooltip: { callbacks: { label(context) {
                    const total = context.dataset.data.reduce((sum, value) => sum + value, 0);
                    const share = total ? context.parsed / total : 0;
                    return `${context.label}: ${money.format(context.parsed)} (${percent.format(share)})`;
                } } }
            }
        }
    });
    const forecastChart = new Chart(forecastCanvas, {
        type: "bar",
        data: {
            labels: [text.income, text.outflow, text.available],
            datasets: [
                { label: text.actual, data: [], backgroundColor: "#087f75", borderRadius: 3, maxBarThickness: 24 },
                { label: text.forecast, data: [], backgroundColor: "#d7e5ef", borderColor: "#5c86ab", borderWidth: 1, borderRadius: 3, maxBarThickness: 24 }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            locale: text.locale,
            plugins: {
                legend: { position: "bottom", labels: { boxWidth: 10, boxHeight: 10, padding: 14 } },
                tooltip: { callbacks: { label: context => `${context.dataset.label}: ${money.format(context.parsed.y)}` } }
            },
            scales: {
                x: { grid: { display: false }, ticks: { color: "#607188", maxRotation: 0 } },
                y: {
                    beginAtZero: true,
                    ticks: { maxTicksLimit: 5, color: "#607188", callback: value => money.format(value) },
                    grid: { color: context => context.tick.value === 0 ? "#607188" : "#edf2f7" }
                }
            }
        }
    });
    return {
        update(data) {
            spendingChart.data.labels = data.labels;
            spendingChart.data.datasets[0].data = data.spending;
            spendingChart.data.datasets[0].backgroundColor = data.colors;
            forecastChart.data.datasets[0].data = data.actual;
            forecastChart.data.datasets[1].data = data.forecast;
            spendingChart.update("none");
            forecastChart.update("none");
        },
        destroy() {
            spendingChart.destroy();
            forecastChart.destroy();
        }
    };
}

export function createYear(trendCanvas, spendingCanvas, savingsCanvas, text) {
    const money = new Intl.NumberFormat(text.locale, { style: "currency", currency: "EUR" });
    const axis = {
        beginAtZero: true,
        ticks: { maxTicksLimit: 5, color: "#607188", callback: value => money.format(value) },
        grid: { color: context => context.tick.value === 0 ? "#9cabb9" : "#edf2f7" },
        border: { display: false }
    };
    const options = {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        locale: text.locale,
        interaction: { mode: "index", intersect: false },
        plugins: {
            legend: { position: "bottom", labels: { usePointStyle: true, boxWidth: 8, boxHeight: 8, padding: 18 } },
            tooltip: { callbacks: { label: context => `${context.dataset.label}: ${money.format(context.parsed.y)}` } }
        },
        scales: {
            x: { grid: { display: false }, border: { display: false }, ticks: { color: "#607188", maxRotation: 0 } },
            y: axis
        }
    };
    const trend = new Chart(trendCanvas, {
        type: "bar",
        data: { labels: [], datasets: [
            { type: "line", label: text.income, data: [], borderColor: "#087f75", backgroundColor: "#087f75", borderWidth: 2, pointRadius: 3, tension: 0.2 },
            { label: text.outflow, data: [], backgroundColor: "#d8a483", hoverBackgroundColor: "#be6432", borderRadius: 4, maxBarThickness: 26 }
        ] },
        options
    });
    const spending = new Chart(spendingCanvas, {
        type: "doughnut",
        data: { labels: [], datasets: [{ data: [], borderWidth: 2, borderColor: "#fff" }] },
        options: {
            responsive: true, maintainAspectRatio: false, animation: false, cutout: "74%",
            plugins: { legend: { display: false }, tooltip: { callbacks: { label: context => `${context.label}: ${money.format(context.parsed)}` } } }
        }
    });
    const savings = new Chart(savingsCanvas, {
        type: "bar",
        data: { labels: [], datasets: [
            { type: "line", label: text.cumulativeSavings, data: [], borderColor: "#526dc1", backgroundColor: "#526dc1", borderWidth: 2, pointRadius: 3, tension: 0.2 },
            { label: text.monthlySavings, data: [], backgroundColor: "#bfcbea", borderRadius: 3, maxBarThickness: 22 }
        ] },
        options
    });
    return {
        update(data) {
            trend.data.labels = savings.data.labels = data.months;
            trend.data.datasets[0].data = data.income;
            trend.data.datasets[1].data = data.outflow;
            savings.data.datasets[0].data = data.cumulativeSavings;
            savings.data.datasets[1].data = data.savings;
            spending.data.labels = data.categories;
            spending.data.datasets[0].data = data.spending;
            spending.data.datasets[0].backgroundColor = data.colors;
            trend.update("none");
            spending.update("none");
            savings.update("none");
        },
        destroy() {
            trend.destroy();
            spending.destroy();
            savings.destroy();
        }
    };
}
