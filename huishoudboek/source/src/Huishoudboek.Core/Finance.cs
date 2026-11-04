namespace Huishoudboek.Core;

public sealed record CategoryResult(Guid Id, string Name, decimal Budget, decimal Spent)
{
    public decimal Remaining => Budget - Spent;
    public decimal Overspent => Budget > 0 ? Math.Max(0, Spent - Budget) : 0;
    public double Progress => Budget > 0 ? (double)Math.Clamp(Spent / Budget, 0, 1) * 100 : 0;
    public bool IsOverBudget => Budget > 0 && Spent > Budget;
}

public sealed record Forecast(decimal Income, decimal Outflow, decimal Savings,
    decimal VariableEstimate, int OverdueCount, bool LimitedHistory)
{
    public decimal Available => Income - Outflow - Savings;
}

public sealed record SavingsResult(Guid Id, string Name, decimal Target, decimal Saved);

public sealed record MonthlyReport(decimal Income, decimal Outflow, decimal Savings,
    decimal PrincipalRepaid, IReadOnlyList<CategoryResult> Categories, Forecast Forecast)
{
    public IReadOnlyList<SavingsResult> SavingsCategories { get; init; } = [];
    public decimal BeforeSavings => Income - Outflow;
    public decimal Available => Income - Outflow - Savings;
    public decimal ConsumptionExpense => Outflow - PrincipalRepaid;
    public decimal Budget => Categories.Sum(c => c.Budget);
    public decimal Overspent => Categories.Sum(c => c.Overspent);
    public bool IsOverspending => BeforeSavings < 0 || Overspent > 0;
    public bool SavingsExceedSurplus => Savings > 0 && Available < 0;
    public bool NeedsAttention => IsOverspending || SavingsExceedSurplus || Forecast.Available < 0;
}

public sealed record MortgageRow(int Number, DateOnly Date, decimal Payment, decimal Interest,
    decimal Principal, decimal Remaining, bool AssumesUnchangedRate);

public static class Finance
{
    public static decimal Balance(HouseholdData data, Account account, DateOnly asOf)
    {
        if (asOf < account.OpeningDate) return 0;
        // Opening balance is the balance at the START of OpeningDate.
        return account.OpeningBalance + data.Transactions
            .Where(t => t.Date >= account.OpeningDate && t.Date <= asOf)
            .Sum(t => AccountEffect(t, account.Id));
    }

    public static decimal AccountEffect(Transaction t, Guid accountId) =>
        t.AccountId == accountId ? t.Amount :
        t.Kind == TransactionKind.Transfer && t.CounterAccountId == accountId ? -t.Amount : 0;

    public static decimal SecuritiesValue(HouseholdData data, Guid accountId, DateOnly asOf) =>
        data.Valuations.Where(v => v.AccountId == accountId && v.Date <= asOf)
            .OrderByDescending(v => v.Date).Select(v => v.SecuritiesValue).FirstOrDefault();

    public static decimal MortgageBalance(HouseholdData data, DateOnly asOf)
    {
        if (data.Mortgage is not { } m || asOf < m.ReferenceDate) return 0;
        return Math.Max(0, m.ReferencePrincipal - data.Transactions
            .Where(t => t.Kind == TransactionKind.Mortgage && t.Date >= m.ReferenceDate && t.Date <= asOf)
            .Sum(t => t.PrincipalComponent));
    }

