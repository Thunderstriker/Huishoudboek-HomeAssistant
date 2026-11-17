using Huishoudboek.Core;
using Huishoudboek.Web;
using Microsoft.AspNetCore.Components.Authorization;

namespace Huishoudboek.Service;

public sealed class InvestmentAdministration(InvestmentMarketService market, AuthenticationStateProvider authentication,
    WebsiteSessions sessions) : IInvestmentAdministration
{
    private async Task AuthorizeAsync()
    {
        if (!sessions.Valid((await authentication.GetAuthenticationStateAsync()).User)) throw new UnauthorizedAccessException();
    }
    private static async Task<T> SafeAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (MarketDataException ex) { throw new InvalidOperationException(UiCulture.Text("InvestmentStatus" + ex.Code)); }
    }
    private static async Task SafeAsync(Func<Task> action) => await SafeAsync(async () => { await action(); return true; });
    public async Task<InvestmentMarketSnapshot> ReadAsync() { await AuthorizeAsync(); return market.Read(); }
    public async Task SaveSettingsAsync(InvestmentQuoteSettings settings, string? replacementKey, bool removeKey = false)
    { await AuthorizeAsync(); await SafeAsync(() => market.SaveSettingsAsync(settings, replacementKey, removeKey)); }
    public async Task TestAsync() { await AuthorizeAsync(); await SafeAsync(() => market.TestAsync()); }
    public async Task RefreshAsync() { await AuthorizeAsync(); await SafeAsync(() => market.RefreshAsync()); }
    public async Task<IReadOnlyList<InvestmentSymbol>> SearchAsync(string keywords)
    { await AuthorizeAsync(); return await SafeAsync(() => market.SearchAsync(keywords)); }
}
