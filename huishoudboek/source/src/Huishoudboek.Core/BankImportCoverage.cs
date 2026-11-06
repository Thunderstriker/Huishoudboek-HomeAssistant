using System.Security.Cryptography;
using System.Text.Json;

namespace Huishoudboek.Core;

public sealed record StatementPeriod(DateOnly From, DateOnly Through);

/// <summary>Only explicitly confirmed, complete statements establish coverage. Quiet days are valid; gaps are not.</summary>
public static class BankImportCoverage
{
    public static bool Covers(HouseholdData data, Guid accountId, DateOnly from, DateOnly through)
        => through >= from && MissingPeriods(data, accountId, from, through).Count == 0;

    public static IReadOnlyList<StatementPeriod> MissingPeriods(HouseholdData data, Guid accountId, DateOnly from, DateOnly through)
    {
        if (through < from) return [];
        var missing = new List<StatementPeriod>();
        var next = from;
        foreach (var period in Periods(data, accountId).OrderBy(p => p.From))
        {
            if (period.Through < next) continue;
            if (period.From > through) break;
            if (period.From > next) missing.Add(new(next, period.From.AddDays(-1)));
            if (period.Through >= through) return missing;
            next = period.Through.AddDays(1);
        }
        missing.Add(new(next, through));
        return missing;
    }

    private static IEnumerable<StatementPeriod> Periods(HouseholdData data, Guid accountId) => data.ImportBatches
        .Where(b => b.AccountId == accountId && !b.Undone && b.CoverageFrom != null && b.CoverageThrough != null)
        .Select(b => new StatementPeriod(b.CoverageFrom!.Value, b.CoverageThrough!.Value))
        .Concat(data.AccountStatementPeriods.Where(p => p.AccountId == accountId && IsCurrent(data, p))
            .Select(p => new StatementPeriod(p.From, p.Through)));

    public static DateOnly? ConfirmedThrough(HouseholdData data, Guid accountId) => Periods(data, accountId)
        .Select(p => (DateOnly?)p.Through).Max();

    public static void ConfirmAccountPeriod(HouseholdData data, Guid accountId, DateOnly from, DateOnly through, DateOnly today, Guid? periodId = null)
    {
        var account = data.Accounts.Single(a => a.Id == accountId);
        if (account.Kind == AccountKind.Investment || from < account.OpeningDate || from > through || through > today)
            throw new InvalidOperationException("Kies een periode tussen de openingsdatum en vandaag voor een betaal- of spaarrekening.");
        var period = periodId is { } id
            ? data.AccountStatementPeriods.Single(p => p.Id == id && p.AccountId == accountId)
            : new AccountStatementPeriod { AccountId = accountId };
        period.From = from; period.Through = through;
        period.BankDataFingerprint = Fingerprint(data, account, from, through);
        if (periodId == null) data.AccountStatementPeriods.Add(period);
    }

    public static bool IsCurrent(HouseholdData data, AccountStatementPeriod period) =>
        data.Accounts.FirstOrDefault(a => a.Id == period.AccountId) is { } account &&
        account.Kind != AccountKind.Investment && period.From >= account.OpeningDate && period.Through >= period.From &&
        period.BankDataFingerprint == Fingerprint(data, account, period.From, period.Through);

    // Bank-data changes reopen the confirmation, even when made outside the web editor.
    // Categorization, descriptions written by the user and recurring links do not.
    private static string Fingerprint(HouseholdData data, Account account, DateOnly from, DateOnly through) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            account.OpeningDate, account.OpeningBalance, account.BankReference,
            Entries = data.Transactions.Where(t => (t.AccountId == account.Id || t.CounterAccountId == account.Id) && t.Date >= from && t.Date <= through)
                .OrderBy(t => t.Id).Select(t => new { t.Id, t.AccountId, t.CounterAccountId, t.Date, t.Amount, t.Description, t.StatementBalance })
        })));

    public static void Invalidate(HouseholdData data, Guid? batchId)
    {
        if (data.ImportBatches.FirstOrDefault(b => b.Id == batchId) is not { } batch) return;
        batch.CoverageFrom = null;
        batch.CoverageThrough = null;
    }
}
