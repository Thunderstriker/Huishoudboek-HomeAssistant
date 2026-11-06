namespace Huishoudboek.Core;

public sealed record InsightMonth(DateOnly Month, bool Covered, decimal Income, decimal Spending,
    decimal Essentials, decimal Exceptional, decimal Savings, decimal Discretionary,
    IReadOnlyList<AccountingLine> Lines);

public sealed record InsightDiscovery(string Key, decimal Value, DateOnly From, DateOnly Through,
    IReadOnlyList<InsightEvidence> Evidence);

public sealed record InsightEvidence(Transaction Transaction, decimal Spending);

public sealed record MoneyFingerprint(decimal? Consistency, decimal? BurstShare, decimal? SavingRhythm,
    decimal? Commitments, decimal? Concentration, decimal? Flexibility,
    IReadOnlyList<InsightDiscovery> Discoveries);

public sealed record FreedomMap(decimal LiquidBalance, decimal Earmarked, decimal ExceptionalReserve,
    decimal UsableReserves, decimal CurrentCash, decimal Spendable, decimal? MonthlyEssentials,
    decimal? MonthlyLifestyle, decimal? MonthlyIncome, decimal MonthlyCommitments,
    decimal? EssentialMonths, decimal? LifestyleMonths, decimal? ReserveChangeMonths,
    int TargetMonths, bool BalancesCovered, bool SpendingUncertain);

public sealed record FinancialInsightsReport(DateOnly AsOf, IReadOnlyList<InsightMonth> Months,
    FreedomMap Freedom, MoneyFingerprint Fingerprint, int CoveredMonths, decimal UnclassifiedSpending)
{
    public bool LimitedHistory => CoveredMonths < FinancialInsights.MinimumMonths;
}

/// <summary>
/// Reporting only: never changes bank entries or balances. Baselines use explicitly covered,
/// completed months. Current schedules replace their linked history, not an additional expense.
/// </summary>
public static class FinancialInsights
{
    public const int MinimumMonths = 3;
    public const int HistoryMonths = 12;

