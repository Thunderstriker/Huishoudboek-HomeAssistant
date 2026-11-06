using System.Net;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Huishoudboek.Web.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class LanguageSwitchTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/api/hassio_ingress/test")]
    public async Task SidebarSwitchPersistsLanguageAndTranslatesPages(string pathBase)
    {
        await using var app = Build(pathBase);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        var dutch = await GetPage(client, pathBase + "/jaar");
        Assert.Contains("Jaaroverzicht", dutch);
        Assert.Contains("class=\"language-switch\"", dutch);
        Assert.DoesNotContain("name=\"language\" disabled", dutch);
        Assert.DoesNotContain("Taal toepassen", dutch);
        var sidebarEnd = dutch.IndexOf("</aside>", StringComparison.Ordinal);
        Assert.InRange(dutch.IndexOf("class=\"language-switch\"", StringComparison.Ordinal), 0, sidebarEnd);
        Assert.Contains("name=\"language\" value=\"nl\"", dutch);
        Assert.Contains("name=\"language\" value=\"en\"", dutch);

        var response = await Switch(client, pathBase, dutch, "en");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(pathBase + "/jaar", response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie =>
            cookie.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal) && cookie.Contains("path=" + (pathBase.Length == 0 ? "/" : pathBase)));

        var english = await GetPage(client, response.Headers.Location.OriginalString);
        Assert.Contains("<html lang=\"en\"", english);
        Assert.Contains("Annual overview", english);
        Assert.Contains("Income and spending", english);
        Assert.DoesNotContain("Jaaroverzicht", english);
        foreach (var (path, expected) in new[] {
            ("/", "Your balances, spending and the week ahead."), ("/maand", "Monthly overview"),
            ("/categorietrends", "Your spending story"),
            ("/rekeningen", "Accounts & owners"),
            ("/vooruitblik", "How much can I safely spend?"), ("/instellen", "Your first week"),
            ("/weekcontrole", "Hide weekly review from navigation"),
            ("/budgetten", "Budgets & categories"), ("/transacties", "No transactions within these filters."),
            ("/beheer", "Your records"), ("/hypotheek", "Mortgage · annuity, fixed rate"),
            ("/vaste-posten", "Recurring income & bills"), ("/categoriseren", "Step by step"),
            ("/overboekingen", "Historical transfers"), ("/importeren", "Import settings") })
        {
            var page = await GetPage(client, pathBase + path);
            Assert.Contains(expected, page);
            Assert.Contains("<html lang=\"en\"", page);
        }
        response = await Switch(client, pathBase, english, "nl");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var switchedBack = await GetPage(client, response.Headers.Location!.OriginalString);
        Assert.Contains("<html lang=\"nl\"", switchedBack);
        Assert.Contains("Jaaroverzicht", switchedBack);
        Assert.DoesNotContain("Annual overview", switchedBack);
        foreach (var (path, expected) in new[] {
            ("/", "Uw saldi, uitgaven en de komende week"), ("/maand", "Maandoverzicht"),
            ("/categorietrends", "Het verhaal achter uw uitgaven"),
            ("/vooruitblik", "Hoeveel kan ik verantwoord uitgeven?"), ("/instellen", "Uw eerste week"),
            ("/weekcontrole", "Wekelijkse controle verbergen in de navigatie") })
        {
            var page = await GetPage(client, pathBase + path);
            Assert.Contains(expected, page);
            Assert.Contains("<html lang=\"nl\"", page);
        }

        var invalid = await Switch(client, pathBase, switchedBack, "fr");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var noToken = await client.PostAsync(pathBase + "/website/language", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["language"] = "en", ["returnPath"] = "jaar" }));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
    }

    [Theory]
    [InlineData("nl-NL", "Rekening", "Rekeningtype", "1234,56", "Betaalrekening")]
    [InlineData("en-GB", "Account", "Account type", "1234.56", "Current account")]
    public void EditorsLocalizeLabelsAndRoundTripAmountsWithoutTranslatingUserData(
        string culture, string title, string typeLabel, string amount, string accountType)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var state = new WebState(new EmptyAdministration());
            var account = new Account { Name = "Rekening", OpeningDate = state.Today, MinimumCashBuffer = 1234.56m };
            state.Data.Accounts.Add(account);
            var draft = EditorDraft.Account(state, account);
            Assert.Equal(title, draft.Title);
            Assert.Equal("Rekening", draft.Text("name"));
            Assert.Equal(typeLabel, draft.Fields.Single(f => f.Key == "kind").Label);
            Assert.Equal(accountType, draft.Fields.Single(f => f.Key == "kind").Options.Single(o => o.Value == "Current").Label);
            Assert.Equal(amount, draft.Text("buffer"));
            Assert.Equal(1234.56m, draft.Number("buffer"));
            var forecast = CashFlow.Project(state.Data, state.Today).Accounts.Single();
            var warnings = UiCashFlow.Warnings(state.Data, forecast, state.Today).ToList();
            Assert.Equal(forecast.Warnings.Count, warnings.Count);
            if (culture == "nl-NL") Assert.Equal(forecast.Warnings, warnings);
            else Assert.Contains(warnings, warning => warning.StartsWith("No usable bank import", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private static async Task<string> GetPage(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> Switch(HttpClient client, string pathBase, string html, string language)
    {
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return client.PostAsync(pathBase + "/website/language", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["language"] = language, ["returnPath"] = "jaar", ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token) }));
    }

    private static WebApplication Build(string pathBase)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        WebsiteLanguage.Register(builder.Services);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(new WebHostSettings(true));
        builder.Services.AddSingleton<IWebAdministration, EmptyAdministration>();
        builder.Services.AddScoped<WebState>();
        builder.Services.AddScoped<UiPreferences>();
        var app = builder.Build();
        if (pathBase.Length > 0) app.UsePathBase(pathBase);
        app.UseRouting();
        app.UseRequestLocalization();
        app.Use((context, next) => {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test")], "test"));
            return next(context);
        });
        app.UseAuthorization();
        app.UseAntiforgery();
        WebsiteLanguage.Map(app);
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        return app;
    }

    private sealed class EmptyAdministration : IWebAdministration
    {
        public Task<AdministrationSnapshot> ReadAsync() => Task.FromResult(new AdministrationSnapshot("test", HouseholdData.CreateEmpty()));
        public Task<AdministrationSnapshot> UpdateAsync(string revision, Action<HouseholdData> change) => throw new NotSupportedException();
        public Task<UploadResponse> UploadAsync(Stream source, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PreviewResponse> PreviewAsync(PreviewRequest request, string revision) => throw new NotSupportedException();
        public Task<PreviewRow> AdjustAsync(AdjustImportRequest request) => throw new NotSupportedException();
        public Task<CommitImportResponse> CommitAsync(CommitImportRequest request, string revision) => throw new NotSupportedException();
        public Task<string> RestoreAsync(Stream source, string revision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
