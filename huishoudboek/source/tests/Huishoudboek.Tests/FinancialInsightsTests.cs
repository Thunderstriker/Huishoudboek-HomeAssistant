using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class FinancialInsightsTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    internal static HouseholdData Data(int months = 6, DateOnly? asOf = null)
    {
        var today = asOf ?? Today;
        var first = new DateOnly(today.Year, today.Month, 1);
        var owner = new Owner { Name = "Owner" };
        var data = new HouseholdData { Owners = [owner],
            Accounts = [new() { Name = "Current", OwnerIds = [owner.Id], OpeningDate = first.AddMonths(-12), OpeningBalance = 10000, MinimumCashBuffer = 500 },
                new() { Name = "Savings", Kind = AccountKind.Savings, OwnerIds = [owner.Id], OpeningDate = first.AddMonths(-12), OpeningBalance = 5000 }],
            Categories = [new() { Name = "Essentials", IsEssential = true }, new() { Name = "Leisure" },
                new() { Name = "Home purchase", IsExceptional = true }, new() { Name = "Salary" },
                new() { Name = "Saving", Purpose = CategoryPurpose.Savings }, new() { Name = "Own accounts", Purpose = CategoryPurpose.OwnAccountMovement }] };
        foreach (var account in data.Accounts)
            data.ImportBatches.Add(new() { AccountId = account.Id, CoverageFrom = first.AddMonths(-months), CoverageThrough = today });
        for (var month = first.AddMonths(-months); month < first; month = month.AddMonths(1))
        {
            Entry(data, month, 2000, 3);
            Entry(data, month.AddDays(1), -1000);
            Entry(data, month.AddDays(4), -200, 1);
            Entry(data, month.AddDays(5), -100, 4);
            Entry(data, month.AddDays(5), 100, 4, 1);
        }
        return data;
    }

    internal static Transaction Entry(HouseholdData data, DateOnly date, decimal amount, int category = 0, int account = 0)
    {
        var entry = new Transaction { AccountId = data.Accounts[account].Id, CategoryId = data.Categories[category].Id,
            Date = date, Amount = amount, Description = "Bank entry" };
        data.Transactions.Add(entry);
        return entry;
    }

    [Fact]
    public void UsesEssentialHeadlineAndKeepsLifestyleAndBehaviorSeparateWithoutMutatingData()
    {
        var data = Data(); var before = JsonSerializer.Serialize(data);
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(6, report.CoveredMonths);
        Assert.Equal((1000m, 1200m, 2000m), (report.Freedom.MonthlyEssentials, report.Freedom.MonthlyLifestyle, report.Freedom.MonthlyIncome));
        Assert.Equal(19800m, report.Freedom.UsableReserves);
        Assert.Equal(19.8m, report.Freedom.EssentialMonths);
        Assert.Equal(16.5m, report.Freedom.LifestyleMonths);
        Assert.Equal(1m, report.Fingerprint.Consistency);
        Assert.Equal(1m, report.Fingerprint.SavingRhythm);
        Assert.Equal(.4m, report.Fingerprint.Flexibility);
        Assert.Equal(1m, report.Fingerprint.Concentration);
        Assert.Empty(report.Fingerprint.Discoveries);
        Assert.Equal(before, JsonSerializer.Serialize(data));
    }

    [Fact]
    public void HomePurchaseReducesReservesAndActualTotalsButNeverInflatesLifestyleOrFingerprint()
    {
        var data = Data(); var original = FinancialInsights.Create(data, Today);
        Entry(data, new(2026, 10, 10), -4000, 2);
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(original.Freedom.MonthlyEssentials, report.Freedom.MonthlyEssentials);
        Assert.Equal(original.Freedom.MonthlyLifestyle, report.Freedom.MonthlyLifestyle);
        Assert.Equal(original.Fingerprint.Consistency, report.Fingerprint.Consistency);
        Assert.Equal(original.Freedom.UsableReserves - 4000, report.Freedom.UsableReserves);
        Assert.Equal(15.8m, report.Freedom.EssentialMonths);
        var actual = Finance.Report(data, 2026, 10, Today);
        Assert.Equal(5200m, actual.Outflow); Assert.Equal(4000m, actual.ExceptionalOutflow);
        Assert.Equal(4000m, DetailedReporting.Year(data, 2026, Today).ExceptionalOutflow);
    }

    [Fact]
    public void ExceptionalCategoryChangesAreRetrospectiveButNeverChangeBalances()
    {
        var data = Data(); var transaction = Entry(data, new(2026, 10, 10), -6000, 1);
        var before = FinancialInsights.Create(data, Today);
        var balance = Finance.Balance(data, data.Accounts[0], Today);
        data.Categories[1].IsExceptional = true;
        var after = FinancialInsights.Create(data, Today);
        Assert.Equal(2200m, before.Freedom.MonthlyLifestyle);
        Assert.Equal(1000m, after.Freedom.MonthlyLifestyle);
        Assert.Equal(balance, Finance.Balance(data, data.Accounts[0], Today));
        Assert.Equal(-6000m, transaction.Amount);
    }

    [Fact]
    public void ExceptionalActualsAndFutureDebitsAreNotExtrapolatedButFutureCashIsReserved()
    {
        var data = Data(); Entry(data, Today, -60);
        var normalDaily = CashFlow.Project(data, Today).Accounts[0].DailyVariableEstimate;
        Entry(data, Today, -6000, 2); Entry(data, Today.AddDays(1), -1000, 2);
        var monthly = Finance.Report(data, Today.Year, Today.Month, Today);
        Assert.Equal(6060m, monthly.Outflow);
        Assert.Equal(6000m, monthly.ExceptionalOutflow);
        Assert.Equal(240m, monthly.Forecast.VariableEstimate);
        Assert.Equal(7300m, monthly.Forecast.Outflow);
        var cash = CashFlow.Project(data, Today).Accounts[0];
        Assert.Equal(normalDaily, cash.DailyVariableEstimate);
        Assert.Contains(cash.Events, e => e.Amount == -1000 && e.Date == Today.AddDays(1));
    }

    [Fact]
    public void SplitsAndLinkedRefundsInheritExceptionalTreatmentWithoutTurningRefundsIntoIncome()
    {
        var data = Data(); var baseline = FinancialInsights.Create(data, Today);
        var purchase = Entry(data, new(2026, 9, 10), -1000);
        var exceptional = new TransactionAllocation { TransactionId = purchase.Id, Amount = -800, CategoryId = data.Categories[2].Id };
        TransactionAccounting.SaveAllocations(data, purchase.Id, [exceptional,
            new() { Amount = -200, CategoryId = data.Categories[0].Id }]);
        var refund = Entry(data, new(2026, 10, 10), 300, 3);
        TransactionAccounting.SaveAllocations(data, refund.Id, [new() { Amount = 300, Treatment = AllocationTreatment.Refund,
            OriginalTransactionId = purchase.Id, OriginalAllocationId = exceptional.Id }]);
        var line = Assert.Single(TransactionAccounting.Lines(data, [refund]));
        Assert.True(line.IsExceptional); Assert.Equal(0m, line.Income); Assert.Equal(0m, line.TypicalSpending);
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(baseline.Freedom.MonthlyEssentials + 200m / 6, report.Freedom.MonthlyEssentials);
        Assert.Equal(2000m, report.Freedom.MonthlyIncome);
        Assert.Equal(500m, report.Months.Sum(m => m.Exceptional));
        Assert.Equal(baseline.Freedom.UsableReserves - 700, report.Freedom.UsableReserves);
    }

    [Fact]
    public void AnnualAndQuarterlyEssentialSchedulesAreAmortizedAndLinkedHistoryIsNotCountedTwice()
    {
        var data = Data();
        foreach (var (amount, frequency) in new[] { (-1200m, PaymentFrequency.Yearly), (-300m, PaymentFrequency.Quarterly) })
        {
            var payment = new RecurringPayment { Name = frequency.ToString(), AccountId = data.Accounts[0].Id,
                CategoryId = data.Categories[0].Id, Amount = amount, Frequency = frequency, DayOfMonth = 15, StartDate = new(2026, 6, 15) };
            data.RecurringPayments.Add(payment);
            foreach (var date in RecurringSchedule.Occurrences(payment, new(2026, 5, 1), new(2026, 10, 31)))
                RecurringSchedule.Link(data, Entry(data, date, amount).Id, payment.Id, date);
        }
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(1200m, report.Freedom.MonthlyEssentials);
        Assert.Equal(1400m, report.Freedom.MonthlyLifestyle);
        Assert.Equal(200m, report.Freedom.MonthlyCommitments);
        Assert.Equal(.1m, report.Fingerprint.Commitments);
    }

    [Fact]
    public void SplitRecurringEntriesNormalizeTheirCategoryMixWithoutTreatingExceptionalPartsAsOrdinary()
    {
        var data = Data(); var payment = new RecurringPayment { Name = "Mixed bill", AccountId = data.Accounts[0].Id,
            CategoryId = data.Categories[0].Id, Amount = -100, Frequency = PaymentFrequency.Monthly,
            DayOfMonth = 10, StartDate = new(2026, 5, 10) };
        data.RecurringPayments.Add(payment);
        var entry = Entry(data, new(2026, 10, 10), -100);
        RecurringSchedule.Link(data, entry.Id, payment.Id, entry.Date);
        TransactionAccounting.SaveAllocations(data, entry.Id, [new() { Amount = -30, CategoryId = data.Categories[0].Id },
            new() { Amount = -70, CategoryId = data.Categories[2].Id }]);
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(1030m, report.Freedom.MonthlyEssentials);
        Assert.Equal(1230m, report.Freedom.MonthlyLifestyle);
        Assert.Equal(30m, report.Freedom.MonthlyCommitments);
    }

    [Fact]
    public void KnownExceptionalPaymentBeyondPaydayIsReservedAndItsLinkedEntryIsCountedOnlyOnce()
    {
        var data = Data(); var baseline = FinancialInsights.Create(data, Today).Freedom.UsableReserves;
        var date = Today.AddDays(60);
        var payment = new RecurringPayment { Name = "Home deposit", AccountId = data.Accounts[0].Id,
            CategoryId = data.Categories[2].Id, Amount = -3000, Frequency = PaymentFrequency.Once,
            StartDate = date, DayOfMonth = date.Day };
        data.RecurringPayments.Add(payment);
        var entry = Entry(data, date, -3000, 2);
        RecurringSchedule.Link(data, entry.Id, payment.Id, date);
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(3000m, report.Freedom.ExceptionalReserve);
        Assert.Equal(baseline - 3000, report.Freedom.UsableReserves);
        Assert.Equal(1000m, report.Freedom.MonthlyEssentials);
        Assert.Equal(1200m, report.Freedom.MonthlyLifestyle);
    }

    [Fact]
    public void EarmarksAndExcludedSavingsAreNotSpendableButEmergencyBuffersRemainInRunway()
    {
        var data = Data(); var before = FinancialInsights.Create(data, Today);
        data.Accounts[0].EarmarkedAmount = 1000;
        data.Accounts[1].IncludeInFreedom = false;
        data.Accounts.Add(data.Accounts[1] with { Id = Guid.NewGuid(), Kind = AccountKind.Investment, OpeningBalance = 1000000, IncludeInFreedom = true });
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(13200m, report.Freedom.UsableReserves);
        Assert.Equal(before.Freedom.Spendable - 1000, report.Freedom.Spendable);
        Assert.Equal(1000m, report.Freedom.Earmarked);
        Assert.Equal(13.2m, report.Freedom.EssentialMonths); // The €500 minimum cash buffer is not subtracted here.
    }

    [Fact]
    public void OverdraftsReduceCombinedReservesAndNegativeReservesNeverBecomeNegativeMonths()
    {
        var data = Data(); data.Accounts[0].OpeningBalance = -20000;
        var report = FinancialInsights.Create(data, Today);
        Assert.True(report.Freedom.LiquidBalance < 0);
        Assert.Equal(0m, report.Freedom.UsableReserves);
        Assert.Equal(0m, report.Freedom.EssentialMonths);
    }

    [Fact]
    public void MissingHistoryUnknownEssentialsAndZeroIncomeNeverProduceInfiniteOrInventedScores()
    {
        var data = Data(2); var report = FinancialInsights.Create(data, Today);
        Assert.True(report.LimitedHistory);
        Assert.Null(report.Freedom.EssentialMonths); Assert.Null(report.Fingerprint.Consistency);
        data = Data(); data.Categories[0].IsEssential = false;
        data.Transactions.RemoveAll(t => t.CategoryId == data.Categories[3].Id);
        report = FinancialInsights.Create(data, Today);
        Assert.Null(report.Freedom.EssentialMonths); Assert.NotNull(report.Freedom.LifestyleMonths);
        Assert.Null(report.Freedom.MonthlyIncome); Assert.Null(report.Fingerprint.Flexibility);
    }

    [Fact]
    public void MissingImportsAreNotQuietMonthsAndCurrentOrFutureSpendingDoesNotDistortTheBaseline()
    {
        var data = Data();
        Entry(data, Today, -99999); Entry(data, Today.AddDays(1), -88888);
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(1000m, report.Freedom.MonthlyEssentials);
        Assert.All(report.Months, m => Assert.True(m.Month < new DateOnly(Today.Year, Today.Month, 1)));
        data.ImportBatches.Clear();
        report = FinancialInsights.Create(data, Today);
        Assert.Equal(0, report.CoveredMonths); Assert.True(report.LimitedHistory);
        Assert.Null(report.Freedom.EssentialMonths); Assert.Empty(report.Fingerprint.Discoveries);
    }

    [Fact]
    public void QuietConfirmedMonthsAndMidMonthAccountOpeningsAreHandledExplicitly()
    {
        var data = Data(); data.Transactions.Clear();
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(6, report.CoveredMonths); Assert.Null(report.Fingerprint.Consistency);
        Assert.Equal(0m, report.Fingerprint.SavingRhythm);
        data.Accounts.ForEach(a => a.OpeningDate = new(2026, 8, 10));
        report = FinancialInsights.Create(data, Today);
        Assert.Equal(2, report.CoveredMonths);
    }

    [Fact]
    public void UncategorizedCostsAreIncludedInLifestyleButClearlyNotAssumedEssential()
    {
        var data = Data(); Entry(data, new(2026, 10, 10), -600).CategoryId = null;
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(100m, report.UnclassifiedSpending);
        Assert.Equal(1000m, report.Freedom.MonthlyEssentials);
        Assert.Equal(1300m, report.Freedom.MonthlyLifestyle);
    }

    [Fact]
    public void OwnerFilterCountsSharedAccountsOnceAndNeverIncludesOtherOwnersMoney()
    {
        var data = Data(); var owner = data.Owners[0]; var other = new Owner { Name = "Other" }; data.Owners.Add(other);
        data.Accounts[0].OwnerIds.Add(other.Id); data.Accounts[1].OwnerIds = [other.Id];
        var own = FinancialInsights.Create(data, Today, owner.Id);
        Assert.Equal(14200m, own.Freedom.UsableReserves); Assert.Equal(6, own.CoveredMonths);
        Assert.Equal(19800m, FinancialInsights.Create(data, Today, other.Id).Freedom.UsableReserves);
        var empty = FinancialInsights.Create(data, Today, Guid.NewGuid());
        Assert.Equal(0m, empty.Freedom.UsableReserves); Assert.Null(empty.Freedom.EssentialMonths);
    }

    [Fact]
    public void ExceptionalIncomeDoesNotBecomeTheNextPaycheckOrRegularIncome()
    {
        var data = Data(); Entry(data, new(2026, 10, 10), 50000, 2);
        Entry(data, Today.AddDays(1), 20000, 2);
        Assert.Equal(2000m, FinancialInsights.Create(data, Today).Freedom.MonthlyIncome);
        var cash = CashFlow.Project(data, Today).Accounts[0];
        Assert.Null(cash.NextIncomeDate);
        Assert.Contains(cash.Events, e => e.Amount == 20000);
    }

    [Fact]
    public void ReserveGrowthUsesTheSameBaselineAndIsHiddenForUnconfirmedBalances()
    {
        var data = Data(); Entry(data, Today, 500, 3);
        Assert.Equal(.5m, FinancialInsights.Create(data, Today).Freedom.ReserveChangeMonths);
        data.ImportBatches.ForEach(b => b.CoverageThrough = new(2026, 10, 31));
        var report = FinancialInsights.Create(data, Today);
        Assert.False(report.Freedom.BalancesCovered); Assert.Null(report.Freedom.ReserveChangeMonths);
    }

    [Fact]
    public void DiscoveryComparesDailyRatesRatherThanUnequalPeriodTotalsAndProvidesEvidence()
    {
        var data = Data();
        foreach (var month in Enumerable.Range(5, 6).Select(m => new DateOnly(2026, m, 1)))
        {
            Entry(data, month.AddDays(7), -50, 1);
            Entry(data, month.AddDays(10), -50, 1);
            Entry(data, month.AddMonths(1).AddDays(-2), -150, 1);
        }
        var discovery = Assert.Single(FinancialInsights.Create(data, Today).Fingerprint.Discoveries, d => d.Key == "FingerprintLateMonth");
        // €900 / 30 late days versus €1800 / 154 early days.
        Assert.Equal(30m / (1800m / 154) - 1, discovery.Value);
        Assert.Equal(24, discovery.Evidence.Count);
        Assert.Equal(new DateOnly(2026, 5, 1), discovery.From);
        Assert.Equal(new DateOnly(2026, 10, 31), discovery.Through);
        Assert.All(discovery.Evidence, e => Assert.Equal(data.Categories[1].Id, e.Transaction.CategoryId));
        data.ImportBatches.ForEach(b => b.CoverageFrom = new(2026, 6, 1));
        Assert.Empty(FinancialInsights.Create(data, Today).Fingerprint.Discoveries);
    }

    [Fact]
    public void SplittingFewPurchasesDoesNotArtificiallySatisfyDiscoverySampleSize()
    {
        var data = Data(); data.Transactions.RemoveAll(t => t.CategoryId == data.Categories[1].Id);
        for (var month = new DateOnly(2026, 5, 1); month < new DateOnly(2026, 11, 1); month = month.AddMonths(1))
        {
            var entry = Entry(data, month.AddMonths(1).AddDays(-1), -100, 1);
            TransactionAccounting.SaveAllocations(data, entry.Id,
                Enumerable.Range(0, 20).Select(_ => new TransactionAllocation { Amount = -5, CategoryId = data.Categories[1].Id }));
        }
        Assert.Empty(FinancialInsights.Create(data, Today).Fingerprint.Discoveries);
    }

    [Fact]
    public void LinkedSalaryUsesTheCurrentMonthlyAmountInsteadOfAddingItToHistoricalIncome()
    {
        var data = Data(); var salary = new RecurringPayment { Name = "Salary", AccountId = data.Accounts[0].Id,
            CategoryId = data.Categories[3].Id, Amount = 2000, DayOfMonth = 1, StartDate = new(2026, 5, 1) };
        data.RecurringPayments.Add(salary);
        foreach (var entry in data.Transactions.Where(t => t.CategoryId == data.Categories[3].Id).ToList())
            RecurringSchedule.Link(data, entry.Id, salary.Id, entry.Date);
        salary.Amount = 2500;
        Assert.Equal(2500m, FinancialInsights.Create(data, Today).Freedom.MonthlyIncome);
    }

    [Fact]
    public void ActualMonthlyRefundsRemainSignedWhileTheCostBaselineUsesTheNetWholePeriod()
    {
        var data = Data(); var purchase = Entry(data, new(2026, 7, 10), -2000);
        var refund = Entry(data, new(2026, 10, 10), 2000);
        TransactionAccounting.SaveAllocations(data, refund.Id, [new() { Amount = 2000, Treatment = AllocationTreatment.Refund,
            OriginalTransactionId = purchase.Id }]);
        var report = FinancialInsights.Create(data, Today);
        var october = report.Months.Single(m => m.Month == new DateOnly(2026, 10, 1));
        Assert.Equal(-1000m, october.Essentials); Assert.Equal(-800m, october.Spending);
        Assert.Equal(1000m, report.Freedom.MonthlyEssentials);
    }

    [Fact]
    public void EarmarksAlsoTriggerCashRiskWarningsWhenOrdinaryBillsWouldConsumeProtectedMoney()
    {
        var data = Data(); var account = data.Accounts[0];
        account.EarmarkedAmount = Finance.Balance(data, account, Today) - 1000;
        var cash = CashFlow.Project(data, Today).Accounts[0];
        Assert.True(cash.LowestBalance > account.MinimumCashBuffer);
        Assert.True(cash.BufferAtRisk);
        Assert.False(cash.CanAfford(500));
    }

    [Fact]
    public void FingerprintCanExplainLessPredictableSpendingButDoesNotCallUnstableIncomeStable()
    {
        var data = Data();
        for (var month = new DateOnly(2026, 5, 1); month < new DateOnly(2026, 11, 1); month = month.AddMonths(1))
            for (var day = 8; day <= 11; day++) Entry(data, month.AddDays(day), -10, 1);
        Entry(data, new(2026, 8, 20), -10, 1); Entry(data, new(2026, 9, 20), -20, 1); Entry(data, new(2026, 10, 20), -2000, 1);
        var report = FinancialInsights.Create(data, Today);
        Assert.Contains(report.Fingerprint.Discoveries, d => d.Key == "FingerprintLessPredictable");
        Entry(data, new(2026, 10, 20), 4000, 3);
        Assert.DoesNotContain(FinancialInsights.Create(data, Today).Fingerprint.Discoveries, d => d.Key == "FingerprintLessPredictable");
    }

    [Fact]
    public void CommitmentAndFlexibilityValuesAreNotClippedToTheDrawingRange()
    {
        var data = Data(); data.RecurringPayments.Add(new() { Name = "Large commitment", AccountId = data.Accounts[0].Id,
            CategoryId = data.Categories[0].Id, Amount = -5000, StartDate = Today, DayOfMonth = Today.Day });
        var report = FinancialInsights.Create(data, Today);
        Assert.Equal(2.5m, report.Fingerprint.Commitments);
        Assert.True(report.Fingerprint.Flexibility < 0);
    }

    [Fact]
    public void FlagsAndSettingsAreValidatedAndRoundTripThroughBackups()
    {
        var data = Data(); data.InsightSettings.BufferTargetMonths = 9; data.Accounts[0].EarmarkedAmount = 123.45m;
        data.Accounts[1].IncludeInFreedom = false;
        using var files = new TestFiles(); var store = new SqliteHouseholdStore(files.Database); store.Save(data);
        store.Backup(files.Backup);
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(store.Load()));
        store.Save(HouseholdData.CreateEmpty()); store.Restore(files.Backup);
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(store.Load()));
        data.Categories[0].IsExceptional = true;
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
        data.Categories[0].IsExceptional = false; data.InsightSettings.BufferTargetMonths = 0;
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
        data.InsightSettings.BufferTargetMonths = 6; data.Accounts[0].EarmarkedAmount = -1;
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
    }

    [Fact]
    public void Schema9IsReadWithoutMutationThenUpgradedWithSafeDefaultsAndASeparateBackup()
    {
        using var files = new TestFiles(); var data = Data(); new SqliteHouseholdStore(files.Database).Save(data);
        using (var connection = new SqliteConnection($"Data Source={files.Database};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = PreviousSchema.RemoveSchema10Columns + "PRAGMA user_version=9;";
            command.ExecuteNonQuery();
        }
        var bytes = File.ReadAllBytes(files.Database);
        var old = SqliteHouseholdStore.ReadBackup(files.Database);
        Assert.Equal(bytes, File.ReadAllBytes(files.Database));
        Assert.All(old.Categories, c => { Assert.False(c.IsEssential); Assert.False(c.IsExceptional); });
        Assert.All(old.Accounts, a => { Assert.True(a.IncludeInFreedom); Assert.Equal(0m, a.EarmarkedAmount); });
        Assert.Equal(6, old.InsightSettings.BufferTargetMonths);
        var migrated = new SqliteHouseholdStore(files.Database, allowCreate: false).Load();
        Assert.Equal(JsonSerializer.Serialize(old), JsonSerializer.Serialize(migrated));
        Assert.Single(Directory.GetFiles(files.Directory, "*.voor-versie-11-*.db"));
        Assert.Equal(data.Transactions, migrated.Transactions);
    }

    private sealed class TestFiles : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "opencode", "insights-tests", Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(Directory, "household.db");
        public string Backup => Path.Combine(Directory, "backup.db");
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
    }
}
