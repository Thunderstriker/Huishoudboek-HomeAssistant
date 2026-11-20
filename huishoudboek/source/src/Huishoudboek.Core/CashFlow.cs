namespace Huishoudboek.Core;

public sealed record CashFlowEvent(Guid AccountId, DateOnly Date, DateOnly DueDate, string Name, decimal Amount,
    Guid? TransactionId, Guid? RecurringPaymentId, bool IsOverdue = false, bool IsUncertain = false)
{
    public DateOnly? PurchaseDate { get; init; }
}
public sealed record DailyBalance(DateOnly Date, decimal OpeningBalance, decimal Income, decimal Payments,
    decimal VariableSpending, decimal LowestBalance, decimal ClosingBalance);
public sealed record AccountCashFlow(Account Account, decimal CurrentBalance, DateOnly? NextIncomeDate,
    DateOnly SpendableUntil, decimal EstimatedSpendable, decimal LowestBalance, DateOnly LowestDate,
    decimal DailyVariableEstimate, DateOnly? StatementThrough, DateOnly? LastImportDate,
    bool StaleImports, bool LimitedHistory, IReadOnlyList<string> Warnings,
    IReadOnlyList<CashFlowEvent> Events, IReadOnlyList<DailyBalance> Days)
{
    public int AwaitingBankDataCount { get; init; }
    public int PaymentsToReviewCount { get; init; }
    public SpendingAnalysis Learning { get; init; } = new(0, []);
    public IReadOnlyList<ExpectedSpending> ExpectedSpending { get; init; } = [];
    public decimal DailyUnscheduledEstimate { get; init; }
    public decimal PendingHabitReserve => ExpectedSpending.Where(e => e.IsPending).Sum(e => e.Amount);
    public decimal UncertainReserve => -Events.Where(e => e.IsUncertain && e.Amount < 0).Sum(e => e.Amount);
    public decimal BillsUntil(DateOnly date) => -Events.Where(e => e.Amount < 0 && !e.IsUncertain && e.Date <= date).Sum(e => e.Amount);
    public bool BufferAtRisk => LowestBalance < Account.MinimumCashBuffer + Account.EarmarkedAmount;
    public bool HasUncertainty => Warnings.Count > 0 || AwaitingBankDataCount > 0 || PaymentsToReviewCount > 0 || PendingHabitReserve > 0;
    public bool CanAfford(decimal amount) => amount > 0 && amount <= EstimatedSpendable &&
        LowestBalance - amount >= Account.MinimumCashBuffer + Account.EarmarkedAmount;
}
public sealed record CashFlowOverview(DateOnly Today, IReadOnlyList<AccountCashFlow> Accounts)
{
    // These amounts are not interchangeable: the spending account always decides affordability.
    public decimal EstimatedSpendable => Accounts.Sum(a => a.EstimatedSpendable);
    public decimal Bills7Days => Accounts.Sum(a => a.BillsUntil(Today.AddDays(7)));
    public decimal Bills30Days => Accounts.Sum(a => a.BillsUntil(Today.AddDays(30)));
}

/// <summary>Account-local cash planning. No implicit transfers, no securities or savings as spendable cash.</summary>
public static class CashFlow
{
    public const int StaleAfterDays = 7;
    public const int HistoryDays = 90;
    public const int IncomeSearchDays = 366;

    public static CashFlowOverview Project(HouseholdData data, DateOnly today, IReadOnlySet<Guid>? scope = null, int minimumDays = 30)
    {
        if (minimumDays is < 1 or > IncomeSearchDays) throw new ArgumentOutOfRangeException(nameof(minimumDays));
        var transfers = TransferMatching.ConfirmedTransactionIds(data);
        var candidates = RecurringSchedule.Candidates(data, today);
        var searchEnd = today.AddDays(IncomeSearchDays);
        var accounts = data.Accounts.Where(a => a.Kind == AccountKind.Current && a.OpeningDate <= today &&
            (scope == null || scope.Contains(a.Id))).ToList();
        return new(today, accounts.Select(a => ProjectAccount(data, a, today, searchEnd, minimumDays, transfers, candidates)).ToList());
    }

