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
    [InlineData(typeof(Setup), "/instellen")]
    [InlineData(typeof(Outlook), "/vooruitblik")]
    [InlineData(typeof(WeeklyCheck), "/weekcontrole")]
    public void SupportingPagesHaveDedicatedAuthorizedRoutes(Type page, string route)
    {
        Assert.Contains(page.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>(), a => a.Template == route);
        Assert.NotEmpty(page.GetCustomAttributes(typeof(AuthorizeAttribute), true));
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
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture, uiCulture = CultureInfo.CurrentUICulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
    }
}