    public static MonthlyReport Report(HouseholdData data, int year, int month, DateOnly today,
        IReadOnlySet<Guid>? scope = null)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var cutoff = today < first ? first.AddDays(-1) : today < last ? today : last;
        bool InScope(Transaction t) => scope == null || scope.Contains(t.AccountId);
        var matched = data.TransferMatches.Where(m => m.IsConfirmed)
            .SelectMany(m => new[] { (Id: m.DebitTransactionId, Counter: m.CreditTransactionId), (Id: m.CreditTransactionId, Counter: m.DebitTransactionId) })
            .ToDictionary(p => p.Id, p => p.Counter);
        var transactions = data.Transactions.ToDictionary(t => t.Id);
        var actual = data.Transactions.Where(t => t.Date >= first && t.Date <= cutoff).ToList();
        var ordinary = actual.Where(t => t.Kind != TransactionKind.Transfer && !matched.ContainsKey(t.Id) && InScope(t)).ToList();
        var lines = TransactionAccounting.Lines(data, ordinary).ToList();
        var income = lines.Sum(l => l.Income);
        var outflow = lines.Sum(l => l.Spending);
        var legacySavings = actual.Sum(t => LegacySavingsEffect(data, t, scope, matched, transactions));
        var savings = lines.Sum(l => l.Savings) + legacySavings;
        var principal = ordinary.Where(t => t.Kind == TransactionKind.Mortgage).Sum(t => t.PrincipalComponent);
        var categories = data.Categories.Where(c => c.Purpose == CategoryPurpose.Normal).Select(c => new CategoryResult(c.Id, c.Name,
            data.BudgetOverrides.FirstOrDefault(b => b.CategoryId == c.Id && b.Year == year && b.Month == month)?.Amount
                ?? c.MonthlyBudget,
            lines.Where(l => l.CategoryId == c.Id).Sum(l => l.Spending)))
            .Where(c => c.Budget > 0 || c.Spent != 0).OrderByDescending(c => c.Overspent)
            .ThenByDescending(c => c.Spent).ToList();
        var uncategorized = lines.Where(l => l.CategoryId == null).Sum(l => l.Spending);
        if (uncategorized != 0) categories.Add(new(Guid.Empty, "Zonder categorie", 0, uncategorized));
        var savingsCategories = data.Categories.Where(c => c.Purpose == CategoryPurpose.Savings)
            .Select(c => new SavingsResult(c.Id, c.Name,
                data.BudgetOverrides.FirstOrDefault(b => b.CategoryId == c.Id && b.Year == year && b.Month == month)?.Amount ?? c.MonthlyBudget,
                lines.Where(l => l.CategoryId == c.Id).Sum(l => l.Savings))).ToList();
        if (legacySavings != 0) savingsCategories.Add(new(Guid.Empty, "Eerder vastgelegde overboekingen", 0, legacySavings));

        var future = data.Transactions.Where(t => t.Date >= first && t.Date > cutoff && t.Date <= last).ToList();
        var futureOrdinary = future.Where(t => t.Kind != TransactionKind.Transfer && !matched.ContainsKey(t.Id) && InScope(t)).ToList();
        var futureLines = TransactionAccounting.Lines(data, futureOrdinary).ToList();
        var plannedIncome = futureLines.Sum(l => l.Income);
        var plannedOutflow = futureLines.Sum(l => l.Spending);
        var plannedSavings = futureLines.Sum(l => l.Savings) + future.Sum(t => LegacySavingsEffect(data, t, scope, matched, transactions));
        var overdue = 0;
        if (today <= last)
        {
            foreach (var recurring in data.RecurringPayments.Where(r => scope == null || scope.Contains(r.AccountId)))
            {
                var due = Occurrence(recurring, year, month);
                if (due == null) continue;
                // Explicit links are authoritative; matching is deliberately conservative.
                if (RecurringSchedule.IsLinked(data, recurring, due.Value)) continue;
                if (due <= cutoff) overdue++;
                var purpose = data.Categories.FirstOrDefault(c => c.Id == recurring.CategoryId)?.Purpose ?? CategoryPurpose.Normal;
                if (purpose == CategoryPurpose.Savings && data.Accounts.Any(a => a.Id == recurring.AccountId && a.Kind == AccountKind.Current))
                    plannedSavings -= recurring.Amount;
                else if (purpose == CategoryPurpose.Normal)
                {
                    if (recurring.Amount > 0) plannedIncome += recurring.Amount;
                    else plannedOutflow -= recurring.Amount;
                }
            }
        }