    public static FinancialInsightsReport Create(HouseholdData data, DateOnly today, Guid? ownerId = null)
    {
        var accounts = data.Accounts.Where(a => ownerId == null || a.OwnerIds.Contains(ownerId.Value)).ToList();
        var scope = accounts.Select(a => a.Id).ToHashSet();
        var firstCurrent = new DateOnly(today.Year, today.Month, 1);
        var first = firstCurrent.AddMonths(-HistoryMonths);
        var lines = TransactionAccounting.Lines(data, data.Transactions.Where(t => scope.Contains(t.AccountId) &&
            t.Date >= first && t.Date < firstCurrent)).ToList();
        var byMonth = lines.ToLookup(l => new DateOnly(l.Transaction.Date.Year, l.Transaction.Date.Month, 1));
        var months = new List<InsightMonth>();
        for (var month = first; month < firstCurrent; month = month.AddMonths(1))
        {
            var end = month.AddMonths(1).AddDays(-1);
            var relevant = accounts.Where(a => a.Kind != AccountKind.Investment && a.OpeningDate <= end).ToList();
            // A mid-month opening is not a full observed month.
            var covered = relevant.Count > 0 && relevant.All(a => a.OpeningDate <= month &&
                BankImportCoverage.Covers(data, a.Id, month, end));
            // Investment-account activity is not a normal household spending/income baseline.
            var entries = byMonth[month].Where(l => accounts.Any(a => a.Id == l.Transaction.AccountId && a.Kind != AccountKind.Investment)).ToList();
            var report = Finance.Report(data, month.Year, month.Month, today, scope);
            months.Add(new(month, covered, entries.Where(l => !l.IsExceptional).Sum(l => l.Income),
                entries.Sum(l => l.TypicalSpending), entries.Where(l => l.IsEssential).Sum(l => l.TypicalSpending),
                entries.Where(l => l.IsExceptional).Sum(l => l.Spending), report.Savings,
                entries.Where(IsDiscretionary).Sum(l => l.TypicalSpending), entries));
        }

        var observed = months.Where(m => m.Covered).ToList();
        var usableHistory = observed.Count >= MinimumMonths;
        var categories = data.Categories.ToDictionary(c => c.Id);
        bool Ordinary(RecurringPayment r) => r.CategoryId == null ||
            categories.TryGetValue(r.CategoryId.Value, out var category) && category.Purpose == CategoryPurpose.Normal && !category.IsExceptional;
        var splitTransactions = data.TransactionAllocations.Select(a => a.TransactionId).ToHashSet();
        var observedLines = observed.SelectMany(m => m.Lines).ToList();
        var schedules = data.RecurringPayments.Where(r => scope.Contains(r.AccountId) &&
            accounts.Any(a => a.Id == r.AccountId && a.Kind != AccountKind.Investment && a.OpeningDate <= today) &&
            r.Frequency != PaymentFrequency.Once && r.StartDate <= today && (r.EndDate == null || r.EndDate >= today) &&
            Ordinary(r)).ToList();
        var scheduleIds = schedules.Select(r => r.Id).ToHashSet();
        var normalized = schedules.Select(r => {
            var linked = observedLines.Where(l => l.Transaction.RecurringPaymentId == r.Id).ToList();
            var monthly = MonthlyAmount(r);
            if (linked.Any(l => splitTransactions.Contains(l.Transaction.Id)))
            {
                // Preserve the actual category mix, not the parent entry's category. Weight by
                // bank amounts so multiple allocations or variable bills do not duplicate cash.
                var cash = linked.DistinctBy(l => l.Transaction.Id).Sum(l => Math.Abs(l.Transaction.Amount));
                decimal Share(decimal amount) => cash > 0 ? Math.Clamp(amount / cash, 0, 1) : 0;
                return new RecurringBaseline(monthly > 0 ? monthly * Share(linked.Where(l => !l.IsExceptional).Sum(l => l.Income)) : 0,
                    monthly < 0 ? -monthly * Share(linked.Sum(l => l.TypicalSpending)) : 0,
                    monthly < 0 ? -monthly * Share(linked.Where(l => l.IsEssential).Sum(l => l.TypicalSpending)) : 0);
            }
            return new RecurringBaseline(Math.Max(0, monthly), Math.Max(0, -monthly),
                monthly < 0 && r.CategoryId is { } id && categories[id].IsEssential ? -monthly : 0);
        }).ToList();
        var residual = observedLines.Where(l =>
            l.Transaction.RecurringPaymentId is not { } id || !scheduleIds.Contains(id)).ToList();
        var scheduledEssentials = normalized.Sum(r => r.Essentials);
        var commitments = normalized.Sum(r => r.Spending);
        decimal? essentials = usableHistory ? NetSpending(residual.Where(l => l.IsEssential)) / observed.Count + scheduledEssentials : null;
        decimal? lifestyle = usableHistory ? NetSpending(residual) / observed.Count + commitments : null;
        decimal? income = usableHistory ? Math.Max(0, residual.Where(l => !l.IsExceptional).Sum(l => l.Income)) / observed.Count +
            normalized.Sum(r => r.Income) : null;
        // No essential classification or no observed essentials is unknown, never infinite freedom.
        if (essentials <= 0) essentials = null;
        if (lifestyle <= 0) lifestyle = null;
        if (income <= 0) income = null;

        var reserve = Reserves(data, accounts, today);
        var cashFlow = CashFlow.Project(data, today, scope);
        var currentCash = cashFlow.Accounts.Sum(a => a.CurrentBalance);
        var included = accounts.Where(a => a.Kind != AccountKind.Investment && a.IncludeInFreedom && a.OpeningDate <= today).ToList();
        var balancesCovered = included.Count > 0 && included.All(a => BankImportCoverage.Covers(data, a.Id,
            a.OpeningDate > firstCurrent ? a.OpeningDate : firstCurrent, today));
        var previousDate = firstCurrent.AddDays(-1);
        var previousCovered = included.Count > 0 && included.All(a => a.OpeningDate <= previousDate &&
            BankImportCoverage.Covers(data, a.Id, firstCurrent.AddMonths(-1), previousDate));
        decimal? change = essentials is > 0 && balancesCovered && previousCovered
            ? (reserve.Usable - Reserves(data, accounts, previousDate).Usable) / essentials.Value : null;
        var freedom = new FreedomMap(reserve.Balance, reserve.Earmarked, reserve.Exceptional, reserve.Usable,
            currentCash, cashFlow.EstimatedSpendable, essentials, lifestyle, income, commitments,
            essentials is > 0 ? reserve.Usable / essentials.Value : null,
            lifestyle is > 0 ? reserve.Usable / lifestyle.Value : null, change,
            data.InsightSettings.BufferTargetMonths, balancesCovered,
            cashFlow.Accounts.Any(a => a.HasUncertainty || a.BufferAtRisk));
        var unclassified = usableHistory
            ? NetSpending(observed.SelectMany(m => m.Lines).Where(l => l.CategoryId == null)) / observed.Count : 0;
        return new(today, months, freedom, Fingerprint(observed, income, lifestyle, commitments), observed.Count, unclassified);
    }

