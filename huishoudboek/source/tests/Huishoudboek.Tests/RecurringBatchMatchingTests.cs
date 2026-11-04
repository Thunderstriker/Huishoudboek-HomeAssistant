using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class RecurringBatchMatchingTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private const string Recognition = "Beheren rek. Excellence P. (coop)";

    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Tine" };
        return new HouseholdData { Owners = [owner],
            Accounts = [new Account { Name = "Zichtrekening AXA", OwnerIds = [owner.Id],
                OpeningDate = new(2024, 1, 1), OpeningBalance = 1000, MinimumCashBuffer = 100 }],
            Categories = [new Category { Name = "Bank & Verzekeringen" }] };
    }

    private static RecurringPayment Plan(HouseholdData data) => new()
    {
        Name = "Beheer rekening", AccountId = data.Accounts[0].Id, Amount = -6,
        DayOfMonth = 3, StartDate = new(2024, 1, 1), Frequency = PaymentFrequency.Monthly,
        CategoryId = data.Categories[0].Id, MatchText = Recognition, MatchTolerance = 0, MatchWindowDays = 2
    };

    private static Transaction Entry(HouseholdData data, int offset = 0, decimal amount = -6, string? description = null)
    {
        var entry = new Transaction { AccountId = data.Accounts[0].Id, Date = new DateOnly(2026, 11, 3).AddDays(offset),
            Amount = amount, Description = description ?? $"AXA {Recognition} maandelijkse kosten" };
        data.Transactions.Add(entry);
        return entry;
    }

    private static EditorDraft Draft(HouseholdData data, RecurringPayment plan)
    {
        var state = new WebState(null!);
        state.Data.Accounts = data.Accounts;
        state.Data.Categories = data.Categories;
        return EditorDraft.Recurring(state, plan);
    }

    private static void SaveAndLink(HouseholdData data, RecurringPayment plan, DateOnly? through = null) =>
        Draft(data, plan).PrepareSave(data, through ?? Today).SaveAndLink(data);

    private static Transaction Saved(HouseholdData data, Transaction entry) => data.Transactions.Single(t => t.Id == entry.Id);

    [Theory]
    [InlineData(-2)]
    [InlineData(0)]
    [InlineData(2)]
    public void ConfirmingANewPlanLinksExistingBankEntriesWithinItsConfiguredWindow(int offset)
    {
        var data = Data();
        var entry = Entry(data, offset);
        var batch = new ImportBatch { AccountId = entry.AccountId, FileName = "AXA.csv", RowCount = 1 };
        data.ImportBatches.Add(batch);
        entry.ImportBatchId = batch.Id;
        entry.StatementBalance = 994;
        entry.UserDescription = "Monthly AXA fee";
        var original = entry with { };
        var plan = Plan(data);

        SaveAndLink(data, plan, Today.AddDays(2));
        entry = Saved(data, entry);

        Assert.Equal(plan.Id, entry.RecurringPaymentId);
        Assert.Equal(new DateOnly(2026, 11, 3), entry.RecurringOccurrenceDate);
        Assert.Equal(original with { RecurringPaymentId = plan.Id, RecurringOccurrenceDate = new(2026, 11, 3) }, entry);
        Validation.Validate(data);
        var projection = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.DoesNotContain(projection.Events, e => e.IsOverdue);
        Assert.DoesNotContain(projection.Warnings, w => w.Contains("Beheer rekening"));
        Assert.Equal(0m, projection.DailyVariableEstimate);
    }

    [Fact]
    public void ConfirmingAnExistingPlanChecksPreviouslyImportedEntriesAndIsIdempotent()
    {
        var data = Data();
        var plan = Plan(data);
        data.RecurringPayments.Add(plan);
        var entry = Entry(data);
        Assert.Contains(Assert.Single(CashFlow.Project(data, Today).Accounts).Events, e => e.IsOverdue);

        SaveAndLink(data, plan);
        var saved = JsonSerializer.Serialize(data);
        SaveAndLink(data, plan);

        Assert.Equal(saved, JsonSerializer.Serialize(data));
        Assert.Equal(plan.Id, Saved(data, entry).RecurringPaymentId);
        Assert.Equal(994m, Finance.Balance(data, data.Accounts[0], Today));
    }

    [Fact]
    public void UpdatingRecognitionLinksEachHistoricalOccurrenceWithoutOverwritingCategories()
    {
        var data = Data();
        var plan = Plan(data) with { MatchText = "old recognition" };
        data.RecurringPayments.Add(plan);
        var november = Entry(data);
        november.CategoryId = data.Categories[0].Id;
        var october = Entry(data);
        october.Date = new(2026, 10, 3);
        SaveAndLink(data, plan with { MatchText = $" {Recognition} " });

        Assert.All(data.Transactions, t => Assert.Equal(plan.Id, t.RecurringPaymentId));
        Assert.Equal(new DateOnly(2026, 10, 3), Saved(data, october).RecurringOccurrenceDate);
        Assert.Equal(new DateOnly(2026, 11, 3), Saved(data, november).RecurringOccurrenceDate);
        Assert.Null(Saved(data, october).CategoryId);
        Assert.Equal(data.Categories[0].Id, Saved(data, november).CategoryId);
        Validation.Validate(data);
    }

    [Theory]
    [InlineData(3, -6, null)]
    [InlineData(0, -6.01, null)]
    [InlineData(0, 6, null)]
    [InlineData(0, -6, "Unrelated payment")]
    public void SavingAPlanDoesNotLinkEntriesOutsideItsRecognitionRules(int offset, double amount, string? description)
    {
        var data = Data();
        var entry = Entry(data, offset, (decimal)amount, description);
        SaveAndLink(data, Plan(data));
        Assert.Null(Saved(data, entry).RecurringPaymentId);
    }

    [Theory]
    [InlineData(0, -6.50)]
    [InlineData(7, -6)]
    public void ExplicitBatchConfirmationUsesConfiguredMarginsNotTheStrictImportCaps(int offset, double amount)
    {
        var data = Data();
        var entry = Entry(data, offset, (decimal)amount);
        var plan = Plan(data) with { MatchTolerance = 1, MatchWindowDays = 10 };
        data.RecurringPayments.Add(plan);
        Finance.ApplySuggestions(data, entry);
        Assert.Null(entry.RecurringPaymentId);
        var proposal = Draft(data, plan).PrepareSave(data, Today.AddDays(10));
        Assert.Equal(1, proposal.MatchCount);
        proposal.SaveAndLink(data);
        Assert.Equal(plan.Id, Saved(data, entry).RecurringPaymentId);
        Assert.Equal(-6m, data.RecurringPayments.Single().Amount);
        Assert.Equal((decimal)amount, Saved(data, entry).Amount);
    }

    [Fact]
    public void MultipleBankEntriesForOneOccurrenceAreNotLinkedArbitrarily()
    {
        var data = Data();
        Entry(data);
        Entry(data, 1);
        SaveAndLink(data, Plan(data));
        Assert.All(data.Transactions, t => Assert.Null(t.RecurringPaymentId));
        Assert.Equal(2, RecurringSchedule.Candidates(data, Today).Count);
    }

    [Fact]
    public void OneEntryMatchingMultiplePlansIsLeftForReview()
    {
        var data = Data();
        var plan = Plan(data);
        data.RecurringPayments.Add(plan with { Id = Guid.NewGuid(), Name = "Other bank fee" });
        var entry = Entry(data);
        SaveAndLink(data, plan);
        Assert.Null(Saved(data, entry).RecurringPaymentId);
    }

    [Fact]
    public void SavingDoesNotReplaceAnExistingManualLinkOrLinkAnotherAccountsEntry()
    {
        var data = Data();
        var plan = Plan(data);
        data.RecurringPayments.Add(plan);
        var linked = Entry(data, 0, -6.50m);
        RecurringSchedule.Link(data, linked.Id, plan.Id, new(2026, 11, 3));
        var extra = Entry(data);
        var otherAccount = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Other AXA account" };
        data.Accounts.Add(otherAccount);
        var other = Entry(data);
        other.AccountId = otherAccount.Id;
        var original = JsonSerializer.Serialize(data.Transactions);

        SaveAndLink(data, plan);

        Assert.Equal(original, JsonSerializer.Serialize(data.Transactions));
        Assert.Null(extra.RecurringPaymentId);
        Assert.Null(other.RecurringPaymentId);
        Validation.Validate(data);
    }

    [Fact]
    public void TransfersAndExplicitTransactionDetailsArePreserved()
    {
        var data = Data();
        var other = data.Accounts[0] with { Id = Guid.NewGuid(), Kind = AccountKind.Savings, Name = "Savings" };
        data.Accounts.Add(other);
        var transfer = Entry(data);
        transfer.Kind = TransactionKind.Transfer;
        transfer.CounterAccountId = other.Id;
        var confirmed = Entry(data);
        data.Transactions.Add(confirmed with { Id = Guid.NewGuid(), AccountId = other.Id, Amount = 6 });
        TransferMatching.Confirm(data, confirmed.Id, data.Transactions.Last().Id, Today);
        var split = Entry(data);
        TransactionAccounting.SaveAllocations(data, split.Id, [new TransactionAllocation { Amount = -6 }]);
        var original = JsonSerializer.Serialize(data.Transactions);

        SaveAndLink(data, Plan(data));

        Assert.Equal(original, JsonSerializer.Serialize(data.Transactions));
        Assert.Single(data.TransferMatches);
        Assert.Single(data.TransactionAllocations);
        Validation.Validate(data);
    }

    [Fact]
    public void SavingAnUnrelatedPlanDoesNotUndoAnExplicitUnlink()
    {
        var data = Data();
        var plan = Plan(data);
        data.RecurringPayments.Add(plan);
        var entry = Entry(data);
        RecurringSchedule.Link(data, entry.Id, plan.Id, new(2026, 11, 3));
        RecurringSchedule.Unlink(data, entry.Id);
        SaveAndLink(data, plan with { Id = Guid.NewGuid(), Name = "Insurance", MatchText = "Insurer" });
        Assert.Null(data.Transactions.Single().RecurringPaymentId);
    }

    [Fact]
    public void ConfirmationThroughTheServicePersistsTheBatchWithThePlan()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", "huishoudboek-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using (var coordinator = new AdministrationCoordinator(directory, initialize: true))
            {
                var data = Data();
                Entry(data);
                var saved = coordinator.Replace(coordinator.Read().Revision, data);
                var plan = Plan(data);
                var proposal = Draft(data, plan).PrepareSave(saved.Data, Today);
                var result = coordinator.Update(saved.Revision, proposal.SaveAndLink);
                Assert.Equal(plan.Id, result.Data.Transactions.Single().RecurringPaymentId);
            }
            using var reopened = new AdministrationCoordinator(directory);
            Assert.Equal(new DateOnly(2026, 11, 3), reopened.Read().Data.Transactions.Single().RecurringOccurrenceDate);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void PreviewDoesNotSaveAnythingAndDecliningSavesOnlyThePlan()
    {
        var data = Data();
        var entry = Entry(data);
        var original = JsonSerializer.Serialize(data);
        var proposal = Draft(data, Plan(data)).PrepareSave(data, Today);
        Assert.Equal(1, proposal.MatchCount);
        Assert.Equal(original, JsonSerializer.Serialize(data));
        proposal.SaveOnly(data);
        Assert.Single(data.RecurringPayments);
        Assert.Null(Saved(data, entry).RecurringPaymentId);
        Validation.Validate(data);
    }

    [Fact]
    public void BatchIncludesHistoryOnlyAndSkipsAmbiguousEntriesWithoutBlockingClearMonths()
    {
        var data = Data();
        var october = Entry(data);
        october.Date = new(2026, 10, 3);
        Entry(data);
        Entry(data, 1);
        var future = Entry(data);
        future.Date = new(2026, 12, 3);
        var proposal = Draft(data, Plan(data)).PrepareSave(data, Today);
        Assert.Equal(1, proposal.MatchCount);
        proposal.SaveAndLink(data);
        Assert.NotNull(Saved(data, october).RecurringPaymentId);
        Assert.All(data.Transactions.Where(t => t.Id != october.Id), t => Assert.Null(t.RecurringPaymentId));
    }

    [Fact]
    public void ConfirmationUsesThePreviewedPlanEvenIfDraftFieldsChange()
    {
        var data = Data();
        var entry = Entry(data);
        var draft = Draft(data, Plan(data));
        var proposal = draft.PrepareSave(data, Today);
        draft.Fields.Single(f => f.Key == "name").Value = "Changed after preview";
        proposal.SaveAndLink(data);
        Assert.Equal("Beheer rekening", data.RecurringPayments.Single().Name);
        Assert.NotNull(Saved(data, entry).RecurringPaymentId);
    }

    [Fact]
    public void InvalidPlanIsRejectedBeforeBuildingABatch()
    {
        var data = Data();
        Entry(data);
        var draft = Draft(data, Plan(data));
        draft.Fields.Single(f => f.Key == "day").Value = "0";
        Assert.Throws<InvalidOperationException>(() => draft.PrepareSave(data, Today));
        Assert.Empty(data.RecurringPayments);
        Assert.Null(data.Transactions.Single().RecurringPaymentId);
    }

    [Fact]
    public void SalaryBatchLinksMultipleMonthsAcrossBankMonthBoundariesUsingActualAmounts()
    {
        var data = Data();
        var october = Entry(data, amount: 3002);
        october.Date = new(2026, 9, 30);
        var november = Entry(data, amount: 2998);
        november.Date = new(2026, 10, 31);
        var plan = Plan(data) with { Name = "Salaris Tine", Amount = 3000, DayOfMonth = 1,
            MatchTolerance = 10, MatchWindowDays = 2 };
        var proposal = Draft(data, plan).PrepareSave(data, Today);
        Assert.Equal(2, proposal.MatchCount);
        proposal.SaveAndLink(data);
        Assert.Equal(new DateOnly(2026, 10, 1), Saved(data, october).RecurringOccurrenceDate);
        Assert.Equal(new DateOnly(2026, 11, 1), Saved(data, november).RecurringOccurrenceDate);
        Assert.Equal(3000m, data.RecurringPayments.Single().Amount);
        Assert.Equal(7000m, Finance.Balance(data, data.Accounts[0], Today));
        Assert.DoesNotContain(Assert.Single(CashFlow.Project(data, Today).Accounts).Warnings, w => w.Contains("Salaris Tine"));
    }

    [Fact]
    public void StaleConfirmationCannotSaveThePlanOrAnyOfItsLinks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", "huishoudboek-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var data = Data();
            Entry(data);
            var saved = coordinator.Replace(coordinator.Read().Revision, data);
            var proposal = Draft(data, Plan(data)).PrepareSave(saved.Data, Today);
            coordinator.Update(saved.Revision, d => d.Owners[0].Name = "Updated by another session");
            Assert.Throws<AdministrationConflictException>(() => coordinator.Update(saved.Revision, proposal.SaveAndLink));
            Assert.Empty(coordinator.Read().Data.RecurringPayments);
            Assert.Null(coordinator.Read().Data.Transactions.Single().RecurringPaymentId);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