        var elapsed = cutoff < first ? 0 : cutoff.Day;
        var remainingDays = DateTime.DaysInMonth(year, month) - elapsed;
        var observedVariable = Math.Max(0, lines.Where(l => l.Transaction.Kind != TransactionKind.Mortgage &&
            l.Transaction.RecurringPaymentId == null).Sum(l => l.Spending));
        var estimate = elapsed > 0 && remainingDays > 0
            ? decimal.Round(observedVariable / elapsed * remainingDays, 2) : 0;
        // Future variable entries occupy part of the run-rate estimate, not an extra layer on top.
        var explicitVariable = futureLines.Where(l => l.Amount < 0 && l.Transaction.Kind != TransactionKind.Mortgage &&
            l.Transaction.RecurringPaymentId == null).Sum(l => l.Spending);
        estimate = Math.Max(0, estimate - explicitVariable);
        var forecast = new Forecast(income + plannedIncome, outflow + plannedOutflow + estimate,
            savings + plannedSavings, estimate, overdue, elapsed < 7 || actual.Count < 3);
        return new(income, outflow, savings, principal, categories, forecast) { SavingsCategories = savingsCategories };
    }

    public static decimal ProjectedCurrentBalance(HouseholdData data, int year, int month, DateOnly today,
        IReadOnlySet<Guid>? scope = null)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var asOf = today < last ? today : last;
        var current = data.Accounts.Where(a => a.Kind == AccountKind.Current && (scope == null || scope.Contains(a.Id))).ToList();
        var ids = current.Select(a => a.Id).ToHashSet();
        var balance = current.Sum(a => Balance(data, a, asOf));
        if (today > last) return balance;
        var days = last.DayNumber - today.DayNumber;
        if (days == 0) return balance;
        if (days <= CashFlow.IncomeSearchDays)
            return CashFlow.Project(data, today, ids, days).Accounts.Sum(a => a.Days.Single(d => d.Date == last).ClosingBalance);
        balance += data.Transactions.Where(t => t.Date >= first && t.Date > today && t.Date <= last)
            .Sum(t => current.Sum(a => AccountEffect(t, a.Id)));
        balance += data.RecurringPayments.Where(r => ids.Contains(r.AccountId) && Occurrence(r, year, month) is { } due &&
            !RecurringSchedule.IsLinked(data, r, due)).Sum(r => r.Amount);
        return balance - Report(data, year, month, today, ids).Forecast.VariableEstimate;
    }

    // Net contributions from daily-use accounts into savings/investments, not household expenditure.
    public static decimal SavingsEffect(HouseholdData data, Transaction t, IReadOnlySet<Guid>? scope = null)
    {
        var matched = data.TransferMatches.Where(m => m.IsConfirmed)
            .SelectMany(m => new[] { (Id: m.DebitTransactionId, Counter: m.CreditTransactionId), (Id: m.CreditTransactionId, Counter: m.DebitTransactionId) })
            .ToDictionary(p => p.Id, p => p.Counter);
        return LegacySavingsEffect(data, t, scope, matched, data.Transactions.ToDictionary(entry => entry.Id)) +
            (scope == null || scope.Contains(t.AccountId) ? TransactionAccounting.Lines(data, new[] { t }).Sum(l => l.Savings) : 0);
    }

    private static decimal LegacySavingsEffect(HouseholdData data, Transaction t, IReadOnlySet<Guid>? scope,
        IReadOnlyDictionary<Guid, Guid> matched, IReadOnlyDictionary<Guid, Transaction> transactions)
    {
        if (matched.TryGetValue(t.Id, out var counterpartId))
        {
            var counterpart = transactions[counterpartId];
            var account = data.Accounts.First(a => a.Id == t.AccountId);
            var other = data.Accounts.First(a => a.Id == counterpart.AccountId);
            // Count once on the current account's bank date, even across month boundaries.
            return account.Kind == AccountKind.Current && other.Kind != AccountKind.Current &&
                (scope == null || scope.Contains(account.Id) || scope.Contains(other.Id)) ? -t.Amount : 0;
        }
        if (t.Kind != TransactionKind.Transfer || t.CounterAccountId == null) return 0;
        var from = data.Accounts.First(a => a.Id == t.AccountId);
        var to = data.Accounts.First(a => a.Id == t.CounterAccountId);
        if (from.Kind == AccountKind.Current && to.Kind != AccountKind.Current &&
            (scope == null || scope.Contains(from.Id) || scope.Contains(to.Id))) return -t.Amount;
        if (from.Kind != AccountKind.Current && to.Kind == AccountKind.Current &&
            (scope == null || scope.Contains(from.Id) || scope.Contains(to.Id))) return t.Amount;
        return 0;
    }

    public static DateOnly? Occurrence(RecurringPayment item, int year, int month)
        => RecurringSchedule.Occurrence(item, year, month);

    public static void ApplySuggestions(HouseholdData data, Transaction transaction, bool suggestCategory = true)
        => ApplySuggestions(data, [transaction], suggestCategory);

    public static void ApplySuggestions(HouseholdData data, IReadOnlyList<Transaction> transactions, bool suggestCategory = true)
    {
        var transfers = TransferMatching.ConfirmedTransactionIds(data);
        foreach (var transaction in transactions)
        {
            if (transfers.Contains(transaction.Id) || TransactionAccounting.HasDetails(data, transaction.Id)) continue;
            // Import-save rechecks must preserve a deliberately empty category too.
            if (transaction.Kind == TransactionKind.Transfer)
            {
                if (suggestCategory) TransferCategories.ApplyDefault(data, transaction);
                continue;
            }
            if (suggestCategory) transaction.CategoryId ??= Categorization.Match(data, transaction.Description)?.CategoryId;
        }

        if (transactions.Count == 0 || data.RecurringPayments.Count == 0) return;
        var selectedIds = transactions.Select(t => t.Id).ToHashSet();
        // Include pending rows together and existing entries as competitors, but only update
        // the selected transactions. Use the same configured margins as review proposals.
        var entries = transactions.Concat(data.Transactions).DistinctBy(t => t.Id);
        var matches = RecurringSchedule.UnambiguousCandidates(data, entries);
        foreach (var candidate in matches.Where(c => selectedIds.Contains(c.Transaction.Id)))
        {
            var transaction = candidate.Transaction;
            var match = candidate.Payment;
            transaction.RecurringPaymentId = match.Id;
            transaction.RecurringOccurrenceDate = candidate.DueDate;
            if (suggestCategory) transaction.CategoryId ??= match.CategoryId;
            if (match.IsMortgage && transaction.Amount < 0) transaction.Kind = TransactionKind.Mortgage;
        }
    }

    public static IReadOnlyList<MortgageRow> MortgageSchedule(Mortgage mortgage, decimal balance,
        DateOnly asOf, int? months = null)
    {
        var rows = new List<MortgageRow>();
        var rate = mortgage.AnnualInterestPercent / 1200m;
        var first = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(1);
        var elapsed = Math.Max(0, (asOf.Year - mortgage.ReferenceDate.Year) * 12 + asOf.Month - mortgage.ReferenceDate.Month);
        var count = months ?? Math.Max(0, mortgage.RemainingMonths - elapsed);
        for (var i = 0; i < count && balance > 0; i++)
        {
            var date = first.AddMonths(i).AddMonths(1).AddDays(-1);
            var interest = decimal.Round(balance * rate, 2, MidpointRounding.AwayFromZero);
            var payment = Math.Min(mortgage.MonthlyPayment, balance + interest);
            var principal = payment - interest;
            balance = decimal.Round(balance - principal, 2);
            rows.Add(new(i + 1, date, payment, interest, principal, balance,
                mortgage.FixedRateUntil is { } end && date > end));
            if (principal <= 0) break; // Warn the user rather than produce a misleading amortization schedule.
        }
        return rows;
    }
}
