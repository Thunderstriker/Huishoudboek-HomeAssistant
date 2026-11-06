namespace Huishoudboek.Core;

public sealed record CategoryHistoryMonth(DateOnly Month, decimal Purchases, decimal Recoveries,
    int EntryCount, bool IsPartial, bool CoverageConfirmed, decimal? MovingAverage)
{
    public decimal Spent => Purchases - Recoveries;
}

public sealed record CategoryHistoryComparison(IReadOnlyList<CategoryHistoryMonth> Previous,
    IReadOnlyList<CategoryHistoryMonth> Latest)
{
    public decimal PreviousTotal => Previous.Sum(m => m.Spent);
    public decimal LatestTotal => Latest.Sum(m => m.Spent);
    public decimal Difference => LatestTotal - PreviousTotal;
    // A zero or negative baseline has no meaningful percentage increase/decrease.
    public decimal? Change => PreviousTotal > 0 ? Difference / PreviousTotal : null;
}

public sealed record CategoryHistoryOverview(Guid CategoryId, DateOnly From, DateOnly Through,
    IReadOnlyList<CategoryHistoryMonth> Months, CategoryHistoryComparison? Comparison)
{
    public decimal Total => Months.Sum(m => m.Spent);
    public decimal Purchases => Months.Sum(m => m.Purchases);
    public decimal Recoveries => Months.Sum(m => m.Recoveries);
    public decimal? MonthlyAverage => Months.Any(m => !m.IsPartial)
        ? Months.Where(m => !m.IsPartial).Average(m => m.Spent) : null;
    public CategoryHistoryMonth? Peak => Months.Where(m => !m.IsPartial && m.EntryCount > 0)
        .OrderByDescending(m => m.Spent).FirstOrDefault();
    public bool HasActivity => Months.Any(m => m.EntryCount > 0);
    public int UnconfirmedMonths => Months.Count(m => !m.CoverageConfirmed);
}

/// <summary>Actual monthly category spending, using the same signed accounting lines as budgets.
/// Missing bank coverage is reported explicitly, never inferred from an absence of transactions.</summary>
public static class CategoryHistory
{
    public static CategoryHistoryOverview Create(HouseholdData data, Guid categoryId, DateOnly from,
        DateOnly through, DateOnly today, Guid? ownerId = null)
    {
        var first = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(through.Year, through.Month, 1);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var count = (lastMonth.Year - first.Year) * 12 + lastMonth.Month - first.Month + 1;
        if (first.Year < 1900 || lastMonth.Year > 2200 || count is < 1 or > 120 || lastMonth > currentMonth)
            throw new InvalidOperationException("Kies een periode van 1 tot 120 maanden tussen 1900 en vandaag.");
        if (categoryId != Guid.Empty && !data.Categories.Any(c => c.Id == categoryId && c.Purpose == CategoryPurpose.Normal))
            throw new InvalidOperationException("Kies een gewone uitgavencategorie.");

        var last = lastMonth.AddMonths(1).AddDays(-1);
        var cutoff = today < last ? today : last;
        var accounts = data.Accounts.Where(a => ownerId == null || a.OwnerIds.Contains(ownerId.Value)).ToList();
        var scope = accounts.Select(a => a.Id).ToHashSet();
        var lines = TransactionAccounting.Lines(data, data.Transactions.Where(t => scope.Contains(t.AccountId) &&
                t.Date >= first && t.Date <= cutoff))
            .Where(l => (l.CategoryId ?? Guid.Empty) == categoryId && l.Spending != 0)
            .ToLookup(l => new DateOnly(l.Transaction.Date.Year, l.Transaction.Date.Month, 1));
        var months = new List<CategoryHistoryMonth>(count);
        for (var index = 0; index < count; index++)
        {
            var month = first.AddMonths(index);
            var end = month.AddMonths(1).AddDays(-1);
            var asOf = end < today ? end : today;
            var relevant = accounts.Where(a => a.OpeningDate <= asOf).ToList();
            var covered = relevant.Count > 0 && relevant.All(a => BankImportCoverage.Covers(data, a.Id,
                a.OpeningDate > month ? a.OpeningDate : month, asOf));
            var entries = lines[month].ToList();
            var purchases = entries.Where(l => l.Spending > 0).Sum(l => l.Spending);
            var recoveries = -entries.Where(l => l.Spending < 0).Sum(l => l.Spending);
            var partial = end > today;
            decimal? average = index >= 2 && !partial
                ? (months[index - 2].Spent + months[index - 1].Spent + purchases - recoveries) / 3 : null;
            months.Add(new(month, purchases, recoveries, entries.Select(l => l.Transaction.Id).Distinct().Count(),
                partial, covered, average));
        }

        var complete = months.Where(m => !m.IsPartial).ToList();
        var comparison = complete.Count >= 24
            ? new CategoryHistoryComparison(complete.TakeLast(24).Take(12).ToArray(), complete.TakeLast(12).ToArray()) : null;
        return new(categoryId, first, cutoff, months, comparison);
    }
}
