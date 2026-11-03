namespace Huishoudboek.Core;

// Transient statement observations, including unselected and invalid preview rows.
// They are not persisted and never replace the account's opening balance.
public sealed record StatementBalanceObservation(int RowNumber, DateOnly? Date, decimal? Amount, decimal? Balance);

public enum StatementBalanceStatus { Available, Missing, Ambiguous, InvalidRows, NoActualRows }

public sealed record ImportBalanceCheck(DateOnly AsOf, decimal CalculatedBalance, decimal? StatementBalance,
    StatementBalanceStatus Status, int InvalidRowCount, int FutureRowCount)
{
    // Positive means the application's balance is higher than the bank's balance.
    public decimal? Difference => StatementBalance is { } balance ? CalculatedBalance - balance : null;
}

public static class ImportBalance
{
    public static ImportBalanceCheck Check(HouseholdData data, Guid accountId,
        IReadOnlyList<StatementBalanceObservation> observations, DateOnly today)
    {
        var account = data.Accounts.Single(a => a.Id == accountId);
        var actual = observations.Where(r => r.Date is { } date && date <= today).ToList();
        var asOf = actual.Count == 0 ? today : actual.Max(r => r.Date!.Value);
        var calculated = Finance.Balance(data, account, asOf);
        var invalidCount = observations.Count(r => r.Amount == null);
        var futureCount = observations.Count(r => r.Date > today);
        ImportBalanceCheck Result(StatementBalanceStatus status, decimal? balance = null) =>
            new(asOf, calculated, balance, status, invalidCount, futureCount);

        if (observations.Any(r => r.Date == null)) return Result(StatementBalanceStatus.InvalidRows);
        if (actual.Count == 0) return Result(StatementBalanceStatus.NoActualRows);
        var latest = actual.Where(r => r.Date == asOf).OrderBy(r => r.RowNumber).ToList();
        if (latest.Any(r => r.Amount == null)) return Result(StatementBalanceStatus.InvalidRows);
        if (latest.Any(r => r.Balance == null)) return Result(StatementBalanceStatus.Missing);
        if (latest.Count == 1) return Result(StatementBalanceStatus.Available, latest[0].Balance);

        // Dates alone cannot order multiple transactions on one day. Verify the
        // running balances in both source-row directions instead of guessing.
        var forward = true;
        var reverse = true;
        for (var i = 1; i < latest.Count; i++)
        {
            var previous = latest[i - 1]; var current = latest[i];
            forward &= current.Balance == previous.Balance + current.Amount;
            reverse &= previous.Balance == current.Balance + previous.Amount;
        }
        if (forward && reverse && latest[0].Balance != latest[^1].Balance)
            return Result(StatementBalanceStatus.Ambiguous);
        if (forward) return Result(StatementBalanceStatus.Available, latest[^1].Balance);
        if (reverse) return Result(StatementBalanceStatus.Available, latest[0].Balance);
        return Result(StatementBalanceStatus.Ambiguous);
    }
}
