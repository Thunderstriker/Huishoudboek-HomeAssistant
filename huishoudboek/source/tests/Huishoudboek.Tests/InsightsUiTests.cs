using System.Globalization;
using System.Net;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class InsightsUiTests
{
    [Theory]
    [InlineData("nl-NL", false)]
    [InlineData("en-GB", false)]
    [InlineData("nl-NL", true)]
    [InlineData("en-GB", true)]
    public async Task InsightsAndDashboardRenderAccessibleLocalizedVisualsIncludingEmptyStates(string culture, bool empty)
    {
        using var language = new CultureScope(culture);
        var data = empty ? HouseholdData.CreateEmpty() : FinancialInsightsTests.Data(asOf: DateOnly.FromDateTime(DateTime.Today));
        using var services = Services(data); await services.GetRequiredService<WebState>().LoadAsync();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        async Task<string> Render<T>() where T : IComponent => WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<T>()).ToHtmlString()));
        var html = await Render<Insights>();
        Assert.Contains(UiCulture.Text("FreedomTitle"), html);
        Assert.Contains(UiCulture.Text("FingerprintTitle"), html);
        Assert.Contains(UiCulture.Text("FreedomEssentialsHeadline"), html);
        Assert.Contains(UiCulture.Text("FreedomNotIndependence"), html);
        Assert.Contains("<svg", html); Assert.Contains("role=\"img\"", html);
        Assert.Contains("aria-pressed=\"true\"", html);
        Assert.Contains("href=\"budgetten\"", html);
        Assert.Contains(UiCulture.Text(empty ? "InsightsNeedHistory" : "InsightsExactMonths"), html);
        Assert.DoesNotContain("NaN", html); Assert.DoesNotContain("Infinity", html);
        Assert.DoesNotContain("https://", html); // SVGs work entirely locally without a graph service.
        var dashboard = await Render<Home>();
        Assert.Contains("href=\"inzichten\"", dashboard);
        Assert.Contains(UiCulture.Text("FreedomTitle"), dashboard);
        Assert.DoesNotContain(UiCulture.Text("FingerprintTitle"), dashboard);
        Assert.DoesNotContain("<table", dashboard);
    }

    [Theory]
    [InlineData("nl-NL")]
    [InlineData("en-GB")]
    public async Task EditorsPersistCategoryFlagsEarmarksAccountSelectionAndTargetWithoutChangingBankEntries(string culture)
    {
        using var language = new CultureScope(culture);
        var data = FinancialInsightsTests.Data(); var entries = data.Transactions.Select(t => t with { }).ToArray();
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        var category = EditorDraft.Category(state, data.Categories[1]);
        Assert.Contains(UiCulture.Text("CategoryInsightHelp"), category.Note);
        category.Fields.Single(f => f.Key == "exceptional").Value = "True"; category.Apply(data);
        Assert.True(data.Categories[1].IsExceptional);
        category.Fields.Single(f => f.Key == "purpose").Value = "Savings";
        Assert.False(category.IsFieldVisible(category.Fields.Single(f => f.Key == "exceptional")));
        category.Apply(data); Assert.False(data.Categories[1].IsExceptional);
        var account = EditorDraft.Account(state, data.Accounts[1]);
        account.Fields.Single(f => f.Key == "freedom").Value = "False";
        account.Fields.Single(f => f.Key == "earmarked").Value = 1234.56m.ToString("0.00", UiCulture.Culture);
        account.Apply(data);
        Assert.False(data.Accounts[1].IncludeInFreedom); Assert.Equal(1234.56m, data.Accounts[1].EarmarkedAmount);
        var target = EditorDraft.FreedomTarget(state); target.Fields.Single(f => f.Key == "months").Value = "9";
        target.Apply(data); Assert.Equal(9, data.InsightSettings.BufferTargetMonths);
        Validation.Validate(data); Assert.Equal(entries, data.Transactions);
    }

    [Fact]
    public async Task ChangingTheOwnerFilterRebuildsTheInsightsWithoutARefresh()
    {
        var data = FinancialInsightsTests.Data(asOf: DateOnly.FromDateTime(DateTime.Today));
        var owner = data.Owners[0]; var other = new Owner { Name = "Other" }; data.Owners.Add(other);
        data.Accounts[1].OwnerIds = [other.Id];
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<Insights>());
        var initial = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains(WebState.Money(19800m), initial);
        await renderer.Dispatcher.InvokeAsync(() => { state.OwnerId = owner.Id; state.Notify(); });
        var filtered = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains(WebState.Money(14200m), filtered);
        Assert.DoesNotContain(WebState.Money(19800m), filtered);
    }

    private static ServiceProvider Services(HouseholdData data)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization();
        services.AddSingleton<IWebAdministration>(new Backend(data)); services.AddSingleton<WebState>();
        services.AddSingleton<IJSRuntime>(new NoJs()); services.AddSingleton<UiPreferences>();
        return services.BuildServiceProvider();
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => new(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => new(default(TValue)!);
    }

    private sealed class Backend(HouseholdData data) : IWebAdministration
    {
        public Task<AdministrationSnapshot> ReadAsync() => Task.FromResult(new AdministrationSnapshot("test", data));
        public Task<AdministrationSnapshot> UpdateAsync(string revision, Action<HouseholdData> change) { change(data); return ReadAsync(); }
        public Task<UploadResponse> UploadAsync(Stream source, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PreviewResponse> PreviewAsync(PreviewRequest request, string revision) => throw new NotSupportedException();
        public Task<PreviewRow> AdjustAsync(AdjustImportRequest request) => throw new NotSupportedException();
        public Task<CommitImportResponse> CommitAsync(CommitImportRequest request, string revision) => throw new NotSupportedException();
        public Task<string> RestoreAsync(Stream source, string revision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture, uiCulture = CultureInfo.CurrentUICulture;
        public CultureScope(string name) { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name); }
        public void Dispose() { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
    }
}