    public static decimal MonthlyAmount(RecurringPayment payment) => payment.Amount / (payment.Frequency switch {
        PaymentFrequency.Quarterly => 3m, PaymentFrequency.Yearly => 12m, _ => 1m
    });

    private sealed record RecurringBaseline(decimal Income, decimal Spending, decimal Essentials);

    private static bool IsDiscretionary(AccountingLine line) => !line.IsExceptional && !line.IsEssential &&
        line.Transaction.Kind == TransactionKind.Normal && line.Transaction.RecurringPaymentId == null;

    private static decimal NetSpending(IEnumerable<AccountingLine> lines) => lines.Where(l => !l.IsExceptional)
        .GroupBy(l => l.CategoryId).Sum(g => Math.Max(0, g.Sum(l => l.Spending)));

    private static (decimal Balance, decimal Earmarked, decimal Exceptional, decimal Usable) Reserves(
        HouseholdData data, IReadOnlyList<Account> accounts, DateOnly date)
    {
        var eligible = accounts.Where(a => a.Kind != AccountKind.Investment && a.IncludeInFreedom && a.OpeningDate <= date).ToList();
        var scope = eligible.Select(a => a.Id).ToHashSet();
        var balance = eligible.Sum(a => Finance.Balance(data, a, date)); // Retain overdrafts.
        var earmarked = eligible.Sum(a => a.EarmarkedAmount);
        var end = date.AddYears(1);
        var exceptional = TransactionAccounting.Lines(data, data.Transactions.Where(t => scope.Contains(t.AccountId) && t.Date > date && t.Date <= end))
            .Where(l => l.IsExceptional && l.Spending > 0).Sum(l => l.Spending);
        foreach (var payment in data.RecurringPayments.Where(r => scope.Contains(r.AccountId) && r.Amount < 0 &&
            data.Categories.Any(c => c.Id == r.CategoryId && c.Purpose == CategoryPurpose.Normal && c.IsExceptional)))
            exceptional += RecurringSchedule.Occurrences(payment, date.AddDays(1), end)
                .Count(due => !RecurringSchedule.IsLinked(data, payment, due)) * -payment.Amount;
        return (balance, earmarked, exceptional, Math.Max(0, balance - earmarked - exceptional));
    }