    private static AccountCashFlow ProjectAccount(HouseholdData data, Account account, DateOnly today,
        DateOnly searchEnd, int minimumDays, IReadOnlySet<Guid> transfers, IReadOnlyList<RecurringCandidate> candidates)
    {
        var warnings = new List<string>();
        var awaitingBankData = 0;
        var reviewPayments = 0;
        var events = new List<CashFlowEvent>();
        var forecastCategories = SpendingForecast.CategoryIds(data);
        // Classification changes reporting, never cash. Only the ordinary expense part
        // of a bank debit belongs in the everyday-spending estimate.
        var spending = TransactionAccounting.Lines(data, data.Transactions.Where(t => t.Amount < 0))
            .Where(l => l.CategoryId is { } category && forecastCategories.Contains(category))
            .GroupBy(l => l.Transaction.Id).ToDictionary(g => g.Key, g => Math.Max(0, g.Sum(l => l.TypicalSpending)));
        bool Variable(Transaction t) => t.Amount < 0 && t.Kind == TransactionKind.Normal &&
            t.RecurringPaymentId == null && !transfers.Contains(t.Id) && spending.GetValueOrDefault(t.Id) > 0;
        foreach (var t in data.Transactions.Where(t => t.Date > today && t.Date <= searchEnd))
        {
            var effect = Finance.AccountEffect(t, account.Id);
            if (effect != 0) events.Add(new(account.Id, t.Date, t.Date, TransactionRecognition.DisplayName(data, t), effect, t.Id, t.RecurringPaymentId));
        }
        foreach (var item in data.RecurringPayments.Where(r => r.AccountId == account.Id))
        {
            foreach (var due in RecurringSchedule.Occurrences(item, today.AddDays(1), searchEnd))
                if (!RecurringSchedule.IsLinked(data, item, due)) events.Add(new(account.Id, due, due, item.Name, item.Amount, null, item.Id));
            // A conservative reserve is not proof of an unpaid bill or a known payment date.
            // Only the latest unresolved occurrence is reserved, never accumulated historical debt.
            if (RecurringSchedule.LatestDue(item, today) is { } overdue && overdue >= account.OpeningDate &&
                !RecurringSchedule.IsLinked(data, item, overdue))
            {
                var status = RecurringSchedule.Status(data, item, overdue, today, candidates);
                if (status == RecurringPaymentStatus.AwaitingBankData) awaitingBankData++;
                else reviewPayments++;
                if (item.Amount < 0)
                {
                    // Reserve at the start of the projection internally; presentation must not claim 'due tomorrow'.
                    events.Add(new(account.Id, today.AddDays(1), overdue, item.Name, item.Amount, null, item.Id,
                        status == RecurringPaymentStatus.NotFound, true));
                }
            }
        }
        if (reviewPayments > 0) warnings.Add($"{reviewPayments} geplande betaling(en) te controleren. Bekijk herkenningsvoorstellen en betalingen zonder match bij Vaste posten.");

        // A future linked bank entry replaces the estimate on its real date, even across month boundaries.
        var transactions = data.Transactions.ToDictionary(t => t.Id);
        var incomeIds = TransactionAccounting.Lines(data, data.Transactions.Where(t => t.Date > today && t.Date <= searchEnd))
            .Where(l => l.Income > 0 && !l.IsExceptional).Select(l => l.Transaction.Id).ToHashSet();
        var nextIncome = events.Where(e => e.Amount > 0 && (e.TransactionId == null ||
            incomeIds.Contains(e.TransactionId.Value)) && (e.RecurringPaymentId == null ||
            data.RecurringPayments.Any(r => r.Id == e.RecurringPaymentId &&
                 (r.CategoryId == null || data.Categories.Any(c => c.Id == r.CategoryId && c.Purpose == CategoryPurpose.Normal && !c.IsExceptional)))))
            .Select(e => (DateOnly?)e.Date).Min();
        var until = nextIncome ?? today.AddDays(minimumDays);
        var end = until > today.AddDays(minimumDays) ? until : today.AddDays(minimumDays);

        var batches = data.ImportBatches.Where(b => b.AccountId == account.Id && !b.Undone).ToList();
        var statementThrough = FirstWeek.ImportedThrough(data, account.Id, today);
        var lastImport = batches.Select(b => (DateOnly?)DateOnly.FromDateTime(b.ImportedAt)).Max();
        var confirmedThrough = BankImportCoverage.ConfirmedThrough(data, account.Id);
        var stale = statementThrough == null || today.DayNumber - statementThrough.Value.DayNumber > StaleAfterDays ||
            (!(confirmedThrough >= statementThrough) &&
             (lastImport == null || today.DayNumber - lastImport.Value.DayNumber > StaleAfterDays));
        if (stale) warnings.Add(statementThrough == null ? "Geen bruikbare bankimport voor deze rekening: actualiteit van het saldo is onbekend." :
            $"Import verouderd of onvoldoende recent: laatste bankboeking {statementThrough:dd-MM-yyyy}, laatste import {lastImport:dd-MM-yyyy}. Niet-geïmporteerde betalingen ontbreken.");

        var historyEnd = statementThrough ?? today;
        var historyStart = historyEnd.AddDays(1 - HistoryDays);
        if (historyStart < account.OpeningDate) historyStart = account.OpeningDate;
        var history = data.Transactions.Where(t => t.AccountId == account.Id && t.Date >= historyStart && t.Date <= historyEnd && Variable(t)).ToList();
        var observedDays = historyEnd.DayNumber - historyStart.DayNumber + 1;
        var dailyVariable = observedDays > 0 ? decimal.Round(history.Sum(t => spending[t.Id]) / observedDays, 2, MidpointRounding.AwayFromZero) : 0;
        var limited = forecastCategories.Count > 0 && (observedDays < 14 || history.Count < 3);
        if (limited) warnings.Add("Weinig variabele uitgavenhistorie: het daggemiddelde kan noodzakelijke dagelijkse kosten onderschatten.");
        if (account.MinimumCashBuffer == 0) warnings.Add("Geen minimale cashbuffer ingesteld. Stel via Buffer instellen een reserve in voor onverwachte kosten.");
        if (nextIncome == null) warnings.Add($"Geen volgende inkomensdatum bekend binnen {IncomeSearchDays} dagen; bestedingsruimte berekend voor {minimumDays} dagen, niet tot een bekende betaaldag.");

        var learning = SpendingLearning.Analyze(data, account, today, historyStart, historyEnd);
        if (learning.Patterns.Any(p => p.Preference?.Enabled == true && !p.Ready))
            warnings.Add("Een geaccepteerd bestedingspatroon heeft onvoldoende bewijs en is tijdelijk niet toegepast. Controleer afschriftdekking en aankoopdatums.");
        if (learning.Patterns.Any(p => p.Active && !p.Strong))
            warnings.Add("De prognose gebruikt een voorlopig bestedingspatroon. Controleer de bewijsperiode, aankoopdagen en bedragvariatie.");
        var activePatterns = learning.Patterns.Where(p => p.Active).ToDictionary(p => p.CategoryId);
        var active = activePatterns.Keys.ToHashSet();
        var residual = Math.Max(0, decimal.Round(dailyVariable - learning.Patterns.Where(p => p.Active).Sum(p => p.BaselineDaily), 2, MidpointRounding.AwayFromZero));
        var variableLines = active.Count > 0 ? SpendingLearning.VariableLines(data, account.Id).ToLookup(l => l.Transaction.Id) : null;
        var purchaseDates = new Dictionary<Guid, DateOnly?>();
        bool UsesDatedPattern(AccountingLine line) => line.CategoryId is { } category &&
            activePatterns.TryGetValue(category, out var pattern) && purchaseDates.GetValueOrDefault(line.Transaction.Id) is { } date &&
            pattern.AmountOn(date) > 0;
        if (active.Count > 0)
        {
            // Planning protects money from the known purchase date, while bank records keep their booking date.
            events = events.Select(e =>
            {
                if (e.TransactionId is not { } id || !variableLines![id].Any(l => l.CategoryId is { } category && active.Contains(category))) return e;
                var purchase = SpendingLearning.PurchaseDate(data, transactions[id]).Date;
                purchaseDates[id] = purchase;
                return purchase is { } date ? e with { Date = date > today ? date : today.AddDays(1), PurchaseDate = date } : e;
            }).ToList();
        }
        events = events.Where(e => e.Date <= end).OrderBy(e => e.Date).ThenBy(e => e.Amount).ToList();
        var expected = SpendingLearning.Project(data, learning, today, end).ToList();
        if (active.Count > 0)
        {
            // An undated future bank entry still occupies the category's spending budget.
            // Offset nearby future expectations without inventing a purchase date or bank link.
            foreach (var group in events.Where(e => e.TransactionId != null).GroupBy(e => e.Date))
            {
                var explicitLines = group.SelectMany(e => variableLines![e.TransactionId!.Value]).ToList();
                var residualCapacity = Math.Max(0, residual - explicitLines.Where(l => l.CategoryId is not { } c || !active.Contains(c) ||
                    purchaseDates.GetValueOrDefault(l.Transaction.Id).HasValue && !UsesDatedPattern(l)).Sum(l => l.TypicalSpending));
                foreach (var category in explicitLines.Where(l => l.CategoryId is { } c && active.Contains(c) &&
                    !purchaseDates.GetValueOrDefault(l.Transaction.Id).HasValue).GroupBy(l => l.CategoryId!.Value))
                {
                    var amount = category.Sum(l => l.TypicalSpending);
                    var coveredByResidual = Math.Min(amount, residualCapacity);
                    residualCapacity -= coveredByResidual; amount -= coveredByResidual;
                    for (var i = 0; i < expected.Count && amount > 0; i++)
                    {
                        var estimate = expected[i];
                        if (estimate.IsPending || estimate.CategoryId != category.Key || estimate.Date > group.Key ||
                            estimate.Date < group.Key.AddDays(-SpendingLearning.BookingWindowDays)) continue;
                        var used = Math.Min(amount, estimate.Amount);
                        expected[i] = estimate with { Amount = estimate.Amount - used }; amount -= used;
                    }
                }
            }
            expected.RemoveAll(e => e.Amount == 0);
        }
        var pendingReserve = expected.Where(e => e.IsPending).Sum(e => e.Amount);
        var plannedByDate = expected.Where(e => !e.IsPending).ToLookup(e => e.Date);
        var appliedSpending = expected.Where(e => e.IsPending).ToList();

        var balance = Finance.Balance(data, account, today);
        var current = balance;
        var lowest = balance;
        var lowestDate = today;
        var spendableFloor = balance;
        var days = new List<DailyBalance>();
        var byDate = events.ToLookup(e => e.Date);
        for (var date = today.AddDays(1); date <= end; date = date.AddDays(1))
        {
            var entries = byDate[date].ToList();
            var income = entries.Where(e => e.Amount > 0).Sum(e => e.Amount);
            var payments = -entries.Where(e => e.Amount < 0).Sum(e => e.Amount);
            if (date == today.AddDays(1)) payments += pendingReserve;
            // Explicit future everyday spending occupies the run-rate, rather than being counted twice.
            var explicitVariable = entries.Where(e => e.TransactionId is { } id && transactions[id].AccountId == account.Id && Variable(transactions[id]))
                .Sum(e => spending[e.TransactionId!.Value]);
            decimal variable;
            if (active.Count == 0) variable = Math.Max(0, dailyVariable - explicitVariable);
            else
            {
                var explicitLines = entries.Where(e => e.TransactionId != null).SelectMany(e => variableLines![e.TransactionId!.Value]).ToList();
                var extra = explicitLines.Where(l => !UsesDatedPattern(l)).Sum(l => l.TypicalSpending);
                variable = Math.Max(0, residual - extra);
                foreach (var estimate in plannedByDate[date])
                {
                    var recorded = explicitLines.Any(l => l.CategoryId == estimate.CategoryId &&
                        purchaseDates.GetValueOrDefault(l.Transaction.Id) == date);
                    var topup = recorded ? 0 : estimate.Amount;
                    if (topup > 0) appliedSpending.Add(estimate with { Amount = topup });
                    variable += topup;
                }
            }
            // Conservative within-day ordering: all debits before credits, including on payday.
            var trough = balance - payments - variable;
            if (trough < lowest) { lowest = trough; lowestDate = date; }
            if (date <= until) spendableFloor = Math.Min(spendableFloor, trough);
            days.Add(new(date, balance, income, payments, variable, trough, trough + income));
            balance = trough + income;
        }
        return new(account, current, nextIncome, until, Math.Max(0, spendableFloor - account.MinimumCashBuffer - account.EarmarkedAmount),
            lowest, lowestDate, dailyVariable, statementThrough, lastImport, stale, limited, warnings, events, days)
             { AwaitingBankDataCount = awaitingBankData, PaymentsToReviewCount = reviewPayments,
                 Learning = learning, ExpectedSpending = appliedSpending, DailyUnscheduledEstimate = active.Count > 0 ? residual : dailyVariable };
    }
}
