import "./chart.umd.min.js";

const money = new Intl.NumberFormat("nl-NL", { style: "currency", currency: "EUR" });

export function create(spendingCanvas, forecastCanvas) {
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
                    return `${context.label}: ${money.format(context.parsed)} (${new Intl.NumberFormat("nl-NL", { style: "percent", maximumFractionDigits: 0 }).format(share)})`;
                } } }
            }
        }
    });
    const forecastChart = new Chart(forecastCanvas, {
        type: "bar",
        data: {
            labels: ["Inkomsten", "Lasten", "Over"],
            datasets: [
                { label: "Werkelijk", data: [], backgroundColor: "#087f75", borderRadius: 3, maxBarThickness: 24 },
                { label: "Prognose", data: [], backgroundColor: "#d7e5ef", borderColor: "#5c86ab", borderWidth: 1, borderRadius: 3, maxBarThickness: 24 }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            locale: "nl-NL",
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
    return DotNet.createJSObjectReference({
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
    });
}

export function createYear(trendCanvas, spendingCanvas, savingsCanvas) {
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
        locale: "nl-NL",
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
            { type: "line", label: "Inkomsten", data: [], borderColor: "#087f75", backgroundColor: "#087f75", borderWidth: 2, pointRadius: 3, tension: 0.2 },
            { label: "Lasten", data: [], backgroundColor: "#d8a483", hoverBackgroundColor: "#be6432", borderRadius: 4, maxBarThickness: 26 }
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
            { type: "line", label: "Opzijgezet sinds januari", data: [], borderColor: "#526dc1", backgroundColor: "#526dc1", borderWidth: 2, pointRadius: 3, tension: 0.2 },
            { label: "Netto opzijgezet per maand", data: [], backgroundColor: "#bfcbea", borderRadius: 3, maxBarThickness: 22 }
        ] },
        options
    });
    return DotNet.createJSObjectReference({
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
    });
}
