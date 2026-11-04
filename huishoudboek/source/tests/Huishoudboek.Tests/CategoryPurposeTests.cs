using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Huishoudboek.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class CategoryPurposeTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);
    private static readonly DateOnly MonthEnd = new(2026, 11, 30);

    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Owner" };
        return new() {
            Owners = [owner],
            Accounts = Enum.GetValues<AccountKind>().Select(kind => new Account {
                Name = kind.ToString(), Kind = kind, OwnerIds = [owner.Id],
                OpeningDate = new(2025, 1, 1), OpeningBalance = 10000, MinimumCashBuffer = 100
            }).ToList(),
            Categories = [new() { Name = "Expenses", MonthlyBudget = 3000 },
                new() { Name = "My savings", Purpose = CategoryPurpose.Savings, MonthlyBudget = 500 },
                new() { Name = "My accounts", Purpose = CategoryPurpose.OwnAccountMovement, MonthlyBudget = 999 }]
        };
    }

    private static Transaction Entry(HouseholdData data, decimal amount, int? category = 0, int account = 0, DateOnly? date = null)
    {
        var entry = new Transaction { AccountId = data.Accounts[account].Id, Amount = amount, Date = date ?? Today,
            Description = "Original bank text", CategoryId = category is { } index ? data.Categories[index].Id : null };
        data.Transactions.Add(entry);
        return entry;
    }

    [Fact]
    public void OneSidedSavingsAreSeparateFromSpendingAndNeverAlterAnotherBalance()
    {
        var data = Data(); Entry(data, 3000); Entry(data, -2600);
        var savings = Entry(data, -600, category: null);
        var bankEntries = data.Transactions.Select(t => t with { }).ToArray();
        var balances = data.Accounts.Select(a => Finance.Balance(data, a, MonthEnd)).ToArray();
        Categorization.Assign(data, new HashSet<Guid> { savings.Id }, data.Categories[1].Id);
        Validation.Validate(data);
        var report = Finance.Report(data, 2026, 11, MonthEnd);
        Assert.Equal((3000m, 2600m, 400m, 600m, -200m),
            (report.Income, report.Outflow, report.BeforeSavings, report.Savings, report.Available));
        Assert.False(report.IsOverspending); Assert.True(report.SavingsExceedSurplus); Assert.True(report.NeedsAttention);
        Assert.Equal(3000m, report.Budget); Assert.Equal(0m, report.Overspent);
        Assert.Equal(2600m, Assert.Single(report.Categories).Spent);
        Assert.Equal(new SavingsResult(data.Categories[1].Id, "My savings", 500, 600), Assert.Single(report.SavingsCategories));
        Assert.Equal(balances, data.Accounts.Select(a => Finance.Balance(data, a, MonthEnd)));
        Assert.Equal(bankEntries.Select(t => t with { CategoryId = null }), data.Transactions.Select(t => t with { CategoryId = null }));
        Assert.All(data.Transactions, t => Assert.Null(t.CounterAccountId)); Assert.Empty(data.TransferMatches);
        Assert.Equal(600m, Reporting.Month(data, 2026, 11, MonthEnd).Savings);
        Assert.Equal(600m, DetailedReporting.Year(data, 2026, MonthEnd).Savings);
        Assert.DoesNotContain(Reporting.Month(data, 2026, 11, MonthEnd).Categories, c => c.Id == data.Categories[1].Id || c.Id == data.Categories[2].Id);
        Assert.DoesNotContain(DetailedReporting.Year(data, 2026, MonthEnd).Categories, c => c.Id == data.Categories[1].Id || c.Id == data.Categories[2].Id);
    }

    [Theory]
    [InlineData(AccountKind.Current, -500, 500)]
    [InlineData(AccountKind.Current, 100, -100)]
    [InlineData(AccountKind.Savings, 500, 0)]
    [InlineData(AccountKind.Savings, -100, 0)]
    [InlineData(AccountKind.Investment, 500, 0)]
    [InlineData(AccountKind.Investment, -100, 0)]
    public void SavingsPurposeUsesOnlyCurrentAccountEntries(AccountKind kind, int amount, int expected)
    {
        var data = Data(); data.Accounts[0].Kind = kind;
        var entry = Entry(data, amount, 1);
        var report = Finance.Report(data, 2026, 11, MonthEnd);
        Assert.Equal((0m, 0m, (decimal)expected), (report.Income, report.Outflow, report.Savings));
        Assert.Equal((decimal)expected, Finance.SavingsEffect(data, entry));
        Assert.Equal(10000m + amount, Finance.Balance(data, data.Accounts[0], MonthEnd));
        Assert.Equal(0m, report.Forecast.VariableEstimate);
    }

    [Fact]
    public void ImportingBothSidesAndWithdrawalsCountsNetContributionsOnce()
    {
        var data = Data(); Entry(data, -600, 1); Entry(data, 600, 1, 1);
        Entry(data, -100, 1, 1); Entry(data, 100, 1);
        Validation.Validate(data);
        var report = Finance.Report(data, 2026, 11, MonthEnd);
        Assert.Equal((0m, 0m, 500m), (report.Income, report.Outflow, report.Savings));
        Assert.Equal(500m, data.Transactions.Sum(t => Finance.SavingsEffect(data, t)));
        Assert.Equal(9500m, Finance.Balance(data, data.Accounts[0], MonthEnd));
        Assert.Equal(10500m, Finance.Balance(data, data.Accounts[1], MonthEnd));
        Assert.Empty(data.TransferMatches);
    }

    [Fact]
    public void SavingsAcrossYearBoundariesUseTheCurrentAccountsOwnDate()
    {
        var data = Data(); Entry(data, -600, 1, date: new(2026, 12, 31));
        Entry(data, 600, 1, 1, new(2027, 1, 2));
        Entry(data, -100, 1, 1, new(2027, 1, 31)); Entry(data, 100, 1, date: new(2027, 2, 1));
        var end = new DateOnly(2027, 2, 28);
        Assert.Equal(600m, Finance.Report(data, 2026, 12, end).Savings);
        Assert.Equal(0m, Finance.Report(data, 2027, 1, end).Savings);
        Assert.Equal(-100m, Finance.Report(data, 2027, 2, end).Savings);
        Assert.Equal(600m, DetailedReporting.Year(data, 2026, end).Savings);
        Assert.Equal(-100m, DetailedReporting.Year(data, 2027, end).Savings);
        Assert.Equal(0m, DetailedReporting.Year(data, 2027, end).Income);
    }

    [Fact]
    public void OwnerFiltersFollowTheCurrentAccountAndSharedAccountsCountOnce()
    {
        var data = Data(); var other = new Owner { Name = "Other owner" }; data.Owners.Add(other);
        data.Accounts[1].OwnerIds = [other.Id]; Entry(data, -500, 1); Entry(data, 500, 1, 1);
        Assert.Equal(500m, DetailedReporting.Dashboard(data, 2026, 11, MonthEnd, data.Owners[0].Id).Report.Savings);
        Assert.Equal(0m, DetailedReporting.Dashboard(data, 2026, 11, MonthEnd, other.Id).Report.Savings);
        Assert.Equal(0m, Finance.Report(data, 2026, 11, MonthEnd, new HashSet<Guid>()).Savings);
        data.Accounts[0].OwnerIds.Add(other.Id);
        Assert.Equal(500m, DetailedReporting.Dashboard(data, 2026, 11, MonthEnd, other.Id).Report.Savings);
        Assert.Equal(500m, DetailedReporting.Dashboard(data, 2026, 11, MonthEnd).Report.Savings);
    }

    [Fact]
    public void RealIncomeAndSpendingOnSavingsAccountsStillCountNormally()
    {
        var data = Data(); Entry(data, 25, account: 1); Entry(data, -40, account: 1);
        Entry(data, 15, account: 2); Entry(data, -10, account: 2);
        var report = Finance.Report(data, 2026, 11, MonthEnd);
        Assert.Equal((40m, 50m, 0m), (report.Income, report.Outflow, report.Savings));
    }

    [Fact]
    public void OwnAccountCategoriesIgnoreEvenOneSidedMovementsButRetainTheirCashEffects()
    {
        var data = Data(); data.Accounts[1].Kind = AccountKind.Current;
        Entry(data, -500, 2); Entry(data, 500, 2, 1); Entry(data, -200, 2);
        var report = Finance.Report(data, 2026, 11, MonthEnd);
        Assert.Equal((0m, 0m, 0m, 3000m), (report.Income, report.Outflow, report.Savings, report.Budget));
        Assert.Equal(9300m, Finance.Balance(data, data.Accounts[0], MonthEnd));
        Assert.Equal(10500m, Finance.Balance(data, data.Accounts[1], MonthEnd));
        Assert.Equal((0m, 0m), Categorization.ReviewTotals(data, data.Transactions));
        Assert.All(CashFlow.Project(data, Today).Accounts, a => Assert.Equal(0m, a.DailyVariableEstimate));
    }

    [Fact]
    public void WithdrawingSavingsDoesNotHideSpendingAboveIncome()
    {
        var data = Data(); Entry(data, 3000); Entry(data, -3100); Entry(data, 200, 1);
        var report = Finance.Report(data, 2026, 11, MonthEnd);
        Assert.Equal((-100m, -200m, 100m), (report.BeforeSavings, report.Savings, report.Available));
        Assert.True(report.IsOverspending);
    }

    [Fact]
    public void CategoryNamesNeverImplicitlyChangeTheirPurpose()
    {
        var data = Data(); data.Categories[0].Name = "Sparen"; Entry(data, -500);
        Assert.Equal(500m, Finance.Report(data, 2026, 11, MonthEnd).Outflow);
        Assert.Equal(0m, Finance.Report(data, 2026, 11, MonthEnd).Savings);
        Categorization.RenameCategory(data, data.Categories[1].Id, "Anything I like"); Entry(data, -200, 1);
        Assert.Equal(200m, Finance.Report(data, 2026, 11, MonthEnd).Savings);
    }

    [Fact]
    public void SplitEntriesClassifyEachPartAndOnlyOrdinarySpendingFeedsEstimates()
    {
        var data = Data(); var entry = Entry(data, -200);
        var original = entry with { };
        TransactionAccounting.SaveAllocations(data, entry.Id, [
            new() { Amount = -70, CategoryId = data.Categories[0].Id },
            new() { Amount = -100, CategoryId = data.Categories[1].Id },
            new() { Amount = -30, CategoryId = data.Categories[2].Id }
        ]);
        var report = Finance.Report(data, 2026, 11, Today);
        Assert.Equal((70m, 100m, 140m), (report.Outflow, report.Savings, report.Forecast.VariableEstimate));
        Assert.Equal(0.78m, CashFlow.Project(data, Today).Accounts[0].DailyVariableEstimate);
        Assert.Equal(original, Assert.Single(data.Transactions));
        Assert.Equal(9800m, Finance.Balance(data, data.Accounts[0], Today));
    }

    [Fact]
    public void FutureSplitUsesFullCashPaymentWithoutDoubleEstimatingSavings()
    {
        var data = Data(); var entry = Entry(data, -200, date: Today.AddDays(1));
        TransactionAccounting.SaveAllocations(data, entry.Id, [
            new() { Amount = -70, CategoryId = data.Categories[0].Id },
            new() { Amount = -100, CategoryId = data.Categories[1].Id },
            new() { Amount = -30, CategoryId = data.Categories[2].Id }
        ]);
        var report = Finance.Report(data, 2026, 11, Today);
        Assert.Equal((0m, 0m, 70m, 100m, 0m), (report.Outflow, report.Savings, report.Forecast.Outflow, report.Forecast.Savings, report.Forecast.VariableEstimate));
        var cash = CashFlow.Project(data, Today).Accounts[0];
        Assert.Equal(200m, cash.Days[0].Payments); Assert.Equal(9800m, cash.Days[0].ClosingBalance);
    }

    [Fact]
    public void FutureSplitOccupiesOnlyItsOrdinaryPartOfTheSpendingEstimate()
    {
        var data = Data(); Entry(data, -100);
        var entry = Entry(data, -200, date: Today.AddDays(1));
        TransactionAccounting.SaveAllocations(data, entry.Id, [
            new() { Amount = -70, CategoryId = data.Categories[0].Id },
            new() { Amount = -100, CategoryId = data.Categories[1].Id },
            new() { Amount = -30, CategoryId = data.Categories[2].Id }
        ]);
        var report = Finance.Report(data, 2026, 11, Today);
        Assert.Equal((300m, 100m, 130m), (report.Forecast.Outflow, report.Forecast.Savings, report.Forecast.VariableEstimate));
    }

    [Fact]
    public void FutureRefundReducesSpendingWithoutIncreasingTheVariableEstimate()
    {
        var data = Data(); var expense = Entry(data, -100); var refund = Entry(data, 20, date: Today.AddDays(1));
        TransactionAccounting.SaveAllocations(data, refund.Id,
            [new() { Amount = 20, Treatment = AllocationTreatment.Refund, OriginalTransactionId = expense.Id }]);
        Entry(data, -300, 1, date: Today.AddDays(1)); Entry(data, -500, 2, date: Today.AddDays(1));
        var report = Finance.Report(data, 2026, 11, Today);
        Assert.Equal((0m, 280m, 300m, 200m), (report.Forecast.Income, report.Forecast.Outflow, report.Forecast.Savings, report.Forecast.VariableEstimate));
    }

    [Theory]
    [InlineData(CategoryPurpose.Savings, -500, 500)]
    [InlineData(CategoryPurpose.Savings, 200, -200)]
    [InlineData(CategoryPurpose.OwnAccountMovement, -500, 0)]
    [InlineData(CategoryPurpose.OwnAccountMovement, 200, 0)]
    public void PlannedMovementsReserveCashButAreNotSpendingOrTheNextPayday(CategoryPurpose purpose, int amount, int expectedSavings)
    {
        var data = Data(); var category = data.Categories.Single(c => c.Purpose == purpose);
        var plan = new RecurringPayment { Name = "Planned movement", AccountId = data.Accounts[0].Id, Amount = amount,
            CategoryId = category.Id, StartDate = Today.AddDays(2), Frequency = PaymentFrequency.Once };
        data.RecurringPayments.Add(plan);
        var report = Finance.Report(data, 2026, 11, Today);
        Assert.Equal((0m, 0m, (decimal)expectedSavings), (report.Forecast.Income, report.Forecast.Outflow, report.Forecast.Savings));
        var cash = CashFlow.Project(data, Today).Accounts[0];
        Assert.Null(cash.NextIncomeDate); Assert.Equal(amount, Assert.Single(cash.Events).Amount);
        Assert.Equal(10000m + amount, cash.Days[1].ClosingBalance);
        var actual = Entry(data, amount, category: null, date: plan.StartDate);
        RecurringSchedule.Link(data, actual.Id, plan.Id, plan.StartDate);
        Validation.Validate(data);
        Assert.Equal(category.Id, data.Transactions.Single().CategoryId);
        Assert.Equal((decimal)expectedSavings, Finance.Report(data, 2026, 11, plan.StartDate).Savings);
        Assert.Equal((decimal)expectedSavings, Finance.Report(data, 2026, 11, plan.StartDate).Forecast.Savings);
        Assert.Single(CashFlow.Project(data, Today).Accounts[0].Events);
    }

    [Fact]
    public void SavingsCannotAccidentallyBeMatchedAsAnOrdinaryBill()
    {
        var data = Data(); var entry = Entry(data, -500, 1);
        var bill = new RecurringPayment { AccountId = entry.AccountId, Name = "Ordinary bill", CategoryId = data.Categories[0].Id,
            Amount = -500, StartDate = Today, Frequency = PaymentFrequency.Once, MatchText = "bank text" };
        data.RecurringPayments.Add(bill); Finance.ApplySuggestions(data, entry);
        Assert.Null(entry.RecurringPaymentId); Assert.Empty(RecurringSchedule.Candidates(data, Today));
        Assert.Throws<InvalidOperationException>(() => RecurringSchedule.Link(data, entry.Id, bill.Id, Today));
        bill.CategoryId = data.Categories[1].Id; Finance.ApplySuggestions(data, entry);
        Assert.Equal(bill.Id, entry.RecurringPaymentId); Assert.Equal(500m, Finance.Report(data, 2026, 11, Today).Savings);
    }

    [Fact]
    public void SavingsTargetsUseMonthlyOverridesAndNeverBecomeOverspentBudgets()
    {
        var data = Data(); Entry(data, -600, 1); HouseholdEdits.SetBudget(data, data.Categories[1].Id, 2026, 11, 550);
        var savings = DetailedReporting.Budgets(data, 2026, 11, MonthEnd).Single(c => c.Purpose == CategoryPurpose.Savings);
        Assert.Equal((500m, 550m, 600m), (savings.DefaultBudget, savings.Budget, savings.Spent)); Assert.False(savings.IsOverBudget);
        Assert.Equal(550m, Assert.Single(Finance.Report(data, 2026, 11, MonthEnd).SavingsCategories).Target);
        Assert.Equal(500m, Assert.Single(Finance.Report(data, 2026, 12, new(2026, 12, 31)).SavingsCategories).Target);
        Assert.Equal(3000m, Finance.Report(data, 2026, 11, MonthEnd).Budget);
    }

    [Fact]
    public void RefundAndClaimCategoriesCannotBeChangedToSavings()
    {
        var data = Data(); var expense = Entry(data, -100); var refund = Entry(data, 10);
        TransactionAccounting.SaveAllocations(data, refund.Id, [new() { Amount = 10, Treatment = AllocationTreatment.Refund, OriginalTransactionId = expense.Id }]);
        data.Categories[0].Purpose = CategoryPurpose.Savings;
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
        var other = Data(); var receipt = Entry(other, 10);
        Assert.Throws<InvalidOperationException>(() => TransactionAccounting.SaveAllocations(other, receipt.Id,
            [new() { Amount = 10, Treatment = AllocationTreatment.Refund, CategoryId = other.Categories[1].Id }]));
        other.Categories[0].Purpose = (CategoryPurpose)99;
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(other));
    }

    [Fact]
    public void MortgagePaymentsCannotBeHiddenAsSavingsOrOwnAccountMovements()
    {
        var data = Data(); data.Mortgage = new Mortgage { ReferencePrincipal = 200000, ReferenceDate = new(2025, 1, 1),
            MonthlyPayment = 1000, OwnerIds = [data.Owners[0].Id] };
        var mortgage = Entry(data, -1000, 1); mortgage.Kind = TransactionKind.Mortgage; mortgage.PrincipalComponent = 500;
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
        mortgage.CategoryId = data.Categories[0].Id; Validation.Validate(data);
        data.RecurringPayments.Add(new() { AccountId = data.Accounts[0].Id, CategoryId = data.Categories[2].Id,
            Name = "Mortgage", Amount = -1000, IsMortgage = true, StartDate = Today });
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealImportsCountSavingsWithoutMatchingAndUndoDoesNotTurnTheMirrorIntoIncome(bool bothSides)
    {
        using var files = new TestFiles(); var seed = Data();
        seed.Rules.Add(new() { Contains = "bank text", CategoryId = seed.Categories[1].Id });
        using var coordinator = new AdministrationCoordinator(files.Directory, initialize: true);
        coordinator.Replace(coordinator.Read().Revision, seed);
        using var workspace = new ImportWorkspace(files.Directory);
        var profile = new ImportProfile { Name = "Savings test", DateColumn = "Date", DescriptionColumn = "Description",
            AmountColumn = "Amount", BalanceColumn = "Balance", DateFormat = "yyyy-MM-dd", CultureName = "en-GB" };
        async Task<CommitImportResponse> Import(int account, int amount)
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes($"Date;Description;Amount;Balance\n2026-11-10;Original bank text;{amount};{10000 + amount}\n"));
            var upload = await workspace.UploadAsync(source, "statement.csv", CancellationToken.None);
            var preview = workspace.Preview(new(upload.UploadId, seed.Accounts[account].Id, Today, Today, profile), coordinator.Read());
            var row = Assert.Single(preview.Rows); Assert.Equal(seed.Categories[1].Id, row.CategoryId);
            return workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, row.CategoryId, null)]), preview.Revision, coordinator, Today);
        }
        var debit = await Import(0, -500);
        Assert.Equal(0m, debit.Balance.Difference);
        Assert.Equal(10000m, Finance.Balance(coordinator.Read().Data, seed.Accounts[1], Today));
        if (bothSides) Assert.Equal(0m, (await Import(1, 500)).Balance.Difference);
        var data = coordinator.Read().Data; var report = Finance.Report(data, 2026, 11, Today);
        Assert.Equal((0m, 0m, 500m), (report.Income, report.Outflow, report.Savings));
        Assert.Empty(data.TransferMatches); Assert.All(data.Transactions, t => Assert.Null(t.CounterAccountId));
        var undone = coordinator.Update(coordinator.Read().Revision, d => HouseholdEdits.UndoImport(d, debit.BatchId)).Data;
        Assert.Equal((0m, 0m, 0m), (Finance.Report(undone, 2026, 11, Today).Income,
            Finance.Report(undone, 2026, 11, Today).Outflow, Finance.Report(undone, 2026, 11, Today).Savings));
        Assert.Equal(10000m, Finance.Balance(undone, seed.Accounts[0], Today));
        Assert.Equal(bothSides ? 10500m : 10000m, Finance.Balance(undone, seed.Accounts[1], Today));
    }

    [Fact]
    public void PurposeRulesWorkWithoutTransferMatchingAndDeletionChangesOnlyReports()
    {
        var data = Data(); var entry = Entry(data, -500, category: null);
        Categorization.Assign(data, new HashSet<Guid> { entry.Id }, data.Categories[1].Id, "bank text");
        var next = Entry(data, -100, category: null); Finance.ApplySuggestions(data, next);
        Assert.Equal(data.Categories[1].Id, next.CategoryId); Assert.Equal(600m, Finance.Report(data, 2026, 11, MonthEnd).Savings);
        var balances = data.Accounts.Select(a => Finance.Balance(data, a, MonthEnd)).ToArray();
        Categorization.DeleteCategory(data, data.Categories[1].Id); Validation.Validate(data);
        Assert.Equal(0m, Finance.Report(data, 2026, 11, MonthEnd).Savings);
        Assert.Equal(600m, Finance.Report(data, 2026, 11, MonthEnd).Outflow);
        Assert.Equal(balances, data.Accounts.Select(a => Finance.Balance(data, a, MonthEnd))); Assert.Empty(data.Rules);
    }

    [Fact]
    public void Schema7UpgradeBacksUpAndPreservesLegacyTransfersWithoutReclassifyingNames()
    {
        using var files = new TestFiles(); var data = Data();
        foreach (var category in data.Categories) category.Purpose = CategoryPurpose.Normal;
        data.Categories[1].Name = "Sparen";
        var debit = Entry(data, -500); var credit = Entry(data, 500, account: 1, date: Today.AddDays(1));
        TransferMatching.Confirm(data, debit.Id, credit.Id, MonthEnd);
        data.Transactions.Add(new() { AccountId = data.Accounts[0].Id, CounterAccountId = data.Accounts[2].Id,
            Kind = TransactionKind.Transfer, Amount = -200, Date = Today, Description = "Old manual transfer" });
        FirstWeek.ConfirmOpening(data, data.Accounts[0].Id);
        var store = new SqliteHouseholdStore(files.Path("old.db")); store.Save(data);
        using (var connection = new SqliteConnection($"Data Source={files.Path("old.db")};Pooling=False"))
        {
            connection.Open(); using var sql = connection.CreateCommand();
            sql.CommandText = PreviousSchema.RemoveSchema9Columns + "ALTER TABLE [Category] DROP COLUMN [Purpose]; PRAGMA user_version=7;"; sql.ExecuteNonQuery();
        }
        var original = File.ReadAllBytes(files.Path("old.db"));
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(SqliteHouseholdStore.ReadBackup(files.Path("old.db"))));
        Assert.Equal(original, File.ReadAllBytes(files.Path("old.db")));
        Assert.Throws<InvalidDataException>(() => new SqliteHouseholdStore(files.Path("old.db"), allowCreate: false, allowUpgrade: false));
        var migrated = new SqliteHouseholdStore(files.Path("old.db"), allowCreate: false).Load();
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(migrated));
        var backup = Assert.Single(Directory.GetFiles(files.Directory, "*.voor-versie-9-*.db"));
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(SqliteHouseholdStore.ReadBackup(backup)));
        Assert.All(migrated.Categories, c => Assert.Equal(CategoryPurpose.Normal, c.Purpose));
        Assert.Equal(700m, Finance.Report(migrated, 2026, 11, MonthEnd).Savings);
        Assert.Equal(700m, Assert.Single(Finance.Report(migrated, 2026, 11, MonthEnd).SavingsCategories).Saved);
        Assert.Equal(data.Accounts.Select(a => Finance.Balance(data, a, MonthEnd)), migrated.Accounts.Select(a => Finance.Balance(migrated, a, MonthEnd)));
    }

    [Fact]
    public void CategoryPurposesRoundTripThroughBackupsJsonAndCsv()
    {
        using var files = new TestFiles(); var data = Data(); Entry(data, -500, 1); Entry(data, 500, 1, 1);
        var store = new SqliteHouseholdStore(files.Path("current.db")); store.Save(data); store.Backup(files.Path("backup.db"));
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(store.Load()));
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(JsonSerializer.Deserialize<HouseholdData>(JsonSerializer.Serialize(data))));
        store.Save(HouseholdData.CreateEmpty()); store.Restore(files.Path("backup.db"));
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(store.Load()));
        Assert.Equal(500m, Finance.Report(store.Load(), 2026, 11, MonthEnd).Savings);
        ImportService.Export(files.Path("export.csv"), data); var csv = File.ReadAllText(files.Path("export.csv"));
        Assert.Contains("Categorie telt als", csv); Assert.Contains("Categorie-instellingen (JSON)", csv);
        Assert.Contains("Savings", csv); Assert.Contains("Purpose", csv);
        Assert.Contains(HouseholdData.CreateEmpty().Categories, c => c.Purpose == CategoryPurpose.Savings);
        Assert.Contains(HouseholdData.CreateEmpty().Categories, c => c.Purpose == CategoryPurpose.OwnAccountMovement);
    }

    [Theory]
    [InlineData("nl-NL", "Telt als", "Standaard spaardoel per maand (€)")]
    [InlineData("en-GB", "Counts as", "Default monthly savings target (€)")]
    public async Task CategoryEditorPersistsPurposeAndHidesIrrelevantBudgetFields(string culture, string label, string targetLabel)
    {
        using var language = new CultureScope(culture); var data = Data(); using var services = Services(data);
        var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        var draft = EditorDraft.Category(state, data.Categories[1]);
        Assert.Equal(label, draft.Fields.Single(f => f.Key == "purpose").Label);
        var budget = draft.Fields.Single(f => f.Key == "budget"); Assert.Equal(targetLabel, draft.Label(budget));
        draft.Fields.Single(f => f.Key == "purpose").Value = "OwnAccountMovement";
        Assert.False(draft.IsFieldVisible(budget)); draft.Apply(data);
        Assert.Equal(CategoryPurpose.OwnAccountMovement, data.Categories[1].Purpose);
        Assert.Equal(500m, data.Categories[1].MonthlyBudget); // Hidden settings are not silently discarded.
        var normal = EditorDraft.Transaction(state);
        Assert.DoesNotContain(normal.Fields.Single(f => f.Key == "kind").Options, o => o.Value == "Transfer");
        normal.Fields.Single(f => f.Key == "kind").Value = "Transfer";
        Assert.Throws<InvalidOperationException>(() => normal.Apply(data));
    }

    [Theory]
    [InlineData("nl-NL", "Meer opzijgezet dan het maandoverschot", "U gaf", "Doel bereikt")]
    [InlineData("en-GB", "Set aside more than the monthly surplus", "You spent", "Target reached")]
    public async Task OverviewExplainsSavingsWithoutCallingThemOverspending(string culture, string heading, string explanation, string reached)
    {
        using var language = new CultureScope(culture); var data = Data(); var today = DateOnly.FromDateTime(DateTime.Today);
        Entry(data, 3000, date: today); Entry(data, -2600, date: today); Entry(data, -600, 1, date: today);
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        state.Year = today.Year; state.Month = today.Month;
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        async Task<string> Render<T>() where T : IComponent => WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<T>()).ToHtmlString()));
        var dashboard = await Render<Dashboard>();
        Assert.Contains(UiCulture.Text("SavingsAboveSurplusBadge"), dashboard); Assert.Contains(reached, dashboard);
        Assert.DoesNotContain(explanation, dashboard);
        var outlook = await Render<Outlook>();
        Assert.Contains(heading, outlook); Assert.Contains(explanation, outlook);
        Assert.DoesNotContain(UiCulture.Text("SpentMoreThanReceived"), outlook);
        Assert.Contains(WebState.Money(400), dashboard); Assert.Contains(WebState.Money(600), dashboard); Assert.Contains(WebState.Money(-200), dashboard);
        Assert.DoesNotContain(UiCulture.Text("SpentMoreThanReceived"), dashboard); Assert.DoesNotContain("href=\"overboekingen\"", dashboard);
        Assert.Contains("id=\"savings-overview\"", dashboard);
        var budgets = await Render<Budgets>(); Assert.Contains(reached, budgets);
        Assert.DoesNotContain("badge negative", budgets); Assert.Contains(UiCulture.Text("OwnAccountPurpose"), budgets);
        var transactions = await Render<Transactions>(); Assert.DoesNotContain("href=\"overboekingen\"", transactions);
    }

    [Fact]
    public async Task LegacyTransferEditorLocksFinancialFieldsButKeepsOriginalBalances()
    {
        var data = Data(); var debit = Entry(data, -500); var credit = Entry(data, 500, account: 1);
        TransferMatching.Confirm(data, debit.Id, credit.Id, MonthEnd);
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        var draft = EditorDraft.Transaction(state, debit);
        foreach (var field in draft.Fields.Where(f => f.Key is "date" or "account" or "amount" or "kind" or "counter" or "recurring" or "occurrence"))
            Assert.True(field.ReadOnly);
        draft.Fields.Single(f => f.Key == "category").Value = data.Categories[1].Id.ToString(); draft.Apply(data);
        Assert.Single(data.TransferMatches); Assert.Equal(500m, Finance.Report(data, 2026, 11, MonthEnd).Savings);
        Assert.Equal(9500m, Finance.Balance(data, data.Accounts[0], MonthEnd));
    }

    private static ServiceProvider Services(HouseholdData data)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization();
        services.AddSingleton<IWebAdministration>(new Backend(data)); services.AddSingleton<WebState>(); services.AddSingleton<IJSRuntime>(new StaticJs());
        return services.BuildServiceProvider();
    }
    private sealed class Backend(HouseholdData data) : IWebAdministration
    {
        public Task<AdministrationSnapshot> ReadAsync() => Task.FromResult(new AdministrationSnapshot("test", data));
        public Task<AdministrationSnapshot> UpdateAsync(string revision, Action<HouseholdData> change) => throw new NotSupportedException();
        public Task<UploadResponse> UploadAsync(Stream source, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PreviewResponse> PreviewAsync(PreviewRequest request, string revision) => throw new NotSupportedException();
        public Task<PreviewRow> AdjustAsync(AdjustImportRequest request) => throw new NotSupportedException();
        public Task<CommitImportResponse> CommitAsync(CommitImportRequest request, string revision) => throw new NotSupportedException();
        public Task<string> RestoreAsync(Stream source, string revision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class StaticJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException();
    }
    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture, uiCulture = CultureInfo.CurrentUICulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
    }
    private sealed class TestFiles : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opencode", "category-purpose-tests", Guid.NewGuid().ToString("N"));
        public TestFiles() => System.IO.Directory.CreateDirectory(Directory);
        public string Path(string name) => System.IO.Path.Combine(Directory, name);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
