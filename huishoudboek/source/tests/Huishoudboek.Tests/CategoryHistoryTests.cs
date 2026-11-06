using System.Text.Json;
using Huishoudboek.Core;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class CategoryHistoryTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Owner" };
        return new() { Owners = [owner],
            Accounts = [new() { Name = "Bank", OwnerIds = [owner.Id], OpeningDate = new(2020, 1, 1) }],
            Categories = [new() { Name = "Food" }, new() { Name = "Clothing" },
                new() { Name = "Saving", Purpose = CategoryPurpose.Savings },
                new() { Name = "Own accounts", Purpose = CategoryPurpose.OwnAccountMovement }] };
    }
    private static Transaction Entry(HouseholdData data, DateOnly date, decimal amount, int category = 0)
    {
        var entry = new Transaction { AccountId = data.Accounts[0].Id, CategoryId = data.Categories[category].Id,
            Date = date, Amount = amount, Description = "Bank entry" };
        data.Transactions.Add(entry); return entry;
    }
    private static CategoryHistoryOverview History(HouseholdData data, DateOnly from, DateOnly through, Guid? owner = null) =>
        CategoryHistory.Create(data, data.Categories[0].Id, from, through, Today, owner);

    [Fact]
    public void FillsAllMonthsAcrossYearBoundariesAndUsesOnlyActualBankDates()
    {
        var data = Data();
        Entry(data, new(2024, 10, 31), -999);
        Entry(data, new(2024, 11, 1), -100);
        Entry(data, new(2024, 12, 31), -200);
        Entry(data, new(2026, 10, 31), -300);
        Entry(data, Today, -50); Entry(data, Today.AddDays(1), -999);
        var history = History(data, new(2024, 11, 20), new(2026, 10, 3));
        Assert.Equal(24, history.Months.Count);
        Assert.Equal(new DateOnly(2024, 11, 1), history.From);
        Assert.Equal(new DateOnly(2026, 10, 31), history.Through);
        Assert.Equal(600m, history.Total); Assert.Equal(25m, history.MonthlyAverage);
        Assert.Equal(21, history.Months.Count(m => m.EntryCount == 0));
        Assert.Equal(300m, history.Peak!.Spent);
        Assert.Equal(100m, history.Months[2].MovingAverage);
        Assert.Null(history.Months[0].MovingAverage); Assert.Null(history.Months[1].MovingAverage);
        Assert.All(history.Months, m => Assert.False(m.IsPartial));
        Assert.Equal(new DateOnly(2025, 11, 1), history.Comparison!.Latest[0].Month);
    }

    [Fact]
    public void SplitsRefundsAndReimbursementsMatchExistingReportsWithoutChangingBankEntries()
    {
        var data = Data(); var food = data.Categories[0].Id;
        var purchase = Entry(data, new(2026, 8, 31), -150, 1);
        var split = new TransactionAllocation { TransactionId = purchase.Id, Amount = -100, CategoryId = food };
        TransactionAccounting.SaveAllocations(data, purchase.Id, [split,
            new() { Amount = -50, CategoryId = data.Categories[1].Id }]);
        var refund = Entry(data, new(2026, 9, 2), 60, 1);
        TransactionAccounting.SaveAllocations(data, refund.Id, [new() { Amount = 60, Treatment = AllocationTreatment.Refund,
            OriginalTransactionId = purchase.Id, OriginalAllocationId = split.Id }]);
        var claim = new ReimbursementClaim { ExpenseTransactionId = purchase.Id, ExpenseAllocationId = split.Id,
            Counterparty = "Friend", ExpectedAmount = 20 };
        TransactionAccounting.SaveClaim(data, claim);
        var receipt = Entry(data, new(2026, 10, 2), 20);
        TransactionAccounting.SaveAllocations(data, receipt.Id, [new() { Amount = 20, Treatment = AllocationTreatment.Reimbursement,
            ReimbursementClaimId = claim.Id }]);
        Entry(data, new(2026, 10, 3), 1000); // Ordinary income in this category is not a refund.
        var before = JsonSerializer.Serialize(data);
        var history = History(data, new(2026, 8, 1), new(2026, 10, 1));
        Assert.Equal(new[] { 100m, -60m, -20m }, history.Months.Select(m => m.Spent));
        Assert.Equal((100m, 80m, 20m), (history.Purchases, history.Recoveries, history.Total));
        Assert.Equal(20m / 3, history.Months[2].MovingAverage);
        foreach (var month in history.Months)
            Assert.Equal(Finance.Report(data, month.Month.Year, month.Month.Month, Today).Categories.Single(c => c.Id == food).Spent, month.Spent);
        // A linked purchase outside the selected period still determines the receipt's category.
        Assert.Equal(-80m, History(data, new(2026, 9, 1), new(2026, 10, 1)).Total);
        Assert.Equal(before, JsonSerializer.Serialize(data));
    }

    [Fact]
    public void OwnerScopeCountsSharedAccountsOnceAndExcludesSavingsTransfersAndIncome()
    {
        var data = Data(); var owner = data.Owners[0]; var other = new Owner { Name = "Other" }; data.Owners.Add(other);
        data.Accounts[0].OwnerIds.Add(other.Id);
        var privateAccount = data.Accounts[0] with { Id = Guid.NewGuid(), OwnerIds = [other.Id] };
        data.Accounts.Add(privateAccount);
        var date = new DateOnly(2026, 10, 1);
        Entry(data, date, -30); Entry(data, date, 500);
        Entry(data, date, -90).AccountId = privateAccount.Id;
        Entry(data, date, -50, 2); Entry(data, date, -60, 3);
        var manual = Entry(data, date, -100); manual.Kind = TransactionKind.Transfer; manual.CounterAccountId = privateAccount.Id;
        var debit = Entry(data, date, -200); var credit = Entry(data, date, 200); credit.AccountId = privateAccount.Id;
        data.TransferMatches.Add(new() { DebitTransactionId = debit.Id, CreditTransactionId = credit.Id });
        Assert.Equal(30m, History(data, date, date, owner.Id).Total);
        Assert.Equal(120m, History(data, date, date).Total);
        Assert.Equal(120m, History(data, date, date, other.Id).Total);
        var empty = History(data, date, date, Guid.NewGuid());
        Assert.False(empty.HasActivity); Assert.Equal(0m, empty.Total); Assert.False(empty.Months[0].CoverageConfirmed);
    }

    [Fact]
    public void PartialMonthIsExcludedFromAllComparativeStatisticsAndFutureEntries()
    {
        var data = Data(); Entry(data, new(2026, 10, 1), -30);
        Entry(data, Today, -1000); Entry(data, Today.AddDays(1), -9000);
        var history = History(data, new(2024, 11, 1), new(2026, 11, 1));
        Assert.Equal(Today, history.Through); Assert.Equal(25, history.Months.Count);
        Assert.Equal(1030m, history.Total); Assert.Equal(30m / 24, history.MonthlyAverage);
        Assert.Equal(30m, history.Peak!.Spent);
        Assert.True(history.Months[^1].IsPartial); Assert.Null(history.Months[^1].MovingAverage);
        Assert.Equal(30m, history.Comparison!.LatestTotal);
        Assert.Equal(new DateOnly(2026, 10, 1), history.Comparison.Latest[^1].Month);
        Assert.Null(History(data, new(2024, 12, 1), new(2026, 11, 1)).Comparison);
        var partialOnly = History(data, new(2026, 11, 1), new(2026, 11, 1));
        Assert.Null(partialOnly.MonthlyAverage); Assert.Null(partialOnly.Peak);
        var monthEnd = CategoryHistory.Create(data, data.Categories[0].Id, new(2026, 11, 1), new(2026, 11, 1), new(2026, 11, 30));
        Assert.False(monthEnd.Months[0].IsPartial);
    }

    [Theory]
    [InlineData(0, 100, null)]
    [InlineData(100, 150, .5)]
    [InlineData(100, 0, -1d)]
    public void ComparesEqualTwelveMonthWindowsWithoutDividingByZero(int previous, int latest, double? change)
    {
        var data = Data(); Entry(data, new(2024, 11, 1), -previous); Entry(data, new(2025, 11, 1), -latest);
        var comparison = History(data, new(2024, 11, 1), new(2026, 10, 1)).Comparison!;
        Assert.Equal(previous, comparison.PreviousTotal); Assert.Equal(latest, comparison.LatestTotal);
        Assert.Equal(latest - previous, comparison.Difference);
        Assert.Equal(change is { } value ? (decimal?)value : null, comparison.Change);
        Assert.Equal(12, comparison.Previous.Count); Assert.Equal(12, comparison.Latest.Count);
    }

    [Fact]
    public void QuietCoveredMonthsAndImportGapsAreDistinguishedAndUndoIsRespected()
    {
        var data = Data(); data.Accounts[0].OpeningDate = new(2026, 8, 10);
        var first = new ImportBatch { AccountId = data.Accounts[0].Id, CoverageFrom = new(2026, 8, 10), CoverageThrough = new(2026, 8, 31) };
        var last = new ImportBatch { AccountId = data.Accounts[0].Id, CoverageFrom = new(2026, 10, 1), CoverageThrough = new(2026, 10, 31) };
        data.ImportBatches.AddRange([first, last]);
        var history = History(data, new(2026, 8, 1), new(2026, 10, 1));
        Assert.False(history.HasActivity);
        Assert.Equal(new[] { true, false, true }, history.Months.Select(m => m.CoverageConfirmed));
        Assert.Equal(1, history.UnconfirmedMonths);
        last.Undone = true;
        Assert.Equal(2, History(data, new(2026, 8, 1), new(2026, 10, 1)).UnconfirmedMonths);
        data.Accounts.Add(data.Accounts[0] with { Id = Guid.NewGuid() });
        Assert.Equal(3, History(data, new(2026, 8, 1), new(2026, 10, 1)).UnconfirmedMonths);
    }

    [Fact]
    public void UncategorizedAllocationsCountAndTwoSplitsInOneCategoryCountAsOneBankEntry()
    {
        var data = Data(); var date = new DateOnly(2026, 10, 1); var entry = Entry(data, date, -100);
        TransactionAccounting.SaveAllocations(data, entry.Id, [new() { Amount = -30, CategoryId = data.Categories[0].Id },
            new() { Amount = -20, CategoryId = data.Categories[0].Id }, new() { Amount = -50 }]);
        Assert.Equal(50m, History(data, date, date).Total); Assert.Equal(1, History(data, date, date).Months[0].EntryCount);
        Assert.Equal(50m, CategoryHistory.Create(data, Guid.Empty, date, date, Today).Total);
    }

    [Theory]
    [InlineData(2026, 10, 2026, 9)]
    [InlineData(2026, 11, 2026, 12)]
    [InlineData(2016, 10, 2026, 10)]
    [InlineData(1899, 12, 1900, 1)]
    public void RejectsInvalidFutureAndExcessiveRanges(int y1, int m1, int y2, int m2)
        => Assert.Throws<InvalidOperationException>(() => History(Data(), new(y1, m1, 1), new(y2, m2, 1)));

    [Fact]
    public void AcceptsTheMaximumRangeAndRejectsUnknownOrNonSpendingCategories()
    {
        var data = Data();
        Assert.Equal(120, History(data, new(2016, 11, 1), new(2026, 10, 1)).Months.Count);
        foreach (var id in new[] { Guid.NewGuid(), data.Categories[2].Id, data.Categories[3].Id })
            Assert.Throws<InvalidOperationException>(() => CategoryHistory.Create(data, id, new(2026, 10, 1), new(2026, 10, 1), Today));
    }
}
