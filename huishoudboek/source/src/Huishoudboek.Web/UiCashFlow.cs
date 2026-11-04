using Huishoudboek.Core;

namespace Huishoudboek.Web;

/// <summary>Localized presentation of cash-flow warnings; account and payment names remain user data.</summary>
public static class UiCashFlow
{
    public static IEnumerable<string> Warnings(HouseholdData data, AccountCashFlow forecast, DateOnly today)
    {
        if (forecast.PaymentsToReviewCount > 0)
            yield return UiCulture.Text("RecurringReviewSummary", forecast.PaymentsToReviewCount);
        if (forecast.StaleImports)
            yield return forecast.StatementThrough == null ? UiCulture.Text("NoUsableImportWarning") :
                UiCulture.Text("OldImportWarning", forecast.StatementThrough.Value.ToString("dd-MM-yyyy"),
                    forecast.LastImportDate?.ToString("dd-MM-yyyy") ?? UiCulture.Text("None"));
        if (forecast.LimitedHistory) yield return UiCulture.Text("LimitedVariableHistoryWarning");
        if (forecast.Account.MinimumCashBuffer == 0) yield return UiCulture.Text("NoBufferWarning");
        if (forecast.NextIncomeDate == null)
            yield return UiCulture.Text("NoNextIncomeWarning", CashFlow.IncomeSearchDays, forecast.SpendableUntil.DayNumber - today.DayNumber);
    }
}
