using Huishoudboek.Core;

namespace Huishoudboek.Web;

/// <summary>One read-only render; no circuit, drafts, shared browser preferences or database writes.</summary>
public sealed record DashboardCardModel(string Widget, string AppBase, string Theme, int Year, int Month,
    HomeOverview Overview, FinancialInsightsReport? Insights, string? OwnerName);
