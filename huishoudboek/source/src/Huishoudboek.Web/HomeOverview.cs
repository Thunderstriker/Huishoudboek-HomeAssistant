using Huishoudboek.Core;
using CashFlowProjection = Huishoudboek.Core.CashFlow;

namespace Huishoudboek.Web;

public sealed record HomeAccount(Account Account, AccountSummary Balance, DateOnly? ImportedThrough, bool StaleImports)
{
    public DateTime? LastImportedAt { get; init; }
}
public sealed record HomeTransaction(Transaction Transaction, Guid AccountId, decimal Amount);

/// <summary>Actual balances and recent activity always use today; only the monthly report uses the month selector.</summary>
public sealed record HomeOverview(DateOnly Today, MonthlyReport Month, IReadOnlyList<HomeAccount> Accounts,
    CashFlowOverview CashFlow, IReadOnlyList<HomeTransaction> Recent, int UncategorizedCount)
{
    public IReadOnlyList<CashFlowEvent> Upcoming => CashFlow.Accounts.SelectMany(a => a.Events)
        .Where(e => e.Amount < 0 && !e.IsUncertain && e.Date > Today && e.Date <= Today.AddDays(7))
        .OrderBy(e => e.Date).ThenBy(e => e.Name).ThenBy(e => e.AccountId).ToArray();

    public static HomeOverview Create(HouseholdData data, int year, int month, DateOnly today, Guid? ownerId = null)
    {
        var scope = data.Accounts.Where(a => ownerId == null || a.OwnerIds.Contains(ownerId.Value)).Select(a => a.Id).ToHashSet();
        var cashFlow = CashFlowProjection.Project(data, today, scope);
        var balances = Reporting.Accounts(data, today).Accounts.ToDictionary(a => a.Id);
        var accounts = data.Accounts.Where(a => scope.Contains(a.Id) && a.OpeningDate <= today)
            .OrderBy(a => a.Kind).ThenBy(a => a.Name).Select(a =>
            {
                var through = FirstWeek.ImportedThrough(data, a.Id, today);
                var lastImportedAt = data.ImportBatches.Where(b => b.AccountId == a.Id && !b.Undone)
                    .Select(b => (DateTime?)b.ImportedAt).Max();
                var lastImport = lastImportedAt is { } at ? (DateOnly?)DateOnly.FromDateTime(at) : null;
                var stale = cashFlow.Accounts.FirstOrDefault(f => f.Account.Id == a.Id)?.StaleImports ??
                    (through == null || today.DayNumber - through.Value.DayNumber > CashFlowProjection.StaleAfterDays ||
                     (!(BankImportCoverage.ConfirmedThrough(data, a.Id) >= through) &&
                      (lastImport == null || today.DayNumber - lastImport.Value.DayNumber > CashFlowProjection.StaleAfterDays)));
                return new HomeAccount(a, balances[a.Id], through, stale) { LastImportedAt = lastImportedAt };
            }).ToArray();
        var recent = data.Transactions.Where(t => t.Date <= today && (scope.Contains(t.AccountId) ||
                t.Kind == TransactionKind.Transfer && t.CounterAccountId is { } counter && scope.Contains(counter)))
            .OrderByDescending(t => t.Date).ThenBy(t => t.Id).Take(5)
            .Select(t => new HomeTransaction(t, scope.Contains(t.AccountId) ? t.AccountId : t.CounterAccountId!.Value,
                scope.Contains(t.AccountId) ? t.Amount : -t.Amount)).ToArray();
        return new(today, Finance.Report(data, year, month, today, scope, includeShared: ownerId == null), accounts, cashFlow, recent,
            FirstWeek.UncategorizedCount(data, today, scope));
    }
}