    private static MoneyFingerprint Fingerprint(IReadOnlyList<InsightMonth> months, decimal? income,
        decimal? lifestyle, decimal commitments)
    {
        if (months.Count < MinimumMonths) return new(null, null, null, null, null, null, []);
        var purchases = months.SelectMany(m => m.Lines).Where(l => IsDiscretionary(l) && l.Spending > 0).ToList();
        var total = purchases.Sum(l => l.Spending);
        var days = months.Sum(m => DateTime.DaysInMonth(m.Month.Year, m.Month.Month));
        var daily = purchases.GroupBy(l => l.Transaction.Date).ToList();
        decimal? consistency = months.Sum(m => m.Discretionary) > 0 ? 1m / (1m + Variation(months.Select(m => m.Discretionary))) : null;
        decimal? bursts = total > 0 ? daily.Where(g => g.Sum(l => l.Spending) > 2 * total / days).Sum(g => g.Sum(l => l.Spending)) / total : null;
        var concentration = months.Where(m => m.Discretionary > 0).Select(m => {
            var entries = m.Lines.Where(l => IsDiscretionary(l) && l.Spending > 0).GroupBy(l => l.Transaction.Id)
                .Select(g => g.Sum(l => l.Spending)).OrderDescending().ToList();
            return entries.Count > 0 ? (decimal?)(entries.Take(3).Sum() / entries.Sum()) : null;
        }).Where(value => value != null).ToList();
        var discoveries = new List<InsightDiscovery>();
        var recent = months.TakeLast(6).ToList();
        var contiguous = recent.Count == 6 && recent.Zip(recent.Skip(1)).All(pair => pair.First.Month.AddMonths(1) == pair.Second.Month);
        var recentPurchases = recent.SelectMany(m => m.Lines).Where(l => IsDiscretionary(l) && l.Spending > 0).ToList();
        if (contiguous && recentPurchases.Select(l => l.Transaction.Id).Distinct().Count() >= 20)
        {
            var late = recentPurchases.Where(l => l.Transaction.Date.Day > DateTime.DaysInMonth(l.Transaction.Date.Year, l.Transaction.Date.Month) - 5).ToList();
            var earlyDays = recent.Sum(m => DateTime.DaysInMonth(m.Month.Year, m.Month.Month) - 5);
            var earlyTotal = recentPurchases.Except(late).Sum(l => l.Spending);
            var lateDaily = late.Sum(l => l.Spending) / 30m;
            var earlyDaily = earlyTotal / earlyDays;
            if (late.Select(l => l.Transaction.Id).Distinct().Count() >= 6 && earlyTotal >= 50 && earlyDaily > 0 && lateDaily / earlyDaily >= 1.2m)
                discoveries.Add(Discovery("FingerprintLateMonth", lateDaily / earlyDaily - 1, recent, recentPurchases));
            var previousVariation = Variation(recent.Take(3).Select(m => m.Discretionary));
            var latestVariation = Variation(recent.TakeLast(3).Select(m => m.Discretionary));
            if (recent.All(m => m.Income > 0 && m.Discretionary > 0) && Variation(recent.Select(m => m.Income)) <= 0.1m && latestVariation > previousVariation + 0.15m)
                discoveries.Add(Discovery("FingerprintLessPredictable", latestVariation - previousVariation, recent,
                    recent.SelectMany(m => m.Lines).Where(l => IsDiscretionary(l) && l.Spending != 0)));
        }
        return new(consistency, bursts, (decimal)months.Count(m => m.Savings > 0) / months.Count,
            income is > 0 ? commitments / income.Value : null,
            concentration.Count > 0 ? concentration.Average() : null,
            income is > 0 && lifestyle != null ? (income.Value - lifestyle.Value) / income.Value : null, discoveries);
    }

    private static InsightDiscovery Discovery(string key, decimal value, IReadOnlyList<InsightMonth> months,
        IEnumerable<AccountingLine> lines) => new(key, value, months[0].Month, months[^1].Month.AddMonths(1).AddDays(-1),
            lines.GroupBy(l => l.Transaction.Id).Select(g => new InsightEvidence(g.First().Transaction, g.Sum(l => l.Spending)))
                .OrderByDescending(e => e.Transaction.Date).ThenBy(e => e.Transaction.Id).ToList());

    private static decimal Variation(IEnumerable<decimal> values)
    {
        var items = values.ToList();
        var mean = items.Average();
        if (mean <= 0) return 0;
        return (decimal)Math.Sqrt(items.Average(value => Math.Pow((double)((value - mean) / mean), 2)));
    }
}
