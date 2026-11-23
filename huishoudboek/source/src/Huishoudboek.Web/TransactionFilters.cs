using System.Globalization;
using Huishoudboek.Core;

namespace Huishoudboek.Web;

public sealed record TransactionFilter(string Search = "", Guid? AccountId = null, Guid? CategoryId = null,
    string Direction = "", bool Uncategorized = false, DateOnly? From = null, DateOnly? Through = null,
    decimal? Minimum = null, decimal? Maximum = null)
{
    public IReadOnlyList<Transaction> Apply(HouseholdData data, IReadOnlySet<Guid> scope)
    {
        var lines = TransactionAccounting.Lines(data, data.Transactions).ToLookup(l => l.Transaction.Id);
        var categoryPaths = CategoryHierarchy.Ordered(data).ToDictionary(e => e.Category.Id, e => e.Path);
        var search = Search.Trim();
        return data.Transactions.Where(t => (scope.Contains(t.AccountId) || t.CounterAccountId is { } counter && scope.Contains(counter)) &&
            (AccountId == null || t.AccountId == AccountId || t.CounterAccountId == AccountId) &&
            (From == null || t.Date >= From) && (Through == null || t.Date <= Through) &&
            (Minimum == null || Math.Abs(t.Amount) >= Minimum) && (Maximum == null || Math.Abs(t.Amount) <= Maximum) &&
            (Direction.Length == 0 || (Direction == "credit" ? Effect(t, scope) > 0 : Direction == "debit" && Effect(t, scope) < 0)) &&
            (CategoryId == null || lines[t.Id].Any(l => (l.CategoryId ?? Guid.Empty) == CategoryId)) &&
            (!Uncategorized || lines[t.Id].Any(l => l.CategoryId == null)) &&
            (search.Length == 0 || ($"{TransactionRecognition.SearchText(data, t)} {data.Accounts.FirstOrDefault(a => a.Id == t.AccountId)?.Name} {data.Accounts.FirstOrDefault(a => a.Id == t.CounterAccountId)?.Name} " +
                 string.Join(" ", lines[t.Id].Select(l => (l.CategoryId is { } id ? categoryPaths.GetValueOrDefault(id, UiCulture.Text("Uncategorized")) : UiCulture.Text("Uncategorized")) + " " + l.Note)))
                .Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(t => t.Date).ThenBy(t => t.Id).ToList();
    }

    public decimal Effect(Transaction transaction, IReadOnlySet<Guid> scope) => AccountId is { } id
        ? Finance.AccountEffect(transaction, id) : scope.Sum(id => Finance.AccountEffect(transaction, id));

    public static bool TryDate(string? text, out DateOnly value) => DateOnly.TryParseExact(text, "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out value) && value.Year is >= 1900 and <= 2200;

    public static bool TryAmount(string text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ||
            amount < 0 || decimal.Round(amount, 2) != amount) return false;
        value = amount; return true;
    }
}
