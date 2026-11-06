using System.Security.Cryptography;
using System.Text.Json;

namespace Huishoudboek.Core;

/// <summary>Explicit checks, not balance adjustments. Snapshots make later cash changes reopen the check.</summary>
public sealed record AccountReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public DateOnly? OpeningDate { get; set; }
    public decimal? OpeningBalance { get; set; }
    public DateOnly? CheckedThrough { get; set; }
    public decimal? BankBalance { get; set; }
    public string CashFingerprint { get; set; } = "";
}

public sealed record SetupStep(string Key, string Link, bool Complete);
public sealed record UpcomingBill(RecurringPayment Payment, DateOnly DueDate);

public static class FirstWeek
{
    public static bool OpeningConfirmed(HouseholdData data, Account account) =>
        data.AccountReviews.Any(r => r.AccountId == account.Id && r.OpeningDate == account.OpeningDate && r.OpeningBalance == account.OpeningBalance);

    public static bool BalanceChecked(HouseholdData data, Account account, DateOnly today) =>
        data.AccountReviews.FirstOrDefault(r => r.AccountId == account.Id) is { CheckedThrough: { } through, BankBalance: { } balance } review &&
        through <= today && through >= account.OpeningDate &&
        !data.Transactions.Any(t => (t.AccountId == account.Id || t.CounterAccountId == account.Id) && t.Date > through && t.Date <= today) &&
        Finance.Balance(data, account, through) == balance && review.CashFingerprint == Fingerprint(data, account, through);

    public static void ConfirmOpening(HouseholdData data, Guid accountId)
    {
        var account = data.Accounts.Single(a => a.Id == accountId);
        var review = Review(data, accountId);
        review.OpeningDate = account.OpeningDate;
        review.OpeningBalance = account.OpeningBalance;
    }

    public static void CheckBalance(HouseholdData data, Guid accountId, DateOnly through, decimal bankBalance, DateOnly today)
    {
        var account = data.Accounts.Single(a => a.Id == accountId);
        if (through > today || through < account.OpeningDate)
            throw new InvalidOperationException("Kies een controledatum tussen de openingsdatum en vandaag.");
        if (decimal.Round(bankBalance, 2) != bankBalance || Finance.Balance(data, account, through) != bankBalance)
            throw new InvalidOperationException("Het banksaldo wijkt af. Controleer het beginsaldo en ontbrekende of dubbele boekingen; er wordt geen saldo aangepast.");
        var review = Review(data, accountId);
        review.CheckedThrough = through;
        review.BankBalance = bankBalance;
        review.CashFingerprint = Fingerprint(data, account, through);
    }

    public static IReadOnlyList<SetupStep> Checklist(HouseholdData data, DateOnly today)
    {
        var accounts = data.Accounts;
        var hasAccounts = accounts.Count > 0;
        var hasEntries = data.Transactions.Any(t => t.Date <= today);
        return [
            new("SetupAccounts", "rekeningen", hasAccounts),
            new("SetupOpening", "rekeningen#balance-checks", hasAccounts && accounts.All(a => OpeningConfirmed(data, a))),
            new("SetupImport", "importeren", hasAccounts && accounts.All(a => BankImportCoverage.ConfirmedThrough(data, a.Id) != null ||
                data.ImportBatches.Any(b => b.AccountId == a.Id && !b.Undone && b.RowCount > 0))),
            new("SetupBalances", "rekeningen#balance-checks", hasAccounts && accounts.All(a => BalanceChecked(data, a, today))),
            new("SetupCategorize", "categoriseren", hasEntries && UncategorizedCount(data, today) == 0),
            new("SetupPlans", "budgetten", (data.Categories.Any(c => c.MonthlyBudget > 0) || data.BudgetOverrides.Any(b => b.Amount > 0)) && data.RecurringPayments.Count > 0)
        ];
    }

    public static int UncategorizedCount(HouseholdData data, DateOnly today, IReadOnlySet<Guid>? scope = null) =>
        TransactionAccounting.Lines(data, data.Transactions.Where(t => t.Date <= today && (scope == null || scope.Contains(t.AccountId))))
            .Where(l => l.CategoryId == null).Select(l => l.Transaction.Id).Distinct().Count();

    // Statement coverage, not upload time: uploading an old file is not a fresh weekly import.
    public static DateOnly? ImportedThrough(HouseholdData data, Guid accountId, DateOnly today)
    {
        var batches = data.ImportBatches.Where(b => b.AccountId == accountId && !b.Undone).Select(b => b.Id).ToHashSet();
        var observed = data.Transactions.Where(t => t.AccountId == accountId && t.Date <= today && t.ImportBatchId is { } id && batches.Contains(id))
            .Select(t => (DateOnly?)t.Date).Max();
        var confirmed = BankImportCoverage.ConfirmedThrough(data, accountId);
        if (confirmed > today) confirmed = today;
        return confirmed > observed || observed == null ? confirmed ?? observed : observed;
    }

    public static IReadOnlyList<UpcomingBill> Upcoming(HouseholdData data, DateOnly today, IReadOnlySet<Guid>? scope = null) =>
        data.RecurringPayments.Where(r => r.Amount < 0 && (scope == null || scope.Contains(r.AccountId)))
            .SelectMany(r => RecurringSchedule.Occurrences(r, today, today.AddDays(7))
                .Where(d => !RecurringSchedule.IsLinked(data, r, d)).Select(d => new UpcomingBill(r, d)))
            .OrderBy(b => b.DueDate).ThenBy(b => b.Payment.Name).ToList();

    private static AccountReview Review(HouseholdData data, Guid accountId)
    {
        var review = data.AccountReviews.FirstOrDefault(r => r.AccountId == accountId);
        if (review != null) return review;
        review = new() { AccountId = accountId }; data.AccountReviews.Add(review); return review;
    }

    private static string Fingerprint(HouseholdData data, Account account, DateOnly through) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            account.OpeningDate, account.OpeningBalance,
            Entries = data.Transactions.Where(t => (t.AccountId == account.Id || t.CounterAccountId == account.Id) && t.Date <= through)
                .OrderBy(t => t.Id).Select(t => new { t.Id, t.AccountId, t.CounterAccountId, t.Date, t.Amount })
        })));
}
