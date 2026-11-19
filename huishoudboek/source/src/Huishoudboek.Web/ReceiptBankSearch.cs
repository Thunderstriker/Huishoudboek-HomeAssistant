using System.Globalization;
using Huishoudboek.Core;

namespace Huishoudboek.Web;

public static class ReceiptBankSearch
{
    public static IEnumerable<Transaction> Find(IEnumerable<Transaction> eligible, string search, Guid? selectedId)
    {
        var terms = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var dutch = CultureInfo.GetCultureInfo("nl-NL");
        var english = CultureInfo.GetCultureInfo("en-GB");
        var choices = eligible.Where(t =>
        {
            if (t.Id == selectedId || terms.Length == 0) return true;
            var text = $"{t.Description} {t.UserDescription} {t.RecognizedName} {t.Date:dd-MM-yyyy} {t.Date:yyyy-MM-dd} {t.Date.ToString("d MMMM yyyy", dutch)} {t.Date.ToString("d MMMM yyyy", english)} {t.Amount.ToString("F2", dutch)} {t.Amount.ToString("F2", CultureInfo.InvariantCulture)}";
            return terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
        }).OrderByDescending(t => t.Id == selectedId).ThenByDescending(t => t.Date);
        return terms.Length == 0 ? choices.Take(80) : choices;
    }
}
