using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class AccountStatementPeriodTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static readonly DateOnly From = new(2024, 11, 1);

    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Owner" };
        return new HouseholdData { Owners = [owner],
            Accounts = [new() { Name = "Quiet savings", Kind = AccountKind.Savings, OwnerIds = [owner.Id], OpeningDate = From, OpeningBalance = 5000 }],
            Categories = [new() { Name = "Interest" }] };
    }

    private static Transaction Entry(HouseholdData data, DateOnly? date = null)
    {
        var entry = new Transaction { AccountId = data.Accounts[0].Id, Date = date ?? Today.AddDays(-10), Amount = 10, Description = "Interest" };
        data.Transactions.Add(entry);
        return entry;
    }

    [Fact]
    public void TwoYearsOfQuietSavingsCanBeConfirmedWithoutAnyImportOrBalanceChange()
    {
        var data = Data(); var account = data.Accounts[0];
        var before = JsonSerializer.Serialize(data.Accounts);
        BankImportCoverage.ConfirmAccountPeriod(data, account.Id, From, Today, Today);
        Validation.Validate(data);
        Assert.Single(data.AccountStatementPeriods);
        Assert.Empty(data.ImportBatches); Assert.Empty(data.Transactions); Assert.Empty(data.AccountReviews);
        Assert.Equal(before, JsonSerializer.Serialize(data.Accounts));
        Assert.Equal(5000, Finance.Balance(data, account, Today));
        Assert.True(BankImportCoverage.Covers(data, account.Id, From, Today));
        Assert.Equal(Today, BankImportCoverage.ConfirmedThrough(data, account.Id));
        Assert.Equal(Today, FirstWeek.ImportedThrough(data, account.Id, Today));
        Assert.False(FirstWeek.BalanceChecked(data, account, Today));
        Assert.True(FirstWeek.Checklist(data, Today).Single(s => s.Key == "SetupImport").Complete);
        Assert.Equal(12, FinancialInsights.Create(data, Today).CoveredMonths);
    }

    [Theory]
    [InlineData(-5)] // Overlap.
    [InlineData(0)] // Adjacent.
    [InlineData(1)] // One-day gap.
    public void ManualAndImportedPeriodsCombineWithoutHidingGaps(int offset)
    {
        var data = Data(); var id = data.Accounts[0].Id;
        var boundary = new DateOnly(2026, 10, 15); var end = new DateOnly(2026, 10, 31);
        var batch = new ImportBatch { AccountId = id, CoverageFrom = new(2026, 10, 1), CoverageThrough = boundary };
        data.ImportBatches.Add(batch);
        BankImportCoverage.ConfirmAccountPeriod(data, id, boundary.AddDays(1 + offset), end, Today);
        var missing = BankImportCoverage.MissingPeriods(data, id, new(2026, 10, 1), end);
        Assert.Equal(offset <= 0, BankImportCoverage.Covers(data, id, new(2026, 10, 1), end));
        if (offset > 0) Assert.Equal(new StatementPeriod(boundary.AddDays(1), boundary.AddDays(1)), Assert.Single(missing));
        else Assert.Empty(missing);
        batch.Undone = true;
        Assert.False(BankImportCoverage.Covers(data, id, new(2026, 10, 1), end));
    }

    [Theory]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("amount")]
    [InlineData("date")]
    [InlineData("description")]
    [InlineData("opening")]
    [InlineData("import")]
    public void BankDataChangesReopenConfirmationUntilExplicitlyReconfirmed(string change)
    {
        var data = Data(); var account = data.Accounts[0]; var entry = Entry(data);
        BankImportCoverage.ConfirmAccountPeriod(data, account.Id, From, Today, Today);
        var period = Assert.Single(data.AccountStatementPeriods);
        switch (change)
        {
            case "add": Entry(data); break;
            case "delete": data.Transactions.Clear(); break;
            case "amount": entry.Amount++; break;
            case "date": entry.Date = entry.Date.AddDays(-1); break;
            case "description": entry.Description = "Corrected bank text"; break;
            case "opening": account.OpeningBalance++; break;
            case "import":
                var batch = new ImportBatch { AccountId = account.Id, RowCount = 1 };
                data.ImportBatches.Add(batch); Entry(data).ImportBatchId = batch.Id; break;
        }
        Validation.Validate(data); // Stale confirmations remain editable, rather than invalidating the database.
        Assert.False(BankImportCoverage.IsCurrent(data, period));
        Assert.False(BankImportCoverage.Covers(data, account.Id, From, Today));
        Assert.Null(BankImportCoverage.ConfirmedThrough(data, account.Id));
        BankImportCoverage.ConfirmAccountPeriod(data, account.Id, From, Today, Today, period.Id);
        Assert.Single(data.AccountStatementPeriods);
        Assert.True(BankImportCoverage.IsCurrent(data, period));
        Assert.True(BankImportCoverage.Covers(data, account.Id, From, Today));
    }

    [Fact]
    public void CategorizationUserNotesAndRecurringLinksDoNotReopenConfirmation()
    {
        var data = Data(); var account = data.Accounts[0]; var entry = Entry(data);
        BankImportCoverage.ConfirmAccountPeriod(data, account.Id, From, Today, Today);
        var payment = new RecurringPayment { Name = "Interest", AccountId = account.Id, Amount = 10,
            Frequency = PaymentFrequency.Once, StartDate = entry.Date, DayOfMonth = entry.Date.Day };
        data.RecurringPayments.Add(payment);
        entry.CategoryId = data.Categories[0].Id; entry.UserDescription = "My note";
        entry.RecurringPaymentId = payment.Id; entry.RecurringOccurrenceDate = entry.Date;
        Validation.Validate(data);
        Assert.True(BankImportCoverage.IsCurrent(data, Assert.Single(data.AccountStatementPeriods)));
    }

    [Fact]
    public void ChangesOutsideConfirmedDatesDoNotReopenThePeriod()
    {
        var data = Data();
        BankImportCoverage.ConfirmAccountPeriod(data, data.Accounts[0].Id, From, Today.AddDays(-5), Today);
        Entry(data, Today);
        Assert.True(BankImportCoverage.IsCurrent(data, Assert.Single(data.AccountStatementPeriods)));
    }

    [Fact]
    public void LegacyTransfersAlsoReopenTheReceivingAccountsConfirmation()
    {
        var data = Data(); var savings = data.Accounts[0];
        var current = savings with { Id = Guid.NewGuid(), Name = "Current", Kind = AccountKind.Current };
        data.Accounts.Add(current);
        var entry = Entry(data); entry.AccountId = current.Id; entry.CounterAccountId = savings.Id;
        entry.Kind = TransactionKind.Transfer; entry.Amount = -100;
        BankImportCoverage.ConfirmAccountPeriod(data, savings.Id, From, Today, Today);
        entry.Amount = -200;
        Validation.Validate(data);
        Assert.False(BankImportCoverage.IsCurrent(data, Assert.Single(data.AccountStatementPeriods)));
    }

    [Fact]
    public void InvalidRangesAndInvestmentAccountsCannotBeConfirmed()
    {
        var data = Data(); var account = data.Accounts[0];
        void Invalid(DateOnly from, DateOnly through) => Assert.Throws<InvalidOperationException>(() =>
            BankImportCoverage.ConfirmAccountPeriod(data, account.Id, from, through, Today));
        Invalid(From.AddDays(-1), Today); Invalid(Today, Today.AddDays(-1)); Invalid(From, Today.AddDays(1));
        account.Kind = AccountKind.Investment; Invalid(From, Today);
        Assert.Empty(data.AccountStatementPeriods);
    }

    [Fact]
    public void MissingRangesHandleNestedIntervalsAndDateBoundaries()
    {
        var data = Data(); var id = data.Accounts[0].Id;
        data.ImportBatches.AddRange([
            new() { AccountId = id, CoverageFrom = new(2026, 10, 5), CoverageThrough = new(2026, 10, 20) },
            new() { AccountId = id, CoverageFrom = new(2026, 10, 10), CoverageThrough = new(2026, 10, 15) },
            new() { AccountId = id, CoverageFrom = new(2026, 10, 22), CoverageThrough = new(2026, 10, 25) }
        ]);
        Assert.Equal(new[] { new StatementPeriod(new(2026, 10, 1), new(2026, 10, 4)),
            new StatementPeriod(new(2026, 10, 21), new(2026, 10, 21)), new StatementPeriod(new(2026, 10, 26), new(2026, 10, 31)) },
            BankImportCoverage.MissingPeriods(data, id, new(2026, 10, 1), new(2026, 10, 31)));
        data.ImportBatches.Add(new() { AccountId = id, CoverageFrom = DateOnly.MinValue, CoverageThrough = DateOnly.MaxValue });
        Assert.True(BankImportCoverage.Covers(data, id, DateOnly.MinValue, DateOnly.MaxValue));
        Assert.False(BankImportCoverage.Covers(data, id, Today, From));
    }

    [Fact]
    public void ConfirmingSavingsClosesTheActualBlockerForHistoricalInsights()
    {
        var data = FinancialInsightsTests.Data(); var savings = data.Accounts[1];
        data.ImportBatches.RemoveAll(b => b.AccountId == savings.Id);
        var before = FinancialInsights.Create(data, Today);
        Assert.Equal(0, before.CoveredMonths); Assert.Null(before.Freedom.EssentialMonths);
        var october = before.Months.Single(m => m.Month == new DateOnly(2026, 10, 1));
        var gap = Assert.Single(october.CoverageIssues);
        Assert.Equal(new InsightCoverageIssue(savings.Id, new(2026, 10, 1), new(2026, 10, 31)), gap);
        var entries = JsonSerializer.Serialize(data.Transactions); var imports = JsonSerializer.Serialize(data.ImportBatches);
        BankImportCoverage.ConfirmAccountPeriod(data, savings.Id, savings.OpeningDate, Today, Today);
        var after = FinancialInsights.Create(data, Today);
        Assert.Equal(6, after.CoveredMonths); Assert.NotNull(after.Freedom.EssentialMonths);
        Assert.Empty(after.Months.Single(m => m.Month == october.Month).CoverageIssues);
        Assert.Equal(entries, JsonSerializer.Serialize(data.Transactions)); Assert.Equal(imports, JsonSerializer.Serialize(data.ImportBatches));
    }

    [Fact]
    public void CoverageDiagnosticsRespectOwnerScopeAndExplainPartialOpeningMonths()
    {
        var data = FinancialInsightsTests.Data(); var other = new Owner { Name = "Other" }; data.Owners.Add(other);
        var savings = data.Accounts[1]; savings.OwnerIds = [other.Id]; savings.OpeningDate = new(2026, 10, 10);
        data.Transactions.RemoveAll(t => t.AccountId == savings.Id);
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(new InsightCoverageIssue(savings.Id, savings.OpeningDate, new(2026, 10, 31), OpeningMonth: true),
            Assert.Single(report.Months.Single(m => m.Month.Month == 10).CoverageIssues));
        Assert.True(FinancialInsights.Create(data, Today, data.Owners[0].Id).Months.Single(m => m.Month.Month == 10).Covered);
    }

    [Fact]
    public void ConfirmationsRoundTripThroughPersistenceAndBackupIncludingStaleStatus()
    {
        using var files = new TestFiles(); var data = Data(); var id = data.Accounts[0].Id;
        BankImportCoverage.ConfirmAccountPeriod(data, id, From, Today, Today);
        var store = new SqliteHouseholdStore(files.Database); store.Save(data); store.Backup(files.Backup);
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(store.Load()));
        store.Save(HouseholdData.CreateEmpty()); store.Restore(files.Backup);
        var restored = store.Load();
        Assert.True(BankImportCoverage.Covers(restored, id, From, Today));
        Entry(restored); store.Save(restored);
        Assert.False(BankImportCoverage.IsCurrent(store.Load(), Assert.Single(store.Load().AccountStatementPeriods)));
    }

    [Fact]
    public void AccountConfirmationsUseTheAtomicCoordinatorAndSurviveRestart()
    {
        using var files = new TestFiles(); var data = Data(); var account = data.Accounts[0];
        var directory = Path.Combine(files.Directory, "administration");
        using (var coordinator = new AdministrationCoordinator(directory, initialize: true))
        {
            var original = coordinator.Replace(coordinator.Read().Revision, data);
            var saved = coordinator.Update(original.Revision, d => BankImportCoverage.ConfirmAccountPeriod(d, account.Id, From, Today, Today));
            Assert.Empty(original.Data.AccountStatementPeriods);
            Assert.True(BankImportCoverage.Covers(saved.Data, account.Id, From, Today));
            saved.Data.AccountStatementPeriods.Clear(); // Returned snapshots cannot mutate persisted state.
            Assert.Single(coordinator.Read().Data.AccountStatementPeriods);
            var replacement = coordinator.Read();
            coordinator.Replace(replacement.Revision, replacement.Data);
            Assert.Single(coordinator.Read().Data.AccountStatementPeriods);
        }
        using var restarted = new AdministrationCoordinator(directory);
        Assert.True(BankImportCoverage.Covers(restarted.Read().Data, account.Id, From, Today));
    }

    [Fact]
    public void Schema10CanBeReadWithoutMutationAndUpgradedWithASafetyBackup()
    {
        using var files = new TestFiles(); var data = FinancialInsightsTests.Data();
        new SqliteHouseholdStore(files.Database).Save(data);
        using (var connection = new SqliteConnection($"Data Source={files.Database};Pooling=False"))
        {
            connection.Open(); using var sql = connection.CreateCommand();
            sql.CommandText = PreviousSchema.RemoveSchema11Tables + "PRAGMA user_version=10;"; sql.ExecuteNonQuery();
        }
        var bytes = File.ReadAllBytes(files.Database); var old = SqliteHouseholdStore.ReadBackup(files.Database);
        Assert.Empty(old.AccountStatementPeriods); Assert.Equal(bytes, File.ReadAllBytes(files.Database));
        Assert.Throws<InvalidDataException>(() => new SqliteHouseholdStore(files.Database, allowCreate: false, allowUpgrade: false));
        var store = new SqliteHouseholdStore(files.Database, allowCreate: false);
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(store.Load()));
        var safety = Assert.Single(Directory.GetFiles(files.Directory, "*.voor-versie-11-*.db"));
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(SqliteHouseholdStore.ReadBackup(safety)));
        BankImportCoverage.ConfirmAccountPeriod(data, data.Accounts[1].Id, data.Accounts[1].OpeningDate, Today, Today);
        store.Save(data); Assert.Single(store.Load().AccountStatementPeriods);
    }

    private sealed class TestFiles : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "opencode", "account-period-tests", Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(Directory, "household.db");
        public string Backup => Path.Combine(Directory, "backup.db");
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
    }
}
