using Huishoudboek.Core;

namespace Huishoudboek.Web;

/// <summary>Secret replacement is write-only; snapshots never contain the configured key.</summary>
public interface IInvestmentAdministration
{
    Task<InvestmentMarketSnapshot> ReadAsync();
    Task SaveSettingsAsync(InvestmentQuoteSettings settings, string? replacementKey, bool removeKey = false);
    Task TestAsync();
    Task TestAmznAsync();
    Task RefreshAsync();
    Task<IReadOnlyList<InvestmentSymbol>> SearchAsync(string keywords);
}
