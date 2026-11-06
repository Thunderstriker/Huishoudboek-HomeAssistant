using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class DashboardLayoutTests
{
    [Theory]
    [InlineData("nl-NL", false)]
    [InlineData("en-GB", false)]
    [InlineData("nl-NL", true)]
    [InlineData("en-GB", true)]
    public async Task StartPageShowsAccountBalancesBeforeACompactMonthlyOverview(string culture, bool empty)
    {
        using var language = new CultureScope(culture);
        var data = empty ? HouseholdData.CreateEmpty() : Data();
        using var services = Services(data);
        var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        await services.GetRequiredService<UiPreferences>().LoadAsync();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<Home>()).ToHtmlString()));
        Assert.Contains(UiCulture.Text("DashboardIntro"), html);
        Assert.Contains("href=\"maand\"", html);
        Assert.True(html.IndexOf("id=\"dashboard-accounts-heading\"", StringComparison.Ordinal) <
            html.IndexOf("id=\"dashboard-monthly-figures\"", StringComparison.Ordinal));
        Assert.Contains(UiCulture.Text("DashboardBalancesToday", state.Today.ToString("dd-MM-yyyy")), html);
        Assert.Contains(UiCulture.Text("AvailableNote"), html);
        Assert.Contains("class=\"monthly-charts spending-only\"", html);
        Assert.DoesNotContain(UiCulture.Text("ActualExpected"), html);
        Assert.DoesNotContain("id=\"monthly-figures\"", html); // The original monthly page is separate.
        Assert.DoesNotContain("<table", html);
        Assert.Contains("href=\"transacties?allDates=true\"", html);
        Assert.Contains(UiCulture.Text("DashboardNoRecent"), html);
        Assert.Contains(UiCulture.Text("DashboardNoUpcoming"), html);
        Assert.Equal(empty, html.Contains("href=\"instellen\"", StringComparison.Ordinal));
        Assert.Equal(!empty, html.Contains("data-account-id=", StringComparison.Ordinal));
        Assert.Equal(!empty, html.Contains("id=\"dashboard-attention-heading\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("nl-NL")]
    [InlineData("en-GB")]
    public async Task AccountSelectionUpdatesCardsWithoutFilteringReportsAndSurvivesOwnerChanges(string culture)
    {
        using var language = new CultureScope(culture);
        var data = Data(); var current = data.Accounts[0];
        var savings = current with { Id = Guid.NewGuid(), Name = "Savings", Kind = AccountKind.Savings };
        var otherOwner = new Owner { Name = "Other" }; data.Owners.Add(otherOwner);
        var other = current with { Id = Guid.NewGuid(), Name = "Other account", OwnerIds = [otherOwner.Id] };
        data.Accounts.AddRange([savings, other]);
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        var preferences = services.GetRequiredService<UiPreferences>(); await preferences.LoadAsync();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<Home>());
        async Task<string> Html() => WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        var initial = await Html();
        Assert.Contains($"data-account-id=\"{current.Id}\"", initial);
        Assert.Contains($"data-account-id=\"{other.Id}\"", initial);
        Assert.DoesNotContain($"data-account-id=\"{savings.Id}\"", initial);

        await renderer.Dispatcher.InvokeAsync(() => preferences.SetDashboardAccountsAsync([savings.Id]));
        var selected = await Html();
        Assert.Contains($"data-account-id=\"{savings.Id}\"", selected);
        Assert.DoesNotContain($"data-account-id=\"{current.Id}\"", selected);
        var monthStart = "<section class=\"dashboard-month\"";
        Assert.Equal(initial[initial.IndexOf(monthStart, StringComparison.Ordinal)..], selected[selected.IndexOf(monthStart, StringComparison.Ordinal)..]);

        await renderer.Dispatcher.InvokeAsync(() => preferences.SetDashboardAccountsAsync([current.Id, other.Id]));
        await renderer.Dispatcher.InvokeAsync(() => { state.OwnerId = current.OwnerIds[0]; state.Notify(); });
        var firstOwner = await Html();
        Assert.Contains($"data-account-id=\"{current.Id}\"", firstOwner);
        Assert.DoesNotContain($"data-account-id=\"{other.Id}\"", firstOwner);
        await renderer.Dispatcher.InvokeAsync(() => { state.OwnerId = otherOwner.Id; state.Notify(); });
        var secondOwner = await Html();
        Assert.Contains($"data-account-id=\"{other.Id}\"", secondOwner);
        Assert.DoesNotContain($"data-account-id=\"{current.Id}\"", secondOwner);
        Assert.Equal(new[] { current.Id, other.Id }, preferences.DashboardAccountIds);

        await renderer.Dispatcher.InvokeAsync(() => preferences.SetDashboardAccountsAsync([]));
        var hidden = await Html();
        Assert.DoesNotContain("data-account-id=", hidden);
        Assert.Contains(UiCulture.Text("DashboardNoSelectedAccounts"), hidden);
        await renderer.Dispatcher.InvokeAsync(() => preferences.SetDashboardAccountsAsync(null));
        Assert.Contains($"data-account-id=\"{other.Id}\"", await Html());
    }

    [Fact]
    public async Task FreshEmptyAccountsDoNotProduceAnAttentionPanelAndUnbudgetedSpendingHasAnExplanation()
    {
        var data = Data(); var account = data.Accounts[0]; data.Categories.Clear();
        data.ImportBatches.Add(new() { AccountId = account.Id, CoverageFrom = account.OpeningDate,
            CoverageThrough = DateOnly.FromDateTime(DateTime.Today) });
        using var services = Services(data); await services.GetRequiredService<WebState>().LoadAsync();
        await services.GetRequiredService<UiPreferences>().LoadAsync();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<Home>()).ToHtmlString()));
        Assert.DoesNotContain("id=\"dashboard-attention-heading\"", html);
        Assert.Contains(UiCulture.Text("DashboardNoBudgets"), html);
        Assert.Contains(UiCulture.Text("DashboardRecentImport"), html);
    }

    [Theory]
    [InlineData("nl-NL", false)]
    [InlineData("en-GB", false)]
    [InlineData("nl-NL", true)]
    [InlineData("en-GB", true)]
    public async Task MonthlyOverviewStartsWithFiguresAndKeepsGuidanceOnSeparatePages(string culture, bool empty)
    {
        using var language = new CultureScope(culture);
        using var services = Services(empty ? HouseholdData.CreateEmpty() : Data());
        await services.GetRequiredService<WebState>().LoadAsync();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        async Task<string> Render<T>() where T : IComponent => WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<T>()).ToHtmlString()));

        var dashboard = await Render<Dashboard>();
        var figures = dashboard.IndexOf("id=\"monthly-figures\"", StringComparison.Ordinal);
        Assert.True(figures > dashboard.IndexOf("</h1>", StringComparison.Ordinal));
        Assert.True(figures < dashboard.IndexOf("class=\"monthly-charts\"", StringComparison.Ordinal));
        var beforeFigures = dashboard[..figures];
        Assert.DoesNotContain("<p", beforeFigures);
        Assert.DoesNotContain("<section", beforeFigures);
        Assert.DoesNotContain("id=\"first-use\"", dashboard);
        Assert.DoesNotContain("id=\"weekly-review\"", dashboard);
        Assert.DoesNotContain("id=\"bestedingsruimte\"", dashboard);
        Assert.DoesNotContain("id=\"maandstatus\"", dashboard);
        Assert.DoesNotContain(UiCulture.Text("FirstWeekNote"), dashboard);
        Assert.Contains("href=\"vooruitblik\"", dashboard);
        Assert.Equal(empty, dashboard.Contains("href=\"instellen\"", StringComparison.Ordinal));
        Assert.All(Regex.Matches(dashboard, "<details[^>]*>").Cast<Match>(), match => Assert.DoesNotContain("open", match.Value));
        Assert.Contains("<details><summary>" + UiCulture.Text("CalculationDetails"), dashboard);
        Assert.Contains("<details><summary>" + UiCulture.Text("BudgetDetails"), dashboard);

        var setup = await Render<Setup>();
        Assert.Contains(UiCulture.Text("SetupTitle"), setup);
        Assert.Contains("id=\"first-use\"", setup);
        Assert.DoesNotContain("id=\"monthly-figures\"", setup);
        if (empty) Assert.Contains("href=\"beheer#database\"", setup);

        var outlook = await Render<Outlook>();
        Assert.Contains(UiCulture.Text("FinancialOutlook"), outlook);
        Assert.Contains("id=\"maandstatus\"", outlook);
        Assert.Contains("id=\"bestedingsruimte\"", outlook);
        Assert.DoesNotContain("id=\"first-use\"", outlook);
        Assert.DoesNotContain("id=\"weekly-review\"", outlook);

        var weekly = await Render<WeeklyCheck>();
        Assert.Contains(UiCulture.Text("WeeklyReviewTitle"), weekly);
        Assert.Contains(UiCulture.Text("HideWeeklyReview"), weekly);
        Assert.Equal(!empty, weekly.Contains("id=\"weekly-review\"", StringComparison.Ordinal));
        Assert.DoesNotContain("id=\"first-use\"", weekly);
        if (!empty) Assert.Contains("<details open", weekly);
    }

    [Theory]
    [InlineData("nl-NL")]
    [InlineData("en-GB")]
    public async Task WarningsLinkFromAffectedFiguresToTheDetailedOutlook(string culture)
    {
        using var language = new CultureScope(culture); var data = Data();
        var today = DateOnly.FromDateTime(DateTime.Today);
        data.Transactions.Add(new Transaction { AccountId = data.Accounts[0].Id, CategoryId = data.Categories[0].Id,
            Date = today, Amount = -200, Description = "Groceries" });
        using var services = Services(data); await services.GetRequiredService<WebState>().LoadAsync();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<Dashboard>()).ToHtmlString()));
        Assert.Contains("href=\"vooruitblik#maandstatus\">" + UiCulture.Text("SpendingAboveIncomeBadge"), html);
        Assert.Contains("href=\"vooruitblik#maandstatus\">" + UiCulture.Text("OverBudgetBadge"), html);
        Assert.Contains("href=\"vooruitblik#maandstatus\">" + UiCulture.Text("ForecastShortfallBadge"), html);
        Assert.DoesNotContain(UiCulture.Text("SpendingAboveIncome", WebState.Money(200)), html);
    }

    [Theory]
    [InlineData(typeof(Home), "/")]
    [InlineData(typeof(Dashboard), "/maand")]
    [InlineData(typeof(Setup), "/instellen")]
    [InlineData(typeof(Outlook), "/vooruitblik")]
    [InlineData(typeof(WeeklyCheck), "/weekcontrole")]
    [InlineData(typeof(CategoryTrends), "/categorietrends")]
    public void SupportingPagesHaveDedicatedAuthorizedRoutes(Type page, string route)
    {
        Assert.Contains(page.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>(), a => a.Template == route);
        Assert.NotEmpty(page.GetCustomAttributes(typeof(AuthorizeAttribute), true));
    }

    [Theory]
    [InlineData("nl-NL", false)]
    [InlineData("en-GB", false)]
    [InlineData("nl-NL", true)]
    [InlineData("en-GB", true)]
    public async Task CategoryHistoryRendersLocalizedAccessibleChartsAndExactAmounts(string culture, bool empty)
    {
        using var language = new CultureScope(culture);
        var data = empty ? HouseholdData.CreateEmpty() : Data();
        if (!empty)
        {
            data.Categories.Add(new() { Name = "Savings only", Purpose = CategoryPurpose.Savings });
            data.Transactions.Add(new() { AccountId = data.Accounts[0].Id, CategoryId = data.Categories[0].Id,
                Date = DateOnly.FromDateTime(DateTime.Today).AddMonths(-1), Amount = -123.45m, Description = "Private bank text" });
        }
        using var services = Services(data); await services.GetRequiredService<WebState>().LoadAsync();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<CategoryTrends>()).ToHtmlString()));
        Assert.Contains(UiCulture.Text("HistoryIntro"), html);
        Assert.Contains(UiCulture.Text("HistoryFilterNote"), html);
        Assert.Contains(UiCulture.Text("HistoryExactAmounts"), html);
        Assert.Contains(UiCulture.Text("HistoryMovingAverage"), html);
        Assert.Contains(UiCulture.Text("HistoryHeatmap"), html);
        Assert.Equal(2, Regex.Matches(html, "<canvas ").Count);
        Assert.Equal(2, Regex.Matches(html, "aria-describedby=\"history-method\"").Count);
        Assert.Equal(24, Regex.Matches(html, "tabindex=\"0\" role=\"img\"").Count);
        Assert.Contains("scope=\"col\"", html); Assert.Contains("scope=\"row\"", html);
        Assert.Contains("aria-pressed=\"true\"", html);
        Assert.DoesNotContain("Private bank text", html); Assert.DoesNotContain("Savings only", html);
        Assert.DoesNotContain(UiCulture.Text("HistoryInvalidRange"), html);
        Assert.Equal(empty, html.Contains(UiCulture.Text("HistoryEmptyTitle"), StringComparison.Ordinal));
        if (!empty)
        {
            Assert.Contains(WebState.Money(123.45m), html);
            Assert.Matches("id=\"history-chart-title\"[^>]*>Groceries</h2>", html);
        }
    }

    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Owner" };
        return new HouseholdData { Owners = [owner], Accounts = [new Account { Name = "Current", OwnerIds = [owner.Id],
            OpeningDate = DateOnly.FromDateTime(DateTime.Today).AddMonths(-1), OpeningBalance = 1000 }],
            Categories = [new Category { Name = "Groceries", MonthlyBudget = 100 }] };
    }

    private static ServiceProvider Services(HouseholdData data)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization();
        services.AddSingleton<IWebAdministration>(new Backend(data)); services.AddSingleton<WebState>();
        services.AddSingleton<IJSRuntime>(new StaticJs()); services.AddSingleton<UiPreferences>();
        return services.BuildServiceProvider();
    }

    private sealed class Backend(HouseholdData data) : IWebAdministration
    {
        public Task<AdministrationSnapshot> ReadAsync() => Task.FromResult(new AdministrationSnapshot("test", data));
        public Task<AdministrationSnapshot> UpdateAsync(string revision, Action<HouseholdData> change) => throw new NotSupportedException();
        public Task<UploadResponse> UploadAsync(Stream source, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PreviewResponse> PreviewAsync(PreviewRequest request, string revision) => throw new NotSupportedException();
        public Task<PreviewRow> AdjustAsync(AdjustImportRequest request) => throw new NotSupportedException();
        public Task<CommitImportResponse> CommitAsync(CommitImportRequest request, string revision) => throw new NotSupportedException();
        public Task<string> RestoreAsync(Stream source, string revision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StaticJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            object? result = identifier switch {
                "huishoudboek.preferences.readWeeklyReviewHidden" => (bool?)false,
                "huishoudboek.preferences.readDashboardAccounts" => new DashboardAccountsPreference(true, null),
                "huishoudboek.preferences.writeDashboardAccounts" => true,
                _ => throw new NotSupportedException(identifier)
            };
            return ValueTask.FromResult((TValue)result!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture, uiCulture = CultureInfo.CurrentUICulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
    }
}
