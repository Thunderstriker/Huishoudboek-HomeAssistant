using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Huishoudboek.Web;

public sealed record HomeAttentionItem(string Title, string Detail, string Url, string Label, string Icon);

/// <summary>The dashboard and HA status card share the same ordering and financial checks.</summary>
public static class HomeAttention
{
    public static IReadOnlyList<HomeAttentionItem> Create(HomeOverview overview, int year, int month,
        IStringLocalizer<UiResources> l, CultureInfo culture)
    {
        var attention = new List<HomeAttentionItem>();
        string Money(decimal amount) => UiCulture.Euro(amount);
        var stale = overview.Accounts.Where(a => a.StaleImports).ToArray();
        if (stale.Length > 0)
            attention.Add(new(l["DashboardUpdateBankTitle"], stale.Length == 1
                ? l["DashboardStaleAccountNote", stale[0].Account.Name, stale[0].ImportedThrough?.ToString("dd-MM-yyyy") ?? l["Unknown"].Value]
                : l["DashboardStaleCount", stale.Length], "importeren", l["DashboardUpdateBankAction"], "import"));
        foreach (var account in overview.CashFlow.Accounts.Where(a => a.BufferAtRisk))
            attention.Add(new(l["DashboardReviewCashTitle"], l["DashboardBufferRisk", account.Account.Name, Money(account.LowestBalance), account.LowestDate.ToString("dd-MM-yyyy")], "vooruitblik#bestedingsruimte", l["DashboardViewPlan"], "alert"));
        if (overview.UncategorizedCount > 0)
            attention.Add(new(l["DashboardCategorizeTitle"], l["DashboardUncategorizedCount", overview.UncategorizedCount], "categoriseren", l["DashboardCategorizeAction"], "sliders"));
        if (overview.Month.Available < 0)
            attention.Add(new(l["DashboardReviewMonthTitle"], l["DashboardReviewMonthNote", new DateTime(year, month, 1).ToString("MMMM yyyy", culture), Money(overview.Month.Available)], "vooruitblik#maandstatus", l["DashboardViewPlan"], "alert"));
        var overBudget = overview.Month.Categories.Count(c => c.IsOverBudget);
        if (overBudget > 0)
            attention.Add(new(l["DashboardReviewBudgetsTitle"], l["DashboardOverBudgetCount", overBudget], "budgetten", l["Budgets"], "budget"));
        return attention;
    }
}
