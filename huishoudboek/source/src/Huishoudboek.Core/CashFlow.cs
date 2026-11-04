namespace Huishoudboek.Core;

public sealed record CashFlowEvent(Guid AccountId, DateOnly Date, DateOnly DueDate, string Name, decimal Amount,
    Guid? TransactionId, Guid? RecurringPaymentId, bool IsOverdue = false);
public sealed record DailyBalance(DateOnly Date, decimal OpeningBalance, decimal Income, decimal Payments,
    decimal VariableSpending, decimal LowestBalance, decimal ClosingBalance);
public sealed record AccountCashFlow(Account Account, decimal CurrentBalance, DateOnly? NextIncomeDate,
    DateOnly SpendableUntil, decimal EstimatedSpendable, decimal LowestBalance, DateOnly LowestDate,
    decimal DailyVariableEstimate, DateOnly? StatementThrough, DateOnly? LastImportDate,
    bool StaleImports, bool LimitedHistory, IReadOnlyList<string> Warnings,
    IReadOnlyList<CashFlowEvent> Events, IReadOnlyList<DailyBalance> Days)
{
    public decimal BillsUntil(DateOnly date) => -Events.Where(e => e.Amount < 0 && e.Date <= date).Sum(e => e.Amount);
    public bool BufferAtRisk => LowestBalance < Account.MinimumCashBuffer;
    public bool HasUncertainty => Warnings.Count > 0;
    public bool CanAfford(decimal amount) => amount > 0 && amount <= EstimatedSpendable && LowestBalance - amount >= Account.MinimumCashBuffer;
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
        var searchEnd = today.AddDays(IncomeSearchDays);
        var accounts = data.Accounts.Where(a => a.Kind == AccountKind.Current && a.OpeningDate <= today &&
            (scope == null || scope.Contains(a.Id))).ToList();
        return new(today, accounts.Select(a => ProjectAccount(data, a, today, searchEnd, minimumDays, transfers)).ToList());
    }

    private static AccountCashFlow ProjectAccount(HouseholdData data, Account account, DateOnly today,
        DateOnly searchEnd, int minimumDays, IReadOnlySet<Guid> transfers)
    {
        var warnings = new List<string>();
        var events = new List<CashFlowEvent>();
        bool Variable(Transaction t) => t.Amount < 0 && t.Kind == TransactionKind.Normal &&
            t.RecurringPaymentId == null && !transfers.Contains(t.Id);
        foreach (var t in data.Transactions.Where(t => t.Date > today && t.Date <= searchEnd))
        {
            var effect = Finance.AccountEffect(t, account.Id);
            if (effect != 0) events.Add(new(account.Id, t.Date, t.Date, t.Description, effect, t.Id, t.RecurringPaymentId));
        }
        foreach (var item in data.RecurringPayments.Where(r => r.AccountId == account.Id))
        {
            foreach (var due in RecurringSchedule.Occurrences(item, today.AddDays(1), searchEnd))
                if (!RecurringSchedule.IsLinked(data, item, due)) events.Add(new(account.Id, due, due, item.Name, item.Amount, null, item.Id));
            // Only the latest missed occurrence is reserved: older unlinked history is not multiplied into fictional debt.
            if (RecurringSchedule.LatestDue(item, today) is { } overdue && overdue >= account.OpeningDate &&
                !RecurringSchedule.IsLinked(data, item, overdue))
            {
                if (item.Amount < 0)
                {
                    events.Add(new(account.Id, today.AddDays(1), overdue, item.Name, item.Amount, null, item.Id, true));
                    warnings.Add($"{item.Name}: betaling van {overdue:dd-MM-yyyy} niet gekoppeld; gereserveerd voor morgen. Koppel de bankboeking om dubbeltelling te voorkomen.");
                }
                else warnings.Add($"{item.Name}: inkomen van {overdue:dd-MM-yyyy} niet gekoppeld. Dit achterstallige inkomen wordt niet als beschikbare ontvangst aangenomen; controleer de import of planning.");
            }
        }

        // A future linked bank entry replaces the estimate on its real date, even across month boundaries.
        var transactions = data.Transactions.ToDictionary(t => t.Id);
        var nextIncome = events.Where(e => e.Amount > 0 && (e.TransactionId == null ||
            transactions[e.TransactionId.Value].Kind != TransactionKind.Transfer && !transfers.Contains(e.TransactionId.Value)))
            .Select(e => (DateOnly?)e.Date).Min();
        var until = nextIncome ?? today.AddDays(minimumDays);
        var end = until > today.AddDays(minimumDays) ? until : today.AddDays(minimumDays);
        events = events.Where(e => e.Date <= end).OrderBy(e => e.Date).ThenBy(e => e.Amount).ToList();

        var batches = data.ImportBatches.Where(b => b.AccountId == account.Id && !b.Undone).ToList();
        var batchIds = batches.Select(b => b.Id).ToHashSet();
        var statementThrough = data.Transactions.Where(t => t.AccountId == account.Id && t.Date <= today &&
            t.ImportBatchId is { } id && batchIds.Contains(id)).Select(t => (DateOnly?)t.Date).Max();
        var lastImport = batches.Select(b => (DateOnly?)DateOnly.FromDateTime(b.ImportedAt)).Max();
        var stale = statementThrough == null || lastImport == null ||
            today.DayNumber - statementThrough.Value.DayNumber > StaleAfterDays || today.DayNumber - lastImport.Value.DayNumber > StaleAfterDays;
        if (stale) warnings.Add(statementThrough == null ? "Geen bruikbare bankimport voor deze rekening: actualiteit van het saldo is onbekend." :
            $"Import verouderd of onvoldoende recent: laatste bankboeking {statementThrough:dd-MM-yyyy}, laatste import {lastImport:dd-MM-yyyy}. Niet-geïmporteerde betalingen ontbreken.");

        var historyEnd = statementThrough ?? today;
        var historyStart = historyEnd.AddDays(1 - HistoryDays);
        if (historyStart < account.OpeningDate) historyStart = account.OpeningDate;
        var history = data.Transactions.Where(t => t.AccountId == account.Id && t.Date >= historyStart && t.Date <= historyEnd && Variable(t)).ToList();
        var observedDays = historyEnd.DayNumber - historyStart.DayNumber + 1;
        var dailyVariable = observedDays > 0 ? decimal.Round(-history.Sum(t => t.Amount) / observedDays, 2, MidpointRounding.AwayFromZero) : 0;
        var limited = observedDays < 14 || history.Count < 3;
        if (limited) warnings.Add("Weinig variabele uitgavenhistorie: het daggemiddelde kan noodzakelijke dagelijkse kosten onderschatten.");
        if (account.MinimumCashBuffer == 0) warnings.Add("Geen minimale cashbuffer ingesteld. Stel via Buffer instellen een reserve in voor onverwachte kosten.");
        if (nextIncome == null) warnings.Add($"Geen volgende inkomensdatum bekend binnen {IncomeSearchDays} dagen; bestedingsruimte berekend voor {minimumDays} dagen, niet tot een bekende betaaldag.");

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
            // Explicit future everyday spending occupies the run-rate, rather than being counted twice.
            var explicitVariable = -entries.Where(e => e.TransactionId is { } id && transactions[id].AccountId == account.Id && Variable(transactions[id]))
                .Sum(e => e.Amount);
            var variable = Math.Max(0, dailyVariable - explicitVariable);
            // Conservative within-day ordering: all debits before credits, including on payday.
            var trough = balance - payments - variable;
            if (trough < lowest) { lowest = trough; lowestDate = date; }
            if (date <= until) spendableFloor = Math.Min(spendableFloor, trough);
            days.Add(new(date, balance, income, payments, variable, trough, trough + income));
            balance = trough + income;
        }
        return new(account, current, nextIncome, until, Math.Max(0, spendableFloor - account.MinimumCashBuffer),
            lowest, lowestDate, dailyVariable, statementThrough, lastImport, stale, limited, warnings, events, days);
    }
}
