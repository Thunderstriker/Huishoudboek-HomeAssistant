using Huishoudboek.Core;
using Huishoudboek.Web;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class HomeOverviewTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    [Fact]
    public void BalancesAndRecentActivityUseTodayEvenWhenViewingAnotherMonth()
    {
        var data = Data(); var account = data.Accounts[0]; var category = data.Categories[0];
        data.Transactions.AddRange([
            new() { AccountId = account.Id, CategoryId = category.Id, Date = Today.AddMonths(-1), Amount = -30, Description = "Last month" },
            new() { AccountId = account.Id, CategoryId = category.Id, Date = Today, Amount = -20, Description = "Today" },
            new() { AccountId = account.Id, CategoryId = category.Id, Date = Today.AddDays(1), Amount = -999, Description = "Future" }
        ]);
        var overview = HomeOverview.Create(data, 2026, 10, Today);
        Assert.Equal(950m, Assert.Single(overview.Accounts).Balance.Total);
        Assert.Equal(30m, overview.Month.Outflow);
        Assert.Equal(new[] { "Today", "Last month" }, overview.Recent.Select(r => r.Transaction.Description));
        Assert.Equal(Today, overview.Today);
        var futureMonth = HomeOverview.Create(data, 2026, 12, Today);
        Assert.Equal(0m, futureMonth.Month.Outflow);
        Assert.Equal(950m, Assert.Single(futureMonth.Accounts).Balance.Total);
    }

    [Fact]
    public void OwnerScopeAppliesToEverySectionAndSharedAccountsAreNotDuplicated()
    {
        var data = Data(); var firstOwner = data.Owners[0]; var secondOwner = new Owner { Name = "Other" };
        data.Owners.Add(secondOwner);
        var shared = data.Accounts[0]; shared.OwnerIds.Add(secondOwner.Id);
        var other = shared with { Id = Guid.NewGuid(), OwnerIds = [secondOwner.Id], Name = "Private" };
        data.Accounts.Add(other);
        data.Transactions.Add(new() { AccountId = other.Id, Date = Today, Amount = -50, Description = "Private purchase" });
        data.RecurringPayments.Add(new() { AccountId = other.Id, Name = "Private bill", Amount = -70,
            Frequency = PaymentFrequency.Once, StartDate = Today.AddDays(1), DayOfMonth = Today.AddDays(1).Day });
        var overview = HomeOverview.Create(data, 2026, 11, Today, firstOwner.Id);
        Assert.Equal(shared.Id, Assert.Single(overview.Accounts).Account.Id);
        Assert.Equal(shared.Id, Assert.Single(overview.CashFlow.Accounts).Account.Id);
        Assert.Empty(overview.Recent); Assert.Empty(overview.Upcoming);
        Assert.Equal(0m, overview.Month.Outflow); Assert.Equal(0, overview.UncategorizedCount);
        Assert.Equal(2, HomeOverview.Create(data, 2026, 11, Today).Accounts.Count);
        var noAccounts = HomeOverview.Create(data, 2026, 11, Today, Guid.NewGuid());
        Assert.Empty(noAccounts.Accounts); Assert.Empty(noAccounts.CashFlow.Accounts);
        Assert.Empty(noAccounts.Recent); Assert.Empty(noAccounts.Upcoming);
    }

    [Fact]
    public void UpcomingPaymentsExcludeUncertainReservesAndRespectTheSevenDayWindow()
    {
        var data = Data(); var id = data.Accounts[0].Id;
        foreach (var (name, days, amount) in new[] { ("Unconfirmed", -1, -50m), ("Soon", 1, -30m),
                     ("Day seven", 7, -15m), ("Too far", 8, -60m), ("Salary", 3, 2500m) })
        {
            var due = Today.AddDays(days);
            data.RecurringPayments.Add(new() { AccountId = id, Name = name, Amount = amount,
                Frequency = PaymentFrequency.Once, StartDate = due, DayOfMonth = due.Day });
        }
        data.Transactions.Add(new() { AccountId = id, Date = Today.AddDays(2), Amount = -25, Description = "Planned entry" });
        var overview = HomeOverview.Create(data, 2026, 11, Today);
        Assert.Equal(new[] { "Soon", "Planned entry", "Day seven" }, overview.Upcoming.Select(p => p.Name));
        Assert.Equal(70m, overview.CashFlow.Bills7Days);
        var account = Assert.Single(overview.CashFlow.Accounts);
        Assert.Equal(50m, account.UncertainReserve);
        Assert.Equal(Today.AddDays(3), account.NextIncomeDate);
    }

    [Fact]
    public void InvestmentValuesSeparateCashAndSecuritiesAndIgnoreFutureValuations()
    {
        var data = Data(); data.Accounts[0].Kind = AccountKind.Investment;
        var account = data.Accounts[0];
        data.Valuations.AddRange([
            new() { AccountId = account.Id, Date = Today.AddDays(-1), SecuritiesValue = 4000 },
            new() { AccountId = account.Id, Date = Today.AddDays(1), SecuritiesValue = 9000 }
        ]);
        data.Accounts.Add(account with { Id = Guid.NewGuid(), OpeningDate = Today.AddDays(1) });
        var overview = HomeOverview.Create(data, 2026, 10, Today);
        var investment = Assert.Single(overview.Accounts);
        Assert.Equal((1000m, 4000m, 5000m), (investment.Balance.Cash, investment.Balance.Securities, investment.Balance.Total));
        Assert.Equal(Today.AddDays(-1), investment.Balance.ValuationDate);
        Assert.Empty(overview.CashFlow.Accounts);
    }

    [Fact]
    public void FreshnessUsesBankCoverageNotTheDateAnOldFileWasUploaded()
    {
        var data = Data(); var account = data.Accounts[0];
        var batch = new ImportBatch { AccountId = account.Id, ImportedAt = Today.ToDateTime(TimeOnly.MinValue),
            CoverageFrom = account.OpeningDate, CoverageThrough = Today.AddDays(-20) };
        data.ImportBatches.Add(batch);
        var old = HomeOverview.Create(data, 2026, 11, Today).Accounts.Single();
        Assert.True(old.StaleImports); Assert.Equal(Today.AddDays(-20), old.ImportedThrough);
        batch.CoverageThrough = Today;
        Assert.False(HomeOverview.Create(data, 2026, 11, Today).Accounts.Single().StaleImports);
        batch.Undone = true;
        Assert.True(HomeOverview.Create(data, 2026, 11, Today).Accounts.Single().StaleImports);
    }

    [Fact]
    public void RecentActivityIsBoundedAndUsesTheIncomingEffectOfHistoricalTransfers()
    {
        var data = Data(); var first = data.Accounts[0];
        var otherOwner = new Owner { Name = "Other" }; data.Owners.Add(otherOwner);
        var second = first with { Id = Guid.NewGuid(), OwnerIds = [otherOwner.Id] }; data.Accounts.Add(second);
        for (var days = 1; days <= 8; days++)
            data.Transactions.Add(new() { AccountId = second.Id, Date = Today.AddDays(-days), Amount = -10, Description = "Older" });
        data.Transactions.Add(new() { AccountId = first.Id, CounterAccountId = second.Id, Kind = TransactionKind.Transfer,
            Date = Today, Amount = -100, Description = "Historical transfer" });
        var overview = HomeOverview.Create(data, 2026, 11, Today, otherOwner.Id);
        Assert.Equal(5, overview.Recent.Count);
        Assert.Equal((second.Id, 100m), (overview.Recent[0].AccountId, overview.Recent[0].Amount));
    }

    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Owner" };
        return new HouseholdData { Owners = [owner],
            Accounts = [new Account { Name = "Zichtrekening", OwnerIds = [owner.Id], OpeningDate = Today.AddMonths(-3), OpeningBalance = 1000 }],
            Categories = [new Category { Name = "Groceries", MonthlyBudget = 100 }] };
    }
}
