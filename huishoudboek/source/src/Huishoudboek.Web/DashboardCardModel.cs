using Huishoudboek.Core;

namespace Huishoudboek.Web;

/// <summary>One read-only render; no circuit, drafts, shared browser preferences or database writes.</summary>
public sealed record DashboardCardModel(string Widget, string AppBase, string Theme, int Year, int Month,
    HomeOverview Overview, FinancialInsightsReport? Insights, string? OwnerName)
{
    public CategoryHistoryOverview? CategoryHistory { get; init; }
    public string? CategoryName { get; init; }
    public string TitleKey => Widget switch
    {
        "month-overview" => "MonthlyOverview", "spending" => "WhereMoneyGoes", "freedom-map" => "FreedomTitle",
        "budget-watch" => "DashboardBudgetWatch", "upcoming" => "DashboardComingUp", "data-status" => "CardDataStatus",
        "category-trend" => "CardCategoryTrend", "import-status" => "CardImportStatus", _ => throw new InvalidOperationException()
    };
    public string AppLink => Widget switch
    {
        "freedom-map" => "inzichten", "budget-watch" => "budgetten", "upcoming" => "vooruitblik",
        "data-status" => "weekcontrole", "category-trend" => $"categorietrends?categorie={CategoryHistory?.CategoryId}",
        "import-status" => "importeren", _ => "maand"
    };
}
