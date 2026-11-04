using System.Globalization;
using System.Net;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
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

public sealed class TransactionAccountingTests
{
    private static readonly DateOnly Today = new(2026, 11, 30);
    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Eigenaar" };
        return new() { Owners = [owner], Accounts = [new() { Name = "Bank", OwnerIds = [owner.Id],
            OpeningDate = new(2026, 1, 1), OpeningBalance = 1000 }],
            Categories = [new() { Name = "Voeding", MonthlyBudget = 100 }, new() { Name = "Kleding", MonthlyBudget = 50 },
                new() { Name = "Huishouden" }, new() { Name = "Salaris" }] };
    }
    private static Transaction Entry(HouseholdData data, decimal amount, DateOnly? date = null, int category = 0)
    {
        var t = new Transaction { AccountId = data.Accounts[0].Id, Amount = amount, Date = date ?? Today,
            Description = "Oorspronkelijke banktekst", CategoryId = data.Categories[category].Id, StatementBalance = 123.45m };
        data.Transactions.Add(t); return t;
    }
    private static TransactionAllocation Split(Transaction t, decimal amount, Guid? category) => new()
    { TransactionId = t.Id, Amount = amount, CategoryId = category };
    private static ReimbursementClaim Claim(HouseholdData data, Transaction expense, decimal amount, Guid? split = null)
    {
        var claim = new ReimbursementClaim { ExpenseTransactionId = expense.Id, ExpenseAllocationId = split,
            Counterparty = "Werkgever / vriend", ExpectedAmount = amount };
        TransactionAccounting.SaveClaim(data, claim); return claim;
    }
    private static TransactionAllocation Recover(HouseholdData data, Transaction receipt, Transaction expense,
        AllocationTreatment treatment = AllocationTreatment.Refund, Guid? split = null, Guid? claim = null)
    {
        var a = new TransactionAllocation { TransactionId = receipt.Id, Amount = receipt.Amount, Treatment = treatment,
            OriginalTransactionId = claim == null ? expense.Id : null, OriginalAllocationId = split, ReimbursementClaimId = claim };
        TransactionAccounting.SaveAllocations(data, receipt.Id, [a]); return a;
    }

    [Fact]
    public void SupermarketSplitPreservesTheExactBankEntryAndAllCashEffects()
    {
        var data = Data(); var bank = Entry(data, -120);
        var original = JsonSerializer.Serialize(bank);
        var balance = Finance.Balance(data, data.Accounts[0], Today);
        var cash = CashFlow.Project(data, Today).Accounts[0];
        TransactionAccounting.SaveAllocations(data, bank.Id, [Split(bank, -70, data.Categories[0].Id),
            Split(bank, -30, data.Categories[1].Id), Split(bank, -20, data.Categories[2].Id)]);
        Assert.Equal(original, JsonSerializer.Serialize(bank)); Assert.Single(data.Transactions);
        Assert.Equal(balance, Finance.Balance(data, data.Accounts[0], Today));
        var after = CashFlow.Project(data, Today).Accounts[0];
        Assert.Equal(cash.DailyVariableEstimate, after.DailyVariableEstimate);
        Assert.Equal(cash.Days, after.Days);
        var report = Finance.Report(data, 2026, 11, Today);
        Assert.Equal((0m, 120m), (report.Income, report.Outflow));
        Assert.Equal(70m, report.Categories.Single(c => c.Id == data.Categories[0].Id).Spent);
        Assert.Equal(30m, report.Categories.Single(c => c.Id == data.Categories[1].Id).Spent);
        Assert.Equal(20m, DetailedReporting.Budgets(data, 2026, 11, Today)[2].Spent);
        Assert.Equal(report.Outflow, report.Categories.Sum(c => c.Spent));
        Assert.Equal(120m, Reporting.Month(data, 2026, 11, Today).Outflow);
    }

    [Theory]
    [InlineData(-99, false)]
    [InlineData(-100.001, false)]
    [InlineData(100, false)]
    [InlineData(0, false)]
    [InlineData(-100, true)]
    public void SplitsMustExactlyReconcileAndUseValidMoney(decimal amount, bool valid)
    {
        var data = Data(); var bank = Entry(data, -100); var previous = data.TransactionAllocations;
        if (valid) TransactionAccounting.SaveAllocations(data, bank.Id, [Split(bank, amount, null)]);
        else
        {
            Assert.Throws<InvalidOperationException>(() => TransactionAccounting.SaveAllocations(data, bank.Id, [Split(bank, amount, null)]));
            Assert.Same(previous, data.TransactionAllocations); Assert.Empty(data.TransactionAllocations);
        }
        Assert.Equal(-100m, bank.Amount);
    }

    [Fact]
    public void RefundUsesOriginalSplitAndItsReceiptMonthNotIncomeOrHistoricalRewrites()
    {
        var data = Data(); var purchase = Entry(data, -120, new(2026, 10, 31));
        var clothing = Split(purchase, -30, data.Categories[1].Id);
        TransactionAccounting.SaveAllocations(data, purchase.Id, [Split(purchase, -90, data.Categories[0].Id), clothing]);
        var receipt = Entry(data, 15); var original = JsonSerializer.Serialize(data.Transactions);
        Recover(data, receipt, purchase, split: clothing.Id);
        var october = Finance.Report(data, 2026, 10, Today); var november = Finance.Report(data, 2026, 11, Today);
        Assert.Equal(120m, october.Outflow); Assert.Equal((0m, -15m), (november.Income, november.Outflow));
        Assert.Equal(-15m, november.Categories.Single(c => c.Id == clothing.CategoryId).Spent);
        Assert.Equal(15m, november.Available); Assert.Equal(895m, Finance.Balance(data, data.Accounts[0], Today));
        Assert.Equal(105m, DetailedReporting.Year(data, 2026, Today).Outflow);
        Assert.Equal(original, JsonSerializer.Serialize(data.Transactions));
        var label = data.Categories[1]; Categorization.RenameCategory(data, label.Id, "Nieuwe kledingnaam");
        Assert.Contains(Finance.Report(data, 2026, 11, Today).Categories, c => c.Name == "Nieuwe kledingnaam" && c.Spent == -15);
    }

    [Fact]
    public void EmployerAndSharedRepaymentsStayExpensesUntilActualPartialReceipts()
    {
        var data = Data(); var expense = Entry(data, -100, new(2026, 11, 1));
        var claim = Claim(data, expense, 60); var before = Finance.Report(data, 2026, 11, Today);
        Assert.Equal(100m, before.Outflow); Assert.Equal(60m, TransactionAccounting.Outstanding(data, claim, Today));
        Assert.Equal(900m, Finance.Balance(data, data.Accounts[0], Today));
        var first = Entry(data, 20, new(2026, 11, 2));
        Recover(data, first, expense, AllocationTreatment.Reimbursement, claim: claim.Id);
        var second = Entry(data, 40, new(2026, 12, 1));
        Recover(data, second, expense, AllocationTreatment.Reimbursement, claim: claim.Id);
        Assert.Equal(20m, TransactionAccounting.Paid(data, claim, Today));
        Assert.Equal(40m, TransactionAccounting.Outstanding(data, claim, Today));
        Assert.Equal(0m, TransactionAccounting.Outstanding(data, claim, new(2026, 12, 1)));
        Assert.Equal((0m, 80m), (Finance.Report(data, 2026, 11, Today).Income, Finance.Report(data, 2026, 11, Today).Outflow));
        Assert.Equal(-40m, Finance.Report(data, 2026, 12, new(2026, 12, 31)).Outflow);
        Assert.Equal(920m, Finance.Balance(data, data.Accounts[0], Today));
    }

    [Fact]
    public void MixedSalaryReceiptCanSettleSeveralExpensesWithoutInflatingIncome()
    {
        var data = Data(); var first = Entry(data, -50); var second = Entry(data, -30, category: 1);
        var claim1 = Claim(data, first, 50); var claim2 = Claim(data, second, 30);
        var receipt = Entry(data, 3080, category: 3);
        TransactionAccounting.SaveAllocations(data, receipt.Id, [Split(receipt, 3000, data.Categories[3].Id),
            new() { Amount = 50, Treatment = AllocationTreatment.Reimbursement, ReimbursementClaimId = claim1.Id },
            new() { Amount = 30, Treatment = AllocationTreatment.Reimbursement, ReimbursementClaimId = claim2.Id }]);
        var report = Finance.Report(data, 2026, 11, Today);
        Assert.Equal((3000m, 0m, 3000m), (report.Income, report.Outflow, report.Available));
        Assert.Equal(0m, TransactionAccounting.Outstanding(data, claim1, Today));
        Assert.Equal(0m, TransactionAccounting.Outstanding(data, claim2, Today));
        Assert.Equal((3000m, 0m), Categorization.ReviewTotals(data, data.Transactions));
        Assert.Equal(4000m, Finance.Balance(data, data.Accounts[0], Today));
    }

    [Fact]
    public void CategoryOnlyRecoveriesAreExplicitAndSurviveCategoryDeletion()
    {
        var data = Data(); var receipt = Entry(data, 25);
        var allocation = Split(receipt, 25, data.Categories[1].Id); allocation.Treatment = AllocationTreatment.Refund;
        TransactionAccounting.SaveAllocations(data, receipt.Id, [allocation]);
        Assert.Equal(-25m, Finance.Report(data, 2026, 11, Today).Outflow);
        Categorization.DeleteCategory(data, data.Categories[1].Id); Validation.Validate(data);
        Assert.Equal(-25m, Finance.Report(data, 2026, 11, Today).Categories.Single(c => c.Id == Guid.Empty).Spent);
        Assert.Equal(0m, Finance.Report(data, 2026, 11, Today).Income);
    }

    [Fact]
    public void CombinedRefundsAndReimbursementsCannotRecoverMoreThanThePurchase()
    {
        var data = Data(); var purchase = Entry(data, -100); var refund = Entry(data, 60);
        Recover(data, refund, purchase);
        var receipt = Entry(data, 41);
        Assert.Throws<InvalidOperationException>(() => Recover(data, receipt, purchase, AllocationTreatment.Reimbursement));
        Assert.Single(data.TransactionAllocations);
        Assert.Throws<InvalidOperationException>(() => Claim(data, purchase, 41));
        Assert.Empty(data.ReimbursementClaims);
        var claim = Claim(data, purchase, 40);
        Assert.Throws<InvalidOperationException>(() => Claim(data, purchase, 1));
        Assert.Single(data.ReimbursementClaims);
        Assert.Throws<InvalidOperationException>(() => Recover(data, receipt, purchase, AllocationTreatment.Reimbursement, claim: claim.Id));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("income")]
    [InlineData("before")]
    [InlineData("wrong-split")]
    [InlineData("whole-split")]
    [InlineData("duplicate")]
    [InlineData("category")]
    [InlineData("negative")]
    public void InvalidRecoveryLinksAreRejectedWithoutLosingExistingAllocations(string failure)
    {
        var data = Data(); var purchase = Entry(data, -100); var receipt = Entry(data, 10);
        var a = new TransactionAllocation { Amount = 10, Treatment = AllocationTreatment.Refund, OriginalTransactionId = purchase.Id };
        switch (failure)
        {
            case "missing": a.OriginalTransactionId = Guid.NewGuid(); break;
            case "income": a.OriginalTransactionId = receipt.Id; break;
            case "before": receipt.Date = Today.AddDays(-1); break;
            case "wrong-split": a.OriginalAllocationId = Guid.NewGuid(); break;
            case "whole-split": TransactionAccounting.SaveAllocations(data, purchase.Id, [Split(purchase, -100, null)]); break;
            case "duplicate": a.Id = Guid.Empty; break;
            case "category": a.CategoryId = data.Categories[0].Id; break;
            case "negative": a.Amount = -10; break;
        }
        var old = data.TransactionAllocations;
        Assert.Throws<InvalidOperationException>(() => TransactionAccounting.SaveAllocations(data, receipt.Id, [a]));
        Assert.Same(old, data.TransactionAllocations);
    }

    [Fact]
    public void PendingAndReceivedRecoveriesDoNotBecomeTheNextPaydayOrForecastPendingCash()
    {
        var data = Data(); var purchase = Entry(data, -100, Today.AddDays(-1));
        var claim = Claim(data, purchase, 50);
        var before = CashFlow.Project(data, Today).Accounts[0];
        Assert.Null(before.NextIncomeDate); Assert.Empty(before.Events);
        var refund = Entry(data, 10, Today.AddDays(1)); Recover(data, refund, purchase);
        var repayment = Entry(data, 50, Today.AddDays(2)); Recover(data, repayment, purchase, AllocationTreatment.Reimbursement, claim: claim.Id);
        var salary = Entry(data, 3000, Today.AddDays(3), 3);
        var after = CashFlow.Project(data, Today).Accounts[0];
        Assert.Equal(salary.Date, after.NextIncomeDate);
        Assert.Equal(10m, after.Days[0].Income); // Bank cash remains a credit even though this is not report income.
        Assert.Equal(50m, after.Days[1].Income);
        Assert.Equal(50m, TransactionAccounting.Outstanding(data, claim, Today));
        Assert.Equal(0m, Finance.Report(data, 2026, 11, Today).Forecast.Income);
    }

    [Fact]
    public void ClosingUnpaidRemainderDoesNotCreateOrReverseAnyFinancialEntry()
    {
        var data = Data(); var expense = Entry(data, -100); var claim = Claim(data, expense, 60);
        var receipt = Entry(data, 20); Recover(data, receipt, expense, AllocationTreatment.Reimbursement, claim: claim.Id);
        var before = JsonSerializer.Serialize(data.Transactions); var report = Finance.Report(data, 2026, 11, Today);
        TransactionAccounting.SaveClaim(data, claim with { IsClosed = true });
        Assert.Equal(0m, TransactionAccounting.Outstanding(data, data.ReimbursementClaims[0], Today));
        Assert.Equal(report.Outflow, Finance.Report(data, 2026, 11, Today).Outflow);
        Assert.Equal(before, JsonSerializer.Serialize(data.Transactions));
        Assert.Throws<InvalidOperationException>(() => TransactionAccounting.DeleteClaim(data, claim.Id));
    }

    [Fact]
    public void SplitsAndRecoveriesCannotBeTransferMatchedOrSilentlyOverwrittenByRules()
    {
        var data = Data(); var purchase = Entry(data, -100);
        TransactionAccounting.SaveAllocations(data, purchase.Id, [Split(purchase, -100, data.Categories[1].Id)]);
        data.Rules.Add(new() { Contains = "banktekst", CategoryId = data.Categories[2].Id });
        HouseholdEdits.ApplyRules(data);
        Assert.Equal(data.Categories[1].Id, data.TransactionAllocations[0].CategoryId);
        Assert.Throws<InvalidOperationException>(() => Categorization.Assign(data, new HashSet<Guid> { purchase.Id }, data.Categories[2].Id));
        Assert.Empty(Categorization.Preview(data, "banktekst", null).Matches);
        var other = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Andere bank" }; data.Accounts.Add(other);
        var credit = Entry(data, 100); credit.AccountId = other.Id;
        Assert.False(TransferMatching.IsEligible(purchase, data)); Assert.Empty(TransferMatching.Candidates(data, Today));
        Assert.Throws<InvalidOperationException>(() => TransferMatching.Confirm(data, purchase.Id, credit.Id, Today));
        Assert.Throws<InvalidOperationException>(() => HouseholdEdits.SaveTransaction(data, purchase with { Amount = -90 }));
        Assert.Throws<InvalidOperationException>(() => HouseholdEdits.SaveTransaction(data, purchase with { Kind = TransactionKind.Mortgage }));
        HouseholdEdits.SaveTransaction(data, purchase with { UserDescription = "Eigen toelichting" });
        Assert.Equal("Oorspronkelijke banktekst", data.Transactions[0].Description);
    }

    [Fact]
    public void DeletionAndImportUndoProtectSurvivingReceiptsAndCleanUpOnlyRemovedEntries()
    {
        var data = Data(); var purchase = Entry(data, -100); var claim = Claim(data, purchase, 40);
        var receipt = Entry(data, 20); Recover(data, receipt, purchase, AllocationTreatment.Reimbursement, claim: claim.Id);
        var batch = new ImportBatch { AccountId = purchase.AccountId, FileName = "bank.csv", RowCount = 1 };
        data.ImportBatches.Add(batch); purchase.ImportBatchId = batch.Id;
        var snapshot = JsonSerializer.Serialize(data);
        Assert.Throws<InvalidOperationException>(() => HouseholdEdits.DeleteTransaction(data, purchase.Id));
        Assert.Throws<InvalidOperationException>(() => HouseholdEdits.UndoImport(data, batch.Id));
        Assert.Equal(snapshot, JsonSerializer.Serialize(data));
        HouseholdEdits.DeleteTransaction(data, receipt.Id);
        Assert.Equal(40m, TransactionAccounting.Outstanding(data, claim, Today));
        HouseholdEdits.UndoImport(data, batch.Id); Validation.Validate(data);
        Assert.Empty(data.Transactions); Assert.Empty(data.TransactionAllocations); Assert.Empty(data.ReimbursementClaims);
    }

    [Fact]
    public void ScopedReportsUseReceiptAccountAndKeepTheHouseholdBudget()
    {
        var data = Data(); var purchase = Entry(data, -100); var other = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Andere bank" };
        data.Accounts.Add(other); var receipt = Entry(data, 25); receipt.AccountId = other.Id; Recover(data, receipt, purchase);
        var report = Finance.Report(data, 2026, 11, Today, new HashSet<Guid> { other.Id });
        Assert.Equal((0m, -25m), (report.Income, report.Outflow));
        Assert.Equal(100m, report.Categories.Single(c => c.Id == purchase.CategoryId).Budget);
        Assert.Equal(75m, Finance.Report(data, 2026, 11, Today).Outflow);
    }

    [Fact]
    public void DatabaseRoundTripBackupRestoreAndCoordinatorReplacementRetainEveryDetail()
    {
        using var files = new TestFiles(); var data = Data(); var purchase = Entry(data, -100);
        var split = Split(purchase, -100, purchase.CategoryId); TransactionAccounting.SaveAllocations(data, purchase.Id, [split]);
        var claim = Claim(data, purchase, 50, split.Id); var receipt = Entry(data, 20);
        Recover(data, receipt, purchase, AllocationTreatment.Reimbursement, claim: claim.Id);
        var original = JsonSerializer.Serialize(data);
        var store = new SqliteHouseholdStore(files.Path("source.db")); store.Save(data);
        Assert.Equal(original, JsonSerializer.Serialize(store.Load())); store.Backup(files.Path("backup.db"));
        Assert.Equal(original, JsonSerializer.Serialize(SqliteHouseholdStore.ReadBackup(files.Path("backup.db"))));
        var target = new SqliteHouseholdStore(files.Path("target.db")); target.Restore(files.Path("backup.db"));
        Assert.Equal(original, JsonSerializer.Serialize(target.Load()));
        using var coordinator = new AdministrationCoordinator(files.Path("administration"), initialize: true);
        var snapshot = coordinator.Replace(coordinator.Read().Revision, data);
        Assert.Equal(original, JsonSerializer.Serialize(snapshot.Data));
        Assert.Throws<AdministrationConflictException>(() => coordinator.Update("stale", d => TransactionAccounting.SaveDetails(d, receipt.Id, [], [])));
        Assert.Equal(original, JsonSerializer.Serialize(coordinator.Read().Data));
    }

    [Fact]
    public void ExistingRecurringLinkSurvivesSplittingAndPreventsDuplicateForecasts()
    {
        var data = Data(); var expense = Entry(data, -100, new(2026, 11, 1));
        var recurring = new RecurringPayment { AccountId = expense.AccountId, Name = "Vaste kosten", Amount = -100,
            StartDate = expense.Date, DayOfMonth = 1, CategoryId = expense.CategoryId };
        data.RecurringPayments.Add(recurring); RecurringSchedule.Link(data, expense.Id, recurring.Id, expense.Date);
        var original = JsonSerializer.Serialize(data.Transactions[0]);
        TransactionAccounting.SaveAllocations(data, expense.Id, [Split(expense, -40, data.Categories[0].Id), Split(expense, -60, data.Categories[1].Id)]);
        Assert.Equal(original, JsonSerializer.Serialize(data.Transactions[0]));
        var report = Finance.Report(data, 2026, 11, new(2026, 11, 15));
        Assert.Equal(100m, report.Forecast.Outflow); Assert.Equal(0m, report.Forecast.VariableEstimate);
    }

    [Fact]
    public void RemovingRecoveryTreatmentRestoresOrdinaryIncomeWithoutRemovingTheBankReceipt()
    {
        var data = Data(); var expense = Entry(data, -100); var claim = Claim(data, expense, 40);
        var receipt = Entry(data, 20); Recover(data, receipt, expense, AllocationTreatment.Reimbursement, claim: claim.Id);
        var original = JsonSerializer.Serialize(data.Transactions);
        TransactionAccounting.SaveAllocations(data, receipt.Id, []);
        Assert.Equal((20m, 100m), (Finance.Report(data, 2026, 11, Today).Income, Finance.Report(data, 2026, 11, Today).Outflow));
        Assert.Equal(40m, TransactionAccounting.Outstanding(data, claim, Today));
        Assert.Equal(original, JsonSerializer.Serialize(data.Transactions));
    }

    [Fact]
    public void RemovingAnOriginalSplitWithDependentReceiptsRollsBackBothCollections()
    {
        var data = Data(); var expense = Entry(data, -100); var split = Split(expense, -100, expense.CategoryId);
        TransactionAccounting.SaveAllocations(data, expense.Id, [split]);
        var claim = Claim(data, expense, 40, split.Id); var receipt = Entry(data, 20);
        Recover(data, receipt, expense, AllocationTreatment.Reimbursement, claim: claim.Id);
        var oldAllocations = data.TransactionAllocations; var oldClaims = data.ReimbursementClaims;
        Assert.Throws<InvalidOperationException>(() => TransactionAccounting.SaveDetails(data, expense.Id, [], []));
        Assert.Same(oldAllocations, data.TransactionAllocations); Assert.Same(oldClaims, data.ReimbursementClaims);
        Assert.Throws<InvalidOperationException>(() => TransactionAccounting.SaveAllocations(data, expense.Id,
            [Split(expense, -100, data.Categories[1].Id)]));
        Assert.Same(oldAllocations, data.TransactionAllocations); Validation.Validate(data);
    }

    [Fact]
    public void DetailsRequestIsAtomicEvenIfTheSecondCollectionFailsDuringEnumeration()
    {
        var data = Data(); var expense = Entry(data, -100);
        var beforeAllocations = data.TransactionAllocations; var beforeClaims = data.ReimbursementClaims;
        static IEnumerable<ReimbursementClaim> BrokenClaims() { yield return new(); throw new InvalidOperationException("broken input"); }
        Assert.Throws<InvalidOperationException>(() => TransactionAccounting.SaveDetails(data, expense.Id,
            [Split(expense, -100, expense.CategoryId)], BrokenClaims()));
        Assert.Same(beforeAllocations, data.TransactionAllocations); Assert.Same(beforeClaims, data.ReimbursementClaims);
    }

    [Fact]
    public void ExistingSplitTransactionDoesNotDeselectBankStatementRow()
    {
        var data = Data(); var expense = Entry(data, -100);
        TransactionAccounting.SaveAllocations(data, expense.Id, [Split(expense, -60, expense.CategoryId), Split(expense, -40, data.Categories[1].Id)]);
        var table = new ImportTable(["Datum", "Omschrijving", "Bedrag"], [new List<ImportCell>
            { new("2026-11-30"), new(expense.Description), new("-100,00") }]);
        var profile = ImportService.SuggestProfile(table.Headers);
        var preview = Assert.Single(ImportService.Preview(table, profile, expense.AccountId, Today, Today, data));
        Assert.True(preview.Include); Assert.Equal("Gereed", preview.Note);
        Assert.Single(data.Transactions); Assert.Equal(2, data.TransactionAllocations.Count);
    }

    [Fact]
    public async Task UnlinkingInTheDetachedEditorKeepsTheOriginalExpenseCategoryByDefault()
    {
        var data = Data(); var expense = Entry(data, -100, category: 1); var receipt = Entry(data, 20);
        Recover(data, receipt, expense);
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        var draft = new TransactionDetailsDraft(state, receipt);
        draft.Allocations[0].Source = ""; draft.Apply(data);
        var line = Assert.Single(TransactionAccounting.Lines(data, new[] { receipt }));
        Assert.Equal(expense.CategoryId, line.CategoryId); Assert.Equal(-20m, line.Spending); Assert.Equal(0m, line.Income);
    }

    [Fact]
    public async Task TransactionsViewRendersSplitAmountsAndOutstandingRepaymentsWithBankText()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var data = Data(); data.Accounts[0].OpeningDate = today.AddDays(-5); var purchase = Entry(data, -120, today);
        var groceries = Split(purchase, -70, data.Categories[0].Id);
        TransactionAccounting.SaveAllocations(data, purchase.Id, [groceries, Split(purchase, -50, data.Categories[1].Id)]);
        var claim = Claim(data, purchase, 50, groceries.Id); var receipt = Entry(data, 20, today);
        Recover(data, receipt, purchase, AllocationTreatment.Reimbursement, claim: claim.Id);
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        state.Year = today.Year; state.Month = today.Month;
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<Transactions>(ParameterView.Empty)).ToHtmlString()));
        Assert.Contains("Oorspronkelijke banktekst", html); Assert.Contains("Voeding", html); Assert.Contains("Kleding", html);
        Assert.Contains(WebState.Money(-120), html); Assert.Contains(WebState.Money(-70), html); Assert.Contains(WebState.Money(-50), html);
        Assert.Contains("Werkgever / vriend", html); Assert.Contains(WebState.Money(30), html);
        Assert.Contains(UiCulture.Text("ExpenseUntilPaidNote"), html);
    }

    [Fact]
    public void Schema5IsReadWithoutMutationAndMigratesWithABackupAndEmptyDetails()
    {
        using var files = new TestFiles(); var path = files.Path("old.db"); var data = Data(); Entry(data, -120);
        new SqliteHouseholdStore(path).Save(data);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var sql = connection.CreateCommand();
            sql.CommandText = "DROP TABLE [AccountReview]; DROP TABLE [TransactionAllocation]; DROP TABLE [ReimbursementClaim]; PRAGMA user_version=5;";
            sql.ExecuteNonQuery();
        }
        var bytes = File.ReadAllBytes(path); var legacy = SqliteHouseholdStore.ReadBackup(path);
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Empty(legacy.TransactionAllocations); Assert.Empty(legacy.ReimbursementClaims);
        var migrated = new SqliteHouseholdStore(path, allowCreate: false, allowUpgrade: true);
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(migrated.Load()));
        var backup = Assert.Single(Directory.GetFiles(files.Directory, "*.voor-versie-7-*.db"));
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(SqliteHouseholdStore.ReadBackup(backup)));
    }

    [Fact]
    public void CsvExportKeepsOneBankRowWithAllocationAndClaimMetadata()
    {
        using var files = new TestFiles(); var data = Data(); var purchase = Entry(data, -100);
        var claim = Claim(data, purchase, 50); claim.Counterparty = "=HYPERLINK(test)";
        TransactionAccounting.SaveClaim(data, claim);
        var receipt = Entry(data, 20); Recover(data, receipt, purchase, AllocationTreatment.Reimbursement, claim: claim.Id);
        ImportService.Export(files.Path("export.csv"), data);
        using var reader = new StreamReader(files.Path("export.csv"));
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.GetCultureInfo("nl-NL")) { Delimiter = ";" });
        csv.Read(); csv.ReadHeader(); var count = 0; var bankTotal = 0m;
        while (csv.Read())
        {
            count++; bankTotal += csv.GetField<decimal>("Bedrag");
            var allocations = JsonSerializer.Deserialize<List<TransactionAllocation>>(csv.GetField("Splitsingen (JSON)")!)!;
            var claims = JsonSerializer.Deserialize<List<ReimbursementClaim>>(csv.GetField("Vergoedingsverwachtingen (JSON)")!)!;
            if (allocations.Count > 0) Assert.Equal(claim.Id, allocations[0].ReimbursementClaimId);
            if (claims.Count > 0) Assert.Equal(claim.Counterparty, claims[0].Counterparty);
        }
        Assert.Equal(2, count); Assert.Equal(-80m, bankTotal);
    }

    [Theory]
    [InlineData("nl-NL", "-70,00")]
    [InlineData("en-GB", "-70.00")]
    public async Task DetachedDraftAndRenderedEditorSupportBothLanguages(string culture, string amount)
    {
        var previous = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var data = Data(); var purchase = Entry(data, -120); var snapshot = JsonSerializer.Serialize(data);
            using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
            var draft = new TransactionDetailsDraft(state, purchase) { UseAllocations = true };
            draft.Allocations[0].Amount = amount;
            draft.Allocations.Add(new() { Amount = TransactionDetailsDraft.Format(-50), CategoryId = data.Categories[1].Id });
            Assert.Equal(snapshot, JsonSerializer.Serialize(data));
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<TransactionDetails>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Transaction"] = purchase, ["Draft"] = draft }))).ToHtmlString());
            html = WebUtility.HtmlDecode(html); Assert.Contains("Oorspronkelijke banktekst", html);
            Assert.Contains(culture == "en-GB" ? "Split / refund / reimbursement" : "Splitsen / retour / vergoeding", html);
            Assert.Contains(culture == "en-GB" ? "An advanced amount remains an expense" : "Een voorgeschoten bedrag blijft een uitgave", html);
            draft.Apply(data); Assert.Equal(70m, Finance.Report(data, 2026, 11, Today).Categories.Single(c => c.Id == purchase.CategoryId).Spent);
            var editor = EditorDraft.Transaction(state, purchase);
            Assert.True(editor.Fields.Single(f => f.Key == "amount").ReadOnly);
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = ui; }
    }

    private static ServiceProvider Services(HouseholdData data)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization();
        services.AddSingleton<IJSRuntime>(new StaticJsRuntime());
        services.AddSingleton<IWebAdministration>(new Backend(data)); services.AddSingleton<WebState>(); return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RemovingAllDraftRowsCannotSilentlyDisableAnEnabledAllocation()
    {
        var data = Data(); var expense = Entry(data, -100);
        TransactionAccounting.SaveAllocations(data, expense.Id, [Split(expense, -100, expense.CategoryId)]);
        using var services = Services(data); var state = services.GetRequiredService<WebState>(); await state.LoadAsync();
        var draft = new TransactionDetailsDraft(state, expense); draft.Allocations.Clear();
        Assert.Throws<InvalidOperationException>(() => draft.Apply(data)); Assert.Single(data.TransactionAllocations);
        draft.UseAllocations = false; draft.Apply(data); Assert.Empty(data.TransactionAllocations);
    }
    private sealed class Backend(HouseholdData data) : IWebAdministration
    {
        public Task<AdministrationSnapshot> ReadAsync() => Task.FromResult(new AdministrationSnapshot("test-revision", data));
        public Task<AdministrationSnapshot> UpdateAsync(string revision, Action<HouseholdData> change) => throw new NotSupportedException();
        public Task<UploadResponse> UploadAsync(Stream source, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PreviewResponse> PreviewAsync(PreviewRequest request, string revision) => throw new NotSupportedException();
        public Task<PreviewRow> AdjustAsync(AdjustImportRequest request) => throw new NotSupportedException();
        public Task<CommitImportResponse> CommitAsync(CommitImportRequest request, string revision) => throw new NotSupportedException();
        public Task<string> RestoreAsync(Stream source, string revision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class StaticJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException("Static rendering must not execute JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException("Static rendering must not execute JavaScript.");
    }
    private sealed class TestFiles : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "huishoudboek-accounting-" + Guid.NewGuid().ToString("N"));
        public TestFiles() => System.IO.Directory.CreateDirectory(Directory);
        public string Path(string name) => System.IO.Path.Combine(Directory, name);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
