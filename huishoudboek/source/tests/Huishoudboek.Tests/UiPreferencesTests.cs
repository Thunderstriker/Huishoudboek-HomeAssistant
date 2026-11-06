using System.Globalization;
using System.Net;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class UiPreferencesTests
{
    [Fact]
    public async Task WeeklyReviewPreferenceSurvivesNewCircuitsAndCanBeRestored()
    {
        var browser = new BrowserJs(); var preferences = new UiPreferences(browser); var changes = 0;
        preferences.Changed += () => changes++;
        Assert.False(preferences.Loaded);
        await Task.WhenAll(preferences.LoadAsync(), preferences.LoadAsync());
        Assert.True(preferences.Loaded); Assert.False(preferences.HideWeeklyReview);
        Assert.Equal(1, browser.Reads);
        await preferences.SetWeeklyReviewHiddenAsync(true);
        Assert.True(preferences.HideWeeklyReview); Assert.True(preferences.CanPersist);
        Assert.Equal(2, changes);

        var nextVisit = new UiPreferences(browser); await nextVisit.LoadAsync();
        Assert.True(nextVisit.HideWeeklyReview);
        await nextVisit.SetWeeklyReviewHiddenAsync(false);
        var restored = new UiPreferences(browser); await restored.LoadAsync();
        Assert.False(restored.HideWeeklyReview);

        var otherBrowser = new UiPreferences(new BrowserJs()); await otherBrowser.LoadAsync();
        Assert.False(otherBrowser.HideWeeklyReview);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableStorageKeepsTheAppUsableWithASessionOnlyPreference(bool throws)
    {
        var browser = new BrowserJs { Available = false, Throws = throws }; var preferences = new UiPreferences(browser);
        await preferences.LoadAsync();
        Assert.True(preferences.Loaded); Assert.False(preferences.CanPersist); Assert.False(preferences.HideWeeklyReview);
        await preferences.SetWeeklyReviewHiddenAsync(true);
        Assert.True(preferences.HideWeeklyReview); Assert.False(preferences.CanPersist);
        await preferences.LoadAsync(); Assert.True(preferences.HideWeeklyReview);
        await preferences.SetWeeklyReviewHiddenAsync(false); Assert.False(preferences.HideWeeklyReview);
        var id = Guid.NewGuid();
        await preferences.SetDashboardAccountsAsync([id]);
        Assert.Equal(new[] { id }, preferences.DashboardAccountIds);
        Assert.False(preferences.CanPersist);
        await preferences.LoadAsync();
        Assert.Equal(new[] { id }, preferences.DashboardAccountIds);
    }

    [Fact]
    public async Task DashboardSelectionPersistsAndDistinguishesDefaultFromDeliberatelyEmpty()
    {
        var browser = new BrowserJs(); var preferences = new UiPreferences(browser);
        await preferences.LoadAsync();
        Assert.Null(preferences.DashboardAccountIds);
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await preferences.SetDashboardAccountsAsync([first, second, first]);
        Assert.Equal(new[] { first, second }, preferences.DashboardAccountIds);
        var next = new UiPreferences(browser); await next.LoadAsync();
        Assert.Equal(new[] { first, second }, next.DashboardAccountIds);
        await next.SetDashboardAccountsAsync([]);
        var empty = new UiPreferences(browser); await empty.LoadAsync();
        Assert.Empty(empty.DashboardAccountIds!);
        await empty.SetDashboardAccountsAsync(null);
        var defaults = new UiPreferences(browser); await defaults.LoadAsync();
        Assert.Null(defaults.DashboardAccountIds);
        Assert.Null((await LoadOtherBrowser()).DashboardAccountIds);

        static async Task<UiPreferences> LoadOtherBrowser()
        {
            var other = new UiPreferences(new BrowserJs()); await other.LoadAsync(); return other;
        }
    }

    [Fact]
    public async Task InvalidStoredAccountIdsFallBackToDefaultsWithoutAffectingWeeklyPreference()
    {
        var browser = new BrowserJs { AccountIds = ["not-a-guid"] };
        var preferences = new UiPreferences(browser);
        await preferences.SetWeeklyReviewHiddenAsync(true);
        Assert.Null(preferences.DashboardAccountIds);
        Assert.True(preferences.HideWeeklyReview);
        Assert.True(preferences.CanPersist);
    }

    [Theory]
    [InlineData("nl-NL")]
    [InlineData("en-GB")]
    public async Task NavigationRespondsToHidingAndRestoringWithoutHidingOtherPages(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization();
            services.AddSingleton<IJSRuntime>(new BrowserJs()); services.AddSingleton<UiPreferences>();
            services.AddSingleton<NavigationManager>(new TestNavigation());
            using var provider = services.BuildServiceProvider(); var preferences = provider.GetRequiredService<UiPreferences>();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var navigation = await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<MainNavigation>());
            async Task<string> Html() => WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(navigation.ToHtmlString));

            Assert.DoesNotContain("href=\"weekcontrole\"", await Html()); // Do not flash a hidden link before storage loads.
            await renderer.Dispatcher.InvokeAsync(preferences.LoadAsync);
            Assert.Contains("href=\"weekcontrole\"", await Html());
            await renderer.Dispatcher.InvokeAsync(() => preferences.SetWeeklyReviewHiddenAsync(true));
            var hidden = await Html();
            Assert.DoesNotContain("href=\"weekcontrole\"", hidden);
            Assert.Contains("href=\"vooruitblik\"", hidden); Assert.Contains("href=\"beheer\"", hidden);
            Assert.Contains("href=\"maand\"", hidden);
            Assert.Contains(UiCulture.Text("StartDashboard"), hidden);
            Assert.Contains(UiCulture.Text("FinancialOutlook"), hidden);
            Assert.DoesNotContain("href=\"instellen\"", hidden); // Setup is a secondary administration link.
            await renderer.Dispatcher.InvokeAsync(() => preferences.SetWeeklyReviewHiddenAsync(false));
            Assert.Contains("href=\"weekcontrole\"", await Html());

            var control = await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<WeeklyReviewVisibility>());
            var controlHtml = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(control.ToHtmlString));
            Assert.Contains(UiCulture.Text("HideWeeklyReview"), controlHtml);
            Assert.DoesNotContain("disabled", controlHtml);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private sealed class BrowserJs : IJSRuntime
    {
        public bool Available { get; set; } = true;
        public bool Throws { get; set; }
        public bool Hidden { get; private set; }
        public int Reads { get; private set; }
        public string[]? AccountIds { get; set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (Throws) throw new JSException("Storage unavailable");
            object? result;
            if (identifier == "huishoudboek.preferences.readWeeklyReviewHidden")
            {
                Reads++; result = Available ? Hidden : null;
            }
            else if (identifier == "huishoudboek.preferences.writeWeeklyReviewHidden")
            {
                if (Available) Hidden = (bool)args![0]!;
                result = Available;
            }
            else if (identifier == "huishoudboek.preferences.readDashboardAccounts")
                result = new DashboardAccountsPreference(Available, Available ? AccountIds : null);
            else if (identifier == "huishoudboek.preferences.writeDashboardAccounts")
            {
                if (Available) AccountIds = ((IReadOnlyList<Guid>?)args![0])?.Select(id => id.ToString()).ToArray();
                result = Available;
            }
            else throw new NotSupportedException(identifier);
            return ValueTask.FromResult((TValue)result!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/ingress/test/", "http://localhost/ingress/test/");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }
}
