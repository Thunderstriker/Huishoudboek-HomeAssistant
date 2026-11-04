using System.Collections;
using System.Globalization;
using System.Resources;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class LocalizationTests
{
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        WebsiteLanguage.Register(services);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("nl-NL", "Maandoverzicht", "12 regels · pagina 2 van 3")]
    [InlineData("en-GB", "Monthly overview", "12 rows · page 2 of 3")]
    public void StandardLocalizerLoadsNamedResourcesAndFormatsArguments(string culture, string title, string summary)
    {
        using var scope = new CultureScope(culture);
        using var services = Services();
        var text = services.GetRequiredService<IStringLocalizer<UiResources>>();
        Assert.Equal(title, text["MonthlyOverview"].Value);
        Assert.False(text["MonthlyOverview"].ResourceNotFound);
        Assert.Equal(summary, text["PagerSummary", 12, 2, 3].Value);
    }

    [Fact]
    public void BothCataloguesContainTheSameKeysAndValidFormatStrings()
    {
        var manager = new ResourceManager(typeof(UiResources));
        var dutch = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var english = manager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)!;
        var expected = dutch.Cast<DictionaryEntry>().Select(e => (string)e.Key).Order().ToArray();
        Assert.Equal(expected, english.Cast<DictionaryEntry>().Select(e => (string)e.Key).Order().ToArray());
        foreach (var key in expected)
        {
            Assert.False(string.IsNullOrWhiteSpace(dutch.GetString(key)));
            Assert.False(string.IsNullOrWhiteSpace(english.GetString(key)));
            var dutchFormat = System.Text.CompositeFormat.Parse(dutch.GetString(key)!);
            var englishFormat = System.Text.CompositeFormat.Parse(english.GetString(key)!);
            Assert.Equal(dutchFormat.MinimumArgumentCount, englishFormat.MinimumArgumentCount);
            var arguments = Enumerable.Repeat<object>(1, dutchFormat.MinimumArgumentCount).ToArray();
            _ = string.Format(CultureInfo.InvariantCulture, dutchFormat, arguments);
            _ = string.Format(CultureInfo.InvariantCulture, englishFormat, arguments);
        }
    }

    [Theory]
    [InlineData(null, "en-GB", "nl-NL")]
    [InlineData("en-GB", "nl-NL", "en-GB")]
    [InlineData("nl-NL", "en-GB", "nl-NL")]
    [InlineData("fr-FR", "en-GB", "nl-NL")]
    public async Task RequestCultureUsesOnlySupportedCookiePreferences(string? cookieCulture, string browserLanguage, string expected)
    {
        using var services = Services();
        var options = services.GetRequiredService<IOptions<RequestLocalizationOptions>>();
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = browserLanguage;
        if (cookieCulture != null)
            context.Request.Headers.Cookie = CookieRequestCultureProvider.DefaultCookieName + "=" +
                Uri.EscapeDataString(CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(cookieCulture)));
        var middleware = new RequestLocalizationMiddleware(c =>
        {
            Assert.Equal(expected, CultureInfo.CurrentCulture.Name);
            Assert.Equal(expected, CultureInfo.CurrentUICulture.Name);
            return Task.CompletedTask;
        }, options, services.GetRequiredService<ILoggerFactory>());
        await middleware.Invoke(context);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("jaar", true)]
    [InlineData("beheer#database", true)]
    [InlineData("https://example.com", false)]
    [InlineData("//example.com", false)]
    [InlineData("/jaar", false)]
    [InlineData("\\example.com", false)]
    [InlineData("%2f%2fexample.com", false)]
    [InlineData("jaar\r\n", false)]
    public void LanguageReturnPathsCannotRedirectOffSite(string path, bool expected)
        => Assert.Equal(expected, WebsiteLanguage.IsSafeReturnPath(path));

    [Theory]
    [InlineData("nl-NL", "1.234,56")]
    [InlineData("en-GB", "1,234.56")]
    public void CurrencyFormattingAlwaysRemainsEuro(string culture, string expectedNumber)
    {
        using var scope = new CultureScope(culture);
        var amount = WebState.Money(1234.56m);
        Assert.Contains("€", amount);
        Assert.Contains(expectedNumber, amount);
        Assert.DoesNotContain("£", amount);
    }

    [Theory]
    [InlineData("nl-NL", "Vorige", "12 regels · pagina 2 van 3")]
    [InlineData("en-GB", "Previous", "12 rows · page 2 of 3")]
    public async Task RazorComponentsRenderNamedLocalizedResources(string culture, string previous, string summary)
    {
        using var scope = new CultureScope(culture);
        using var services = Services();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Count"] = 12, ["Page"] = 1, ["Size"] = 5 });
            return (await renderer.RenderComponentAsync<Pager>(parameters)).ToHtmlString();
        });
        html = System.Net.WebUtility.HtmlDecode(html);
        Assert.Contains(previous, html);
        Assert.Contains(summary, html);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        public CultureScope(string name)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        }
        public void Dispose()
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}
