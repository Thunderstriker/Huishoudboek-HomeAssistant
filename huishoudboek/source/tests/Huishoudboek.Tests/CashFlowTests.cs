using System.Net;
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
using Xunit;

namespace Huishoudboek.Tests;

public sealed class CashFlowTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    private static HouseholdData Data(decimal balance = 1000)
    {
        var owner = new Owner { Name = "Eigenaar" };
        return new HouseholdData { Owners = [owner], Accounts = [new Account { Name = "Betaalrekening", OwnerIds = [owner.Id],
            OpeningDate = Today.AddDays(-89), OpeningBalance = balance, MinimumCashBuffer = 100 }] };
    }

    private static RecurringPayment Plan(HouseholdData data, decimal amount, DateOnly due, PaymentFrequency frequency = PaymentFrequency.Once)
    {
        var r = new RecurringPayment { AccountId = data.Accounts[0].Id, Name = amount > 0 ? "Salaris" : "Verzekering",
            Amount = amount, StartDate = due, DayOfMonth = due.Day, Frequency = frequency };
        data.RecurringPayments.Add(r);
        return r;
    }

    private static Transaction Entry(HouseholdData data, decimal amount, DateOnly date, string description = "Bankboeking")
    {
        var t = new Transaction { AccountId = data.Accounts[0].Id, Date = date, Amount = amount, Description = description };
        data.Transactions.Add(t);
        return t;
    }

    [Fact]
    public void PositiveMonthEndDoesNotHideAPrePaydayShortfall()
    {
        var data = Data(500);
        Plan(data, -1200, Today.AddDays(6));
        Plan(data, 3000, new(2026, 11, 25));
        var report = CashFlow.Project(data, Today);
        var account = Assert.Single(report.Accounts);
        Assert.Equal(2300m, account.Days.Last().ClosingBalance);
        Assert.Equal((-700m, Today.AddDays(6)), (account.LowestBalance, account.LowestDate));
        Assert.Equal(0m, account.EstimatedSpendable);
        Assert.True(account.BufferAtRisk);
        Assert.False(account.CanAfford(150));
        Assert.Equal(1200m, report.Bills7Days);
        Assert.Equal(1200m, report.Bills30Days);
        Assert.Equal(2300m, Finance.ProjectedCurrentBalance(data, 2026, 11, Today));
    }

    [Fact]
    public void OtherAccountsAndSavingsNeverAutomaticallyFundABill()
    {
        var data = Data(100);
        var other = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Andere betaalrekening", OpeningBalance = 4000 };
        data.Accounts.Add(other);
        data.Accounts.Add(other with { Id = Guid.NewGuid(), Kind = AccountKind.Savings, Name = "Sparen", OpeningBalance = 10000 });
        Plan(data, -200, Today.AddDays(2));
        var report = CashFlow.Project(data, Today);
        Assert.Equal(2, report.Accounts.Count);
        Assert.Equal(-100m, report.Accounts[0].LowestBalance);
        Assert.False(report.Accounts[0].CanAfford(150));
        Assert.True(report.Accounts[1].CanAfford(150));
        Assert.Equal(3900m, report.EstimatedSpendable);
        var scoped = CashFlow.Project(data, Today, new HashSet<Guid> { data.Accounts[0].Id });
        Assert.Equal(0m, Assert.Single(scoped.Accounts).EstimatedSpendable);
    }

    [Fact]
    public void PaydayDebitsAreCheckedBeforeSameDayIncome()
    {
        var data = Data(500);
        var due = Today.AddDays(3);
        Plan(data, -350, due);
        Plan(data, 2000, due);
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(due, account.NextIncomeDate);
        Assert.Equal(50m, account.EstimatedSpendable);
        var day = account.Days.Single(d => d.Date == due);
        Assert.Equal((150m, 2150m), (day.LowestBalance, day.ClosingBalance));
        Assert.False(account.CanAfford(150));
        Assert.True(account.CanAfford(50));
    }

    [Fact]
    public void PurchaseCheckAlsoProtectsAgainstShortfallsAfterNextIncome()
    {
        var data = Data(1000);
        Plan(data, 100, Today.AddDays(1));
        Plan(data, -950, Today.AddDays(10));
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(900m, account.EstimatedSpendable);
        Assert.Equal(150m, account.LowestBalance);
        Assert.False(account.CanAfford(150));
        Assert.True(account.CanAfford(50));
    }

    [Fact]
    public void DailyVariableCostsAreReservedAndFutureEverydayEntriesAreNotDoubled()
    {
        var data = Data();
        data.Accounts[0].OpeningDate = Today.AddDays(-9);
        Entry(data, -30, Today.AddDays(-2));
        Entry(data, -30, Today.AddDays(-1));
        Entry(data, -30, Today);
        Entry(data, -9, Today.AddDays(1));
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(9m, account.DailyVariableEstimate);
        Assert.Equal((9m, 0m, 901m), (account.Days[0].Payments, account.Days[0].VariableSpending, account.Days[0].ClosingBalance));
        Assert.Equal((0m, 9m), (account.Days[1].Payments, account.Days[1].VariableSpending));
        Assert.Equal(540m, account.EstimatedSpendable);
    }

    [Fact]
    public void ConfirmedTransfersAndLinkedBillsAreExcludedFromDailyRunRate()
    {
        var data = Data();
        var other = data.Accounts[0] with { Id = Guid.NewGuid(), Kind = AccountKind.Savings, Name = "Sparen" };
        data.Accounts.Add(other);
        var outgoing = Entry(data, -200, Today.AddDays(-1));
        var incoming = outgoing with { Id = Guid.NewGuid(), AccountId = other.Id, Amount = 200 };
        data.Transactions.Add(incoming);
        TransferMatching.Confirm(data, outgoing.Id, incoming.Id, Today);
        var plan = Plan(data, -300, Today.AddDays(-2));
        var paid = Entry(data, -303, Today.AddDays(-2));
        RecurringSchedule.Link(data, paid.Id, plan.Id, plan.StartDate);
        Validation.Validate(data);
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(0m, account.DailyVariableEstimate);
        Assert.Equal(497m, account.CurrentBalance);
        Assert.Empty(account.Events);
    }

    [Fact]
    public void OnlyExplicitFutureTransfersAffectTheOtherAccountsProjection()
    {
        var data = Data(100);
        var other = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Andere", OpeningBalance = 4000 };
        data.Accounts.Add(other);
        data.Transactions.Add(new Transaction { AccountId = other.Id, CounterAccountId = data.Accounts[0].Id,
            Amount = -500, Date = Today.AddDays(1), Description = "Geplande transfer", Kind = TransactionKind.Transfer });
        Plan(data, -200, Today.AddDays(2));
        var accounts = CashFlow.Project(data, Today).Accounts;
        Assert.Equal(400m, accounts[0].Days.Last().ClosingBalance);
        Assert.Equal(3500m, accounts[1].Days.Last().ClosingBalance);
        Assert.All(accounts, a => Assert.Null(a.NextIncomeDate));
    }

    [Fact]
    public void ImportsAreStaleBasedOnBothImportTimeAndBankDateAndUndoIsExcluded()
    {
        var data = Data();
        var batch = new ImportBatch { AccountId = data.Accounts[0].Id, FileName = "oud.csv", ImportedAt = Today.ToDateTime(TimeOnly.MinValue), RowCount = 1 };
        data.ImportBatches.Add(batch);
        Entry(data, -10, Today.AddDays(-8)).ImportBatchId = batch.Id;
        var stale = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.True(stale.StaleImports);
        Assert.Equal(Today.AddDays(-8), stale.StatementThrough);
        data.Transactions[0].Date = Today;
        Assert.False(Assert.Single(CashFlow.Project(data, Today).Accounts).StaleImports);
        batch.ImportedAt = Today.AddDays(-8).ToDateTime(TimeOnly.MinValue);
        Assert.True(Assert.Single(CashFlow.Project(data, Today).Accounts).StaleImports);
        HouseholdEdits.UndoImport(data, batch.Id);
        Assert.Null(Assert.Single(CashFlow.Project(data, Today).Accounts).StatementThrough);
        Assert.Null(Assert.Single(CashFlow.Project(data, Today).Accounts).LastImportDate);
    }

    [Fact]
    public void StaleHistoryIsNotDilutedByDaysMissingFromTheImport()
    {
        var data = Data();
        data.Accounts[0].OpeningDate = Today.AddDays(-29);
        var batch = new ImportBatch { AccountId = data.Accounts[0].Id, FileName = "oud.csv", RowCount = 3 };
        data.ImportBatches.Add(batch);
        for (var i = 0; i < 3; i++) Entry(data, -30, Today.AddDays(-20 - i)).ImportBatchId = batch.Id;
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(9m, account.DailyVariableEstimate); // 90 / 10 observed days, not 90 / 30 calendar days.
        Assert.True(account.StaleImports);
    }

    [Fact]
    public void OverdueBillsAreReservedOnceButOverdueIncomeIsNotAssumedToArriveTomorrow()
    {
        var data = Data();
        var bill = Plan(data, -100, new(2026, 8, 2), PaymentFrequency.Monthly);
        Plan(data, 3000, new(2026, 8, 2), PaymentFrequency.Monthly);
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        var missed = Assert.Single(account.Events, e => e.IsOverdue);
        Assert.Equal((Today.AddDays(1), new DateOnly(2026, 11, 2), -100m), (missed.Date, missed.DueDate, missed.Amount));
        Assert.Equal(new DateOnly(2026, 12, 2), account.NextIncomeDate);
        Assert.Contains(account.Warnings, w => w.Contains("achterstallige"));
        Assert.Equal(100m, account.Days[0].Payments);
        Assert.Equal(0m, account.Days[0].Income);
        var transaction = Entry(data, -102, Today.AddDays(2));
        RecurringSchedule.Link(data, transaction.Id, bill.Id, new(2026, 11, 2));
        var afterLink = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.DoesNotContain(afterLink.Events, e => e.IsOverdue);
        Assert.Equal(102m, afterLink.Days[1].Payments);
    }

    [Fact]
    public void ForecastExtendsToIncomeBeyond30DaysAndWarnsWhenNoIncomeIsKnown()
    {
        var data = Data();
        var due = Today.AddDays(60);
        Plan(data, 3000, due, PaymentFrequency.Yearly);
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(due, account.SpendableUntil);
        Assert.Equal(60, account.Days.Count);
        data.RecurringPayments.Clear();
        var unknown = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Null(unknown.NextIncomeDate);
        Assert.Equal(30, unknown.Days.Count);
        Assert.Contains(unknown.Warnings, w => w.Contains("Geen volgende inkomensdatum"));
    }

    [Fact]
    public void FrequenciesClampDatesAndRespectTheirAnchorAndEndDate()
    {
        var data = Data();
        var r = Plan(data, -100, new(2024, 1, 31), PaymentFrequency.Monthly);
        Assert.Equal(new DateOnly(2024, 2, 29), Finance.Occurrence(r, 2024, 2));
        Assert.Equal(new DateOnly(2025, 2, 28), Finance.Occurrence(r, 2025, 2));
        r.Frequency = PaymentFrequency.Quarterly;
        Assert.Null(Finance.Occurrence(r, 2024, 2));
        Assert.Equal(new DateOnly(2024, 4, 30), Finance.Occurrence(r, 2024, 4));
        r.EndDate = new(2024, 4, 30);
        Assert.Equal(new DateOnly(2024, 4, 30), Finance.Occurrence(r, 2024, 4));
        Assert.Null(Finance.Occurrence(r, 2024, 7));
        r.EndDate = null; r.Frequency = PaymentFrequency.Yearly; r.StartDate = new(2024, 2, 29); r.DayOfMonth = 29;
        Assert.Equal(new DateOnly(2025, 2, 28), Finance.Occurrence(r, 2025, 2));
        Assert.Null(Finance.Occurrence(r, 2025, 3));
        r.Frequency = PaymentFrequency.Once;
        Assert.Equal(r.StartDate, Finance.Occurrence(r, 2024, 2));
        Assert.Null(Finance.Occurrence(r, 2025, 2));
        Assert.Single(RecurringSchedule.Occurrences(r, new(2024, 1, 1), new(2026, 1, 1)));
    }

    [Fact]
    public void WiderSalaryToleranceCreatesAProposalAndLinkUsesActualAmountWithoutChangingNextEstimate()
    {
        var data = Data();
        var salary = Plan(data, 3000, new(2026, 11, 1), PaymentFrequency.Monthly);
        salary.MatchText = "WERKGEVER"; salary.MatchTolerance = 10;
        var actual = Entry(data, 3003.25m, new(2026, 10, 31), "Salaris werkgever");
        var candidate = Assert.Single(RecurringSchedule.Candidates(data, Today));
        Assert.Equal(salary.StartDate, candidate.DueDate);
        Assert.Equal(3.25m, candidate.Difference);
        Finance.ApplySuggestions(data, actual);
        Assert.Null(actual.RecurringPaymentId);
        var originalAmount = actual.Amount; var originalDate = actual.Date;
        RecurringSchedule.Link(data, actual.Id, salary.Id, candidate.DueDate);
        Validation.Validate(data);
        var saved = data.Transactions.Single(t => t.Id == actual.Id);
        Assert.Equal((originalAmount, originalDate, "Salaris werkgever"), (saved.Amount, saved.Date, saved.Description));
        Assert.Equal(3000m, salary.Amount);
        Assert.Equal(0m, Finance.Report(data, 2026, 11, Today).Forecast.Income);
        var forecast = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(4003.25m, forecast.CurrentBalance);
        Assert.Equal(new DateOnly(2026, 12, 1), forecast.NextIncomeDate);
        Assert.Equal(3000m, Assert.Single(forecast.Events).Amount);
    }

    [Fact]
    public void FutureCrossMonthLinkReplacesTheEstimateOnTheActualBankDate()
    {
        var data = Data();
        var salary = Plan(data, 3000, new(2026, 12, 1), PaymentFrequency.Monthly);
        var actual = Entry(data, 2997, new(2026, 11, 30));
        RecurringSchedule.Link(data, actual.Id, salary.Id, salary.StartDate);
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(new DateOnly(2026, 11, 30), account.NextIncomeDate);
        Assert.Equal(2997m, Assert.Single(account.Events).Amount);
        Assert.Equal(3997m, account.Days.Last().ClosingBalance);
        Assert.Equal(0m, Finance.Report(data, 2026, 12, Today).Forecast.Income);
        Assert.Equal(2997m, Finance.Report(data, 2026, 11, Today).Forecast.Income);
    }

    [Fact]
    public void StrictAutomaticRecognitionStillWorksAcrossMonthBoundariesAndAvoidsAmbiguity()
    {
        var data = Data();
        var salary = Plan(data, 3000, new(2026, 11, 1), PaymentFrequency.Monthly);
        salary.MatchText = "Werkgever";
        var actual = Entry(data, 3000, new(2026, 10, 31), "Werkgever salaris");
        Finance.ApplySuggestions(data, actual);
        Assert.Equal(salary.Id, actual.RecurringPaymentId);
        Assert.Equal(salary.StartDate, actual.RecurringOccurrenceDate);
        RecurringSchedule.Unlink(data, actual.Id);
        data.RecurringPayments.Add(salary with { Id = Guid.NewGuid(), Name = "Andere werkgever" });
        Finance.ApplySuggestions(data, data.Transactions[0]);
        Assert.Null(data.Transactions[0].RecurringPaymentId);
        Assert.Equal(2, RecurringSchedule.Candidates(data, Today).Count);
    }

    [Fact]
    public void DuplicateOccurrenceLinksAreRejectedEvenWithDifferentBankMonths()
    {
        var data = Data();
        var salary = Plan(data, 3000, new(2026, 11, 1), PaymentFrequency.Monthly);
        var first = Entry(data, 3000, new(2026, 10, 31));
        var second = Entry(data, 2999, new(2026, 11, 2));
        RecurringSchedule.Link(data, first.Id, salary.Id, salary.StartDate);
        Assert.Throws<InvalidOperationException>(() => RecurringSchedule.Link(data, second.Id, salary.Id, salary.StartDate));
        second.RecurringPaymentId = salary.Id; second.RecurringOccurrenceDate = salary.StartDate;
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
    }

    [Fact]
    public void InvalidAccountDirectionAndScheduleLinksAreRejected()
    {
        var data = Data();
        var bill = Plan(data, -100, Today.AddDays(2), PaymentFrequency.Quarterly);
        var income = Entry(data, 100, Today);
        Assert.Throws<InvalidOperationException>(() => RecurringSchedule.Link(data, income.Id, bill.Id, bill.StartDate));
        var actual = Entry(data, -100, Today);
        Assert.Throws<InvalidOperationException>(() => RecurringSchedule.Link(data, actual.Id, bill.Id, bill.StartDate.AddMonths(1)));
        var other = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Andere" };
        data.Accounts.Add(other); actual.AccountId = other.Id;
        Assert.Throws<InvalidOperationException>(() => RecurringSchedule.Link(data, actual.Id, bill.Id, bill.StartDate));
    }

    [Fact]
    public void DeletingAPlanClearsBothLinkFieldsAndPreservesActualCash()
    {
        var data = Data();
        var bill = Plan(data, -100, Today);
        var actual = Entry(data, -103, Today);
        RecurringSchedule.Link(data, actual.Id, bill.Id, Today);
        HouseholdEdits.DeleteRecurring(data, bill.Id);
        Validation.Validate(data);
        var saved = Assert.Single(data.Transactions);
        Assert.Null(saved.RecurringPaymentId); Assert.Null(saved.RecurringOccurrenceDate);
        Assert.Equal(897m, Finance.Balance(data, data.Accounts[0], Today));
    }

    [Fact]
    public void ChangingALinkedScheduleIsRejectedAtomicallyUntilTheLinkIsRemoved()
    {
        using var files = new TestFiles();
        using var coordinator = new AdministrationCoordinator(files.Directory, initialize: true);
        var data = Data();
        var salary = Plan(data, 3000, Today, PaymentFrequency.Monthly);
        var actual = Entry(data, 3001, Today);
        RecurringSchedule.Link(data, actual.Id, salary.Id, Today);
        var saved = coordinator.Replace(coordinator.Read().Revision, data);
        Assert.Throws<InvalidOperationException>(() => coordinator.Update(saved.Revision,
            d => HouseholdEdits.SaveRecurring(d, d.RecurringPayments[0] with { DayOfMonth = Today.Day + 1 })));
        Assert.Equal(saved.Revision, coordinator.Read().Revision);
        Assert.Equal(Today.Day, coordinator.Read().Data.RecurringPayments[0].DayOfMonth);
        var changed = coordinator.Update(saved.Revision, d =>
        {
            RecurringSchedule.Unlink(d, actual.Id);
            HouseholdEdits.SaveRecurring(d, d.RecurringPayments[0] with { DayOfMonth = Today.Day + 1 });
        });
        Assert.Null(changed.Data.Transactions[0].RecurringOccurrenceDate);
        Assert.Equal(Today.Day + 1, changed.Data.RecurringPayments[0].DayOfMonth);
    }

    [Fact]
    public void NewTransactionEditorLinksMustIdentifyAnActualScheduledOccurrence()
    {
        var data = Data();
        var bill = Plan(data, -100, new(2026, 12, 1), PaymentFrequency.Yearly);
        var actual = Entry(data, -99, Today);
        Assert.Throws<InvalidOperationException>(() => HouseholdEdits.SaveTransaction(data, actual with { RecurringPaymentId = bill.Id }));
        HouseholdEdits.SaveTransaction(data, actual with { RecurringPaymentId = bill.Id, RecurringOccurrenceDate = bill.StartDate });
        Validation.Validate(data);
        Assert.Equal(bill.StartDate, data.Transactions[0].RecurringOccurrenceDate);
    }

    [Fact]
    public void LargerDateWindowRequiresReviewEvenIfTheAmountIsExact()
    {
        var data = Data();
        var bill = Plan(data, -100, Today.AddDays(-7));
        bill.MatchText = "verzekeraar"; bill.MatchWindowDays = 10;
        var actual = Entry(data, -100, Today, "Verzekeraar");
        Assert.Single(RecurringSchedule.Candidates(data, Today));
        Finance.ApplySuggestions(data, actual);
        Assert.Null(actual.RecurringPaymentId);
    }

    [Fact]
    public void NonMonthlyPlansAppearOnlyInTheirDueMonthsInMonthlyReports()
    {
        var data = Data();
        Plan(data, -120, new(2026, 11, 10), PaymentFrequency.Quarterly);
        Plan(data, -365, new(2026, 11, 15), PaymentFrequency.Yearly);
        Plan(data, 200, new(2026, 11, 16));
        Assert.Equal(485m, Finance.Report(data, 2026, 11, Today).Forecast.Outflow);
        Assert.Equal(200m, Finance.Report(data, 2026, 11, Today).Forecast.Income);
        Assert.Equal(0m, Finance.Report(data, 2026, 12, Today).Forecast.Outflow);
        Assert.Equal(0m, Finance.Report(data, 2026, 12, Today).Forecast.Income);
        Assert.Equal(120m, Finance.Report(data, 2027, 2, Today).Forecast.Outflow);
    }

    [Fact]
    public void FreshImportsSufficientHistoryAndBufferPermitAClearEstimatedPurchaseAnswer()
    {
        var data = Data(5000);
        var batch = new ImportBatch { AccountId = data.Accounts[0].Id, FileName = "recent.csv", RowCount = 3,
            ImportedAt = Today.ToDateTime(TimeOnly.MinValue) };
        data.ImportBatches.Add(batch);
        for (var i = 0; i < 3; i++) Entry(data, -30, Today.AddDays(-i)).ImportBatchId = batch.Id;
        Plan(data, 3000, Today.AddDays(20));
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.False(account.HasUncertainty);
        Assert.True(account.CanAfford(150));
        data.Accounts[0].MinimumCashBuffer = 0;
        Assert.True(Assert.Single(CashFlow.Project(data, Today).Accounts).HasUncertainty);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.001)]
    public void InvalidBuffersAreRejected(double value)
    {
        var data = Data(); data.Accounts[0].MinimumCashBuffer = (decimal)value;
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
    }

    [Fact]
    public void Schema5BackupsAndRestorePreserveBuffersFrequenciesAndOccurrenceLinks()
    {
        using var files = new TestFiles();
        var data = Data();
        var salary = Plan(data, 3000, Today, PaymentFrequency.Quarterly);
        salary.MatchTolerance = 15; salary.MatchWindowDays = 10;
        var actual = Entry(data, 3002, Today);
        RecurringSchedule.Link(data, actual.Id, salary.Id, Today);
        var store = new SqliteHouseholdStore(files.Database); store.Save(data);
        var serialized = JsonSerializer.Serialize(data);
        Assert.Equal(serialized, JsonSerializer.Serialize(store.Load()));
        var backup = Path.Combine(files.Directory, "backup.db"); store.Backup(backup);
        store.Save(Data()); store.Restore(backup);
        Assert.Equal(serialized, JsonSerializer.Serialize(store.Load()));
        Assert.Equal(serialized, JsonSerializer.Serialize(SqliteHouseholdStore.ReadBackup(backup)));
        var export = Path.Combine(files.Directory, "export.csv"); ImportService.Export(export, data);
        var csv = File.ReadAllText(export);
        Assert.Contains("Geplande betaaldatum", csv);
        Assert.Contains(salary.Id.ToString(), csv);
        Assert.Contains(salary.StartDate.ToString("yyyy-MM-dd"), csv);
    }

    [Fact]
    public void Schema4UpgradeRetainsLegacyMonthlyLinksAndTransferDecisionsWithSafeDefaults()
    {
        using var files = new TestFiles();
        var data = Data();
        var salary = Plan(data, 3000, new(2026, 11, 1), PaymentFrequency.Monthly);
        Entry(data, 3002, Today).RecurringPaymentId = salary.Id;
        var other = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Sparen", Kind = AccountKind.Savings };
        data.Accounts.Add(other);
        var debit = Entry(data, -100, Today);
        var credit = debit with { Id = Guid.NewGuid(), AccountId = other.Id, Amount = 100 };
        data.Transactions.Add(credit); TransferMatching.Confirm(data, debit.Id, credit.Id, Today);
        new SqliteHouseholdStore(files.Database).Save(data);
        Sql(files.Database, "DROP TABLE [TransactionAllocation]; DROP TABLE [ReimbursementClaim]; ALTER TABLE [Account] DROP COLUMN [MinimumCashBuffer]; ALTER TABLE [Transaction] DROP COLUMN [RecurringOccurrenceDate];" +
            "ALTER TABLE [RecurringPayment] DROP COLUMN [Frequency]; ALTER TABLE [RecurringPayment] DROP COLUMN [MatchTolerance];" +
            "ALTER TABLE [RecurringPayment] DROP COLUMN [MatchWindowDays]; PRAGMA user_version=4;");
        var original = File.ReadAllBytes(files.Database);
        var read = SqliteHouseholdStore.ReadBackup(files.Database);
        Assert.Equal(original, File.ReadAllBytes(files.Database));
        Assert.Single(read.TransferMatches);
        Assert.Equal(0m, read.Accounts[0].MinimumCashBuffer);
        Assert.Equal(PaymentFrequency.Monthly, read.RecurringPayments[0].Frequency);
        Assert.Equal(0.01m, read.RecurringPayments[0].MatchTolerance);
        Assert.Equal(5, read.RecurringPayments[0].MatchWindowDays);
        var store = new SqliteHouseholdStore(files.Database, allowCreate: false, allowUpgrade: true);
        Assert.Equal(JsonSerializer.Serialize(read), JsonSerializer.Serialize(store.Load()));
        Assert.Single(System.IO.Directory.GetFiles(files.Directory, "*.voor-versie-6-*.db"));
        Assert.Equal(3002m, Finance.Report(store.Load(), 2026, 11, Today).Income);
        Assert.Equal(3002m, Finance.Report(store.Load(), 2026, 11, Today).Forecast.Income);
    }

    [Fact]
    public async Task SpendingAndRecurringViewsRenderAssumptionsWarningsAndLinkProposals()
    {
        var data = Data();
        var today = DateOnly.FromDateTime(DateTime.Today);
        data.Accounts[0].OpeningDate = today.AddDays(-89);
        var salary = Plan(data, 3000, today, PaymentFrequency.Monthly);
        salary.MatchText = "Werkgever"; salary.MatchTolerance = 10;
        Entry(data, 3005, today, "Werkgever salaris");
        var spending = WebUtility.HtmlDecode(await Render<SafeSpending>(data));
        Assert.Contains("Hoeveel kan ik verantwoord uitgeven?", spending);
        Assert.Contains("Imports zijn verouderd", spending);
        Assert.Contains("Dagelijkse saldoverwachting", spending);
        Assert.Contains("Aannames en grenzen", spending);
        Assert.Contains("onvoldoende zekerheid", spending);
        Assert.Contains("Buffer instellen", spending);
        var recurring = WebUtility.HtmlDecode(await Render<Recurring>(data));
        Assert.Contains("Koppeling bevestigen", recurring);
        Assert.Contains("Handmatig koppelen", recurring);
        Assert.Contains("Werkgever salaris", recurring);
        Assert.Contains("Maandelijks", recurring);
    }

    private static async Task<string> Render<T>(HouseholdData data) where T : IComponent
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization();
        services.AddSingleton<IWebAdministration>(new Backend(data)); services.AddSingleton<WebState>();
        using var provider = services.BuildServiceProvider(); await provider.GetRequiredService<WebState>().LoadAsync();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<T>(ParameterView.Empty)).ToHtmlString());
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

    private static void Sql(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    private sealed class TestFiles : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "opencode", "huishoudboek-tests", Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(Directory, "huishoudboek.db");
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true); }
    }
}
