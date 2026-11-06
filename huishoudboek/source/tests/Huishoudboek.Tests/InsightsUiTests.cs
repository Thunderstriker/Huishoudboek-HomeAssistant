using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Huishoudboek.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
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

    [Theory]
    [InlineData("nl-NL")]
    [InlineData("en-GB")]
    public async Task ConfirmingSavingsThroughTheEditorUpdatesOpenInsightsWithoutImportingOrRefreshing(string culture)
    {
        using var language = new CultureScope(culture);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var data = FinancialInsightsTests.Data(asOf: today); var savings = data.Accounts[1];
        data.ImportBatches.RemoveAll(b => b.AccountId == savings.Id);
        var bank = JsonSerializer.Serialize(data.Transactions); var imports = JsonSerializer.Serialize(data.ImportBatches);
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var insights = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Insights>());
        var accounts = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Accounts>());
        var initial = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(insights.ToHtmlString));
        var previousMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var gap = UiCulture.Text("InsightsAccountGap", savings.Name, previousMonth.ToString("dd-MM-yyyy"), previousMonth.AddMonths(1).AddDays(-1).ToString("dd-MM-yyyy"));
        Assert.Contains(gap, initial);
        Assert.Contains($"rekeningen#statement-periods-{savings.Id}", initial);
        Assert.Contains(UiCulture.Text("FreedomNotCalculated"), initial);
        var accountHtml = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(accounts.ToHtmlString));
        Assert.Contains(UiCulture.Text("AccountStatementPeriods"), accountHtml);
        Assert.Contains(UiCulture.Text("ConfirmAccountPeriod"), accountHtml);

        var capture = services.GetRequiredService<EditorCapture>(); capture.Draft = EditorDraft.AccountCoverage(state, savings);
        var editor = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CoverageEditorProbe>());
        var editorHtml = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(editor.ToHtmlString));
        Assert.Contains(UiCulture.Text("AccountPeriodConsent"), editorHtml);
        Assert.Contains(UiCulture.Text("ConfirmAccountPeriod"), editorHtml);
        Assert.Empty(data.AccountStatementPeriods); // Opening the form saves nothing.
        await renderer.Dispatcher.InvokeAsync(() => InvokeEditor(capture.Editor, "Save"));
        Assert.False(capture.Closed); Assert.Empty(data.AccountStatementPeriods);
        Assert.Contains(UiCulture.Text("AccountPeriodConsentRequired"), WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(editor.ToHtmlString)));

        capture.Draft.Fields.Single(f => f.Key == "complete").Value = "True";
        await renderer.Dispatcher.InvokeAsync(() => InvokeEditor(capture.Editor, "Save"));
        Assert.True(capture.Closed); Assert.Single(data.AccountStatementPeriods);
        Assert.Equal(UiCulture.Text("StatementPeriodSaved"), state.Message);
        var updated = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(insights.ToHtmlString));
        Assert.DoesNotContain(gap, updated);
        Assert.DoesNotContain(UiCulture.Text("FreedomNotCalculated"), updated);
        Assert.DoesNotContain(UiCulture.Text("InsightsSetupTitle"), updated);
        Assert.Contains(UiCulture.Text("InsightsPeriodNote", today.ToString("dd-MM-yyyy"), 6), updated);
        Assert.Contains(UiCulture.Text("AccountPeriodConfirmed"), WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(accounts.ToHtmlString)));
        Assert.Equal(bank, JsonSerializer.Serialize(data.Transactions)); Assert.Equal(imports, JsonSerializer.Serialize(data.ImportBatches));

        await renderer.Dispatcher.InvokeAsync(() => state.SaveAsync(d => d.AccountStatementPeriods.Clear()));
        Assert.Contains(gap, WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(insights.ToHtmlString)));
    }

    [Theory]
    [InlineData("nl-NL")]
    [InlineData("en-GB")]
    public async Task AccountPeriodEditorRejectsInvalidDatesAndCancellationChangesNothing(string culture)
    {
        using var language = new CultureScope(culture);
        var today = DateOnly.FromDateTime(DateTime.Today); var data = FinancialInsightsTests.Data(asOf: today);
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        var before = JsonSerializer.Serialize(data); var account = data.Accounts[1];
        var capture = services.GetRequiredService<EditorCapture>(); capture.Draft = EditorDraft.AccountCoverage(state, account);
        capture.Draft.Fields.Single(f => f.Key == "complete").Value = "True";
        foreach (var range in new[] { (account.OpeningDate.AddDays(-1), today), (today, today.AddDays(-1)), (account.OpeningDate, today.AddDays(1)) })
        {
            capture.Draft.Fields.Single(f => f.Key == "from").Value = range.Item1.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            capture.Draft.Fields.Single(f => f.Key == "through").Value = range.Item2.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Assert.Equal(UiCulture.Text("InvalidAccountPeriod"), Assert.Throws<InvalidOperationException>(() => capture.Draft.Apply(data)).Message);
            Assert.Equal(before, JsonSerializer.Serialize(data));
        }
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CoverageEditorProbe>());
        await renderer.Dispatcher.InvokeAsync(() => InvokeEditor(capture.Editor, "Cancel"));
        Assert.True(capture.Closed); Assert.Equal(before, JsonSerializer.Serialize(data));
    }

    [Theory]
    [InlineData("nl-NL")]
    [InlineData("en-GB")]
    public async Task ConfirmedImportShowsConfirmedStatusAndAnEditActionInsteadOfTheSameConfirmButton(string culture)
    {
        using var language = new CultureScope(culture);
        var today = DateOnly.FromDateTime(DateTime.Today); var data = FinancialInsightsTests.Data(asOf: today);
        var batch = data.ImportBatches[0]; batch.CoverageFrom = batch.CoverageThrough = null;
        data.ImportBatches.RemoveAt(1);
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Administration>());
        Assert.Contains(UiCulture.Text("ConfirmStatementCoverage"), WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString)));
        var draft = EditorDraft.ImportCoverage(state, batch);
        await renderer.Dispatcher.InvokeAsync(() => state.SaveAsync(draft.Apply, draft.Revision));
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(root.ToHtmlString));
        Assert.Contains(UiCulture.Text("EditStatementCoverage"), html);
        Assert.Contains(UiCulture.Text("AccountPeriodConfirmed"), html);
        Assert.DoesNotContain(UiCulture.Text("ConfirmStatementCoverage"), html);
    }

    private static async Task InvokeEditor(Editor editor, string method)
    {
        await (Task)typeof(Editor).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
        typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null);
    }

    public sealed class EditorCapture
    {
        public EditorDraft Draft { get; set; } = null!;
        public Editor Editor { get; set; } = null!;
        public bool Closed { get; set; }
    }

    public sealed class CoverageEditorProbe : ComponentBase
    {
        [Inject] public EditorCapture Capture { get; set; } = null!;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Capture.Closed) return;
            builder.OpenComponent<Editor>(0);
            builder.AddAttribute(1, "Draft", Capture.Draft);
            builder.AddAttribute(2, "Closed", EventCallback.Factory.Create(this, () => Capture.Closed = true));
            builder.AddComponentReferenceCapture(3, component => Capture.Editor = (Editor)component);
            builder.CloseComponent();
        }
    }

    private static ServiceProvider Services(HouseholdData data)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization();
        services.AddSingleton<IWebAdministration>(new Backend(data)); services.AddSingleton<WebState>();
        services.AddSingleton<IJSRuntime>(new NoJs()); services.AddSingleton<UiPreferences>();
        services.AddSingleton(new WebHostSettings(false)); services.AddSingleton<EditorCapture>();
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
        public Task<AdministrationSnapshot> UpdateAsync(string revision, Action<HouseholdData> change) { change(data); Validation.Validate(data); return ReadAsync(); }
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
