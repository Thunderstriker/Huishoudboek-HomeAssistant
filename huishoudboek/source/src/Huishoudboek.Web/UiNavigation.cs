using System.Globalization;

namespace Huishoudboek.Web;

public enum ToolbarPeriod { None, Month, Year, Today, Independent, History }

public static class UiNavigation
{
    public static DateOnly ActualThrough(DateOnly end)
    { var today = DateOnly.FromDateTime(DateTime.Today); return end < today ? end : today; }
    public static ToolbarPeriod Period(string relativePath) => relativePath.Split('?', '#')[0].Trim('/') switch
    {
        "" or "maand" or "budgetten" or "vaste-posten" or "categoriseren" or "vooruitblik" => ToolbarPeriod.Month,
        "jaar" => ToolbarPeriod.Year,
        "rekeningen" or "beleggingen" or "hypotheek" or "weekcontrole" or "abonnementen" => ToolbarPeriod.Today,
        "transacties" or "categorietrends" or "kalender" => ToolbarPeriod.Independent,
        "inzichten" => ToolbarPeriod.History,
        _ => ToolbarPeriod.None
    };

    // Relative links remain inside the Home Assistant ingress base path.
    public static string Transactions(Guid? category = null, Guid? account = null, DateOnly? from = null,
        DateOnly? through = null, Guid? transaction = null, Guid? owner = null)
    {
        var query = new List<string> { "allDates=true" };
        void Add(string key, string? value) { if (value != null) query.Add(key + "=" + Uri.EscapeDataString(value)); }
        Add("categorie", category?.ToString("D")); Add("account", account?.ToString("D"));
        Add("from", from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("through", through?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("transaction", transaction?.ToString("D")); Add("owner", owner?.ToString("D"));
        return "transacties?" + string.Join("&", query);
    }
}
