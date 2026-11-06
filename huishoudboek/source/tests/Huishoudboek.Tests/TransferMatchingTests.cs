using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class TransferMatchingTests
{
    private static readonly DateOnly Date = new(2026, 11, 3);
    private static readonly DateOnly MonthEnd = new(2026, 11, 30);

    private static HouseholdData CreateData(AccountKind from = AccountKind.Current, AccountKind to = AccountKind.Savings)
    {
        var owner = new Owner { Name = "Eigenaar" };
        var source = new Account { Name = "Van", Kind = from, OpeningDate = new(2026, 1, 1), OpeningBalance = 1000,
            BankReference = "BE000000000001", OwnerIds = [owner.Id] };
        var target = new Account { Name = "Naar", Kind = to, OpeningDate = source.OpeningDate, OpeningBalance = 2000,
            BankReference = "BE000000000002", OwnerIds = [owner.Id] };
        var category = new Category { Name = "Huishouden", MonthlyBudget = 2000 };
        var debitBatch = new ImportBatch { AccountId = source.Id, FileName = "van.csv", RowCount = 1 };
        var creditBatch = new ImportBatch { AccountId = target.Id, FileName = "naar.csv", RowCount = 1 };
        return new HouseholdData
        {
            Owners = [owner], Accounts = [source, target], Categories = [category], ImportBatches = [debitBatch, creditBatch],
            Transactions = [
                new Transaction { AccountId = source.Id, Date = Date, Description = "Naar BE00 0000 0000 02", Amount = -500,
                    CategoryId = category.Id, ImportBatchId = debitBatch.Id, StatementBalance = 500, UserDescription = "Reservering" },
                new Transaction { AccountId = target.Id, Date = Date.AddDays(1), Description = "Ontvangst", Amount = 500,
                    CategoryId = category.Id, ImportBatchId = creditBatch.Id, StatementBalance = 2500 }
            ]
        };
    }

    private static void Confirm(HouseholdData data) => TransferMatching.Confirm(data, data.Transactions[0].Id, data.Transactions[1].Id, MonthEnd);

    [Fact]
    public void ConfirmationCorrectsReportsWithoutChangingAnyBankEntryOrBalance()
    {
        var data = CreateData();
        data.Transactions.Add(new Transaction { AccountId = data.Accounts[0].Id, Date = Date, Description = "Salaris", Amount = 3000 });
        data.Transactions.Add(new Transaction { AccountId = data.Accounts[0].Id, Date = Date, Description = "Lasten", Amount = -1800, CategoryId = data.Categories[0].Id });
        var original = JsonSerializer.Serialize(data.Transactions);
        var balances = data.Accounts.Select(a => Finance.Balance(data, a, MonthEnd)).ToList();
        var before = Finance.Report(data, 2026, 11, MonthEnd);
        Assert.Equal((3500m, 2300m, 0m, 1200m), (before.Income, before.Outflow, before.Savings, before.Available));
        Assert.True(Assert.Single(before.Categories).IsOverBudget);

        Confirm(data);
        Validation.Validate(data);
        Assert.Equal(original, JsonSerializer.Serialize(data.Transactions));
        Assert.Equal(balances, data.Accounts.Select(a => Finance.Balance(data, a, MonthEnd)).ToList());
        var report = Finance.Report(data, 2026, 11, MonthEnd);
        Assert.Equal((3000m, 1800m, 500m, 700m), (report.Income, report.Outflow, report.Savings, report.Available));
        Assert.False(Assert.Single(report.Categories).IsOverBudget);
        Assert.Equal(1800m, Assert.Single(report.Categories).Spent);
        Assert.Equal(500m, Reporting.Month(data, 2026, 11, MonthEnd).Savings);
        Assert.Equal(500m, DetailedReporting.Year(data, 2026, MonthEnd).Savings);

        TransferMatching.Remove(data, Assert.Single(data.TransferMatches).Id);
        Assert.Equal(before, Finance.Report(data, 2026, 11, MonthEnd) with { Categories = before.Categories, SavingsCategories = before.SavingsCategories });
        Assert.Equal(original, JsonSerializer.Serialize(data.Transactions));
    }

    [Theory]
    [InlineData(AccountKind.Current, AccountKind.Savings, 500)]
    [InlineData(AccountKind.Current, AccountKind.Investment, 500)]
    [InlineData(AccountKind.Savings, AccountKind.Current, -500)]
    [InlineData(AccountKind.Investment, AccountKind.Current, -500)]
    [InlineData(AccountKind.Current, AccountKind.Current, 0)]
    [InlineData(AccountKind.Savings, AccountKind.Savings, 0)]
    [InlineData(AccountKind.Savings, AccountKind.Investment, 0)]
    public void ContributionsCountExactlyOnceAccordingToAccountKinds(AccountKind from, AccountKind to, int expected)
    {
        var data = CreateData(from, to);
        Confirm(data);
        var report = Finance.Report(data, 2026, 11, MonthEnd);
        Assert.Equal(0m, report.Income);
        Assert.Equal(0m, report.Outflow);
        Assert.Equal((decimal)expected, report.Savings);
        Assert.Equal((decimal)expected, data.Transactions.Sum(t => Finance.SavingsEffect(data, t)));
    }

    [Fact]
    public void PairedEntriesNeverBecomeVariableSpendingOrDoubleAProjectedBalance()
    {
        var data = CreateData();
        Confirm(data);
        var today = Date.AddDays(7);
        var report = Finance.Report(data, 2026, 11, today);
        Assert.Equal(0m, report.Forecast.VariableEstimate);
        Assert.Equal(0m, report.Forecast.Outflow);
        Assert.Equal(500m, report.Forecast.Savings);
        Assert.Equal(500m, Finance.ProjectedCurrentBalance(data, 2026, 11, today));
        Assert.Equal(2500m, Finance.Balance(data, data.Accounts[1], today));
        foreach (var entry in data.Transactions)
        {
            var check = ImportBalance.Check(data, entry.AccountId,
                [new StatementBalanceObservation(1, entry.Date, entry.Amount, entry.StatementBalance)], today);
            Assert.Equal(StatementBalanceStatus.Available, check.Status);
            Assert.Equal(0m, check.Difference);
        }
    }

    [Theory]
    [InlineData(AccountKind.Current, AccountKind.Savings, 500, 0)]
    [InlineData(AccountKind.Savings, AccountKind.Current, 0, -500)]
    public void CrossMonthContributionsUseTheCurrentAccountsOwnDate(AccountKind from, AccountKind to, int november, int december)
    {
        var data = CreateData(from, to);
        data.Transactions[0].Date = MonthEnd;
        data.Transactions[1].Date = new(2026, 12, 1);
        ConfirmAtDecember(data);
        Assert.Equal((decimal)november, Finance.Report(data, 2026, 11, new(2026, 12, 31)).Savings);
        Assert.Equal((decimal)december, Finance.Report(data, 2026, 12, new(2026, 12, 31)).Savings);
        Assert.Equal(0m, Finance.Report(data, 2026, 11, new(2026, 12, 31)).Outflow);
        Assert.Equal(0m, Finance.Report(data, 2026, 12, new(2026, 12, 31)).Income);
        Assert.Equal(500m, Finance.Balance(data, data.Accounts[0], MonthEnd));
        Assert.Equal(2000m, Finance.Balance(data, data.Accounts[1], MonthEnd));
    }

    private static void ConfirmAtDecember(HouseholdData data) => TransferMatching.Confirm(data, data.Transactions[0].Id, data.Transactions[1].Id, new(2026, 12, 31));

    [Fact]
    public void EitherOwnerScopeSeesOneFullContributionButAnUnrelatedScopeSeesNone()
    {
        var data = CreateData();
        var secondOwner = new Owner { Name = "Tweede eigenaar" };
        data.Owners.Add(secondOwner);
        data.Accounts[1].OwnerIds = [secondOwner.Id];
        Confirm(data);
        foreach (var account in data.Accounts)
        {
            var scope = new HashSet<Guid> { account.Id };
            var report = Finance.Report(data, 2026, 11, MonthEnd, scope);
            Assert.Equal(500m, report.Savings);
            Assert.Equal(0m, report.Income);
            Assert.Equal(0m, report.Outflow);
        }
        Assert.Equal(0m, Finance.Report(data, 2026, 11, MonthEnd, new HashSet<Guid>()).Savings);
        Assert.Equal(500m, DetailedReporting.Dashboard(data, 2026, 11, MonthEnd, secondOwner.Id).Report.Savings);
    }

    [Fact]
    public void SuggestionsAreNonMutatingAndFlagCompetingReceipts()
    {
        var data = CreateData();
        data.Transactions.Add(data.Transactions[1] with { Id = Guid.NewGuid(), Date = Date.AddDays(2), Description = "Andere ontvangst" });
        var original = JsonSerializer.Serialize(data);
        var candidates = TransferMatching.Candidates(data, MonthEnd).ToList();
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => { Assert.True(c.HasAlternatives); Assert.True(c.HasAccountReference); });
        Assert.Equal(original, JsonSerializer.Serialize(data));
        Confirm(data);
        Assert.Empty(TransferMatching.Candidates(data, MonthEnd));
        Assert.Throws<InvalidOperationException>(() => TransferMatching.Confirm(data, data.Transactions[0].Id, data.Transactions[2].Id, MonthEnd));
    }

    [Fact]
    public void RejectingIsReversibleAndDoesNotBlockOtherCandidates()
    {
        var data = CreateData();
        data.Transactions.Add(data.Transactions[1] with { Id = Guid.NewGuid(), Description = "Andere ontvangst" });
        TransferMatching.Reject(data, data.Transactions[0].Id, data.Transactions[1].Id, MonthEnd);
        Assert.False(Assert.Single(data.TransferMatches).IsConfirmed);
        Assert.Equal(data.Transactions[2].Id, Assert.Single(TransferMatching.Candidates(data, MonthEnd)).Credit.Id);
        Assert.Equal(1000m, Finance.Report(data, 2026, 11, MonthEnd).Income);
        TransferMatching.Remove(data, Assert.Single(data.TransferMatches).Id);
        Assert.Equal(2, TransferMatching.Candidates(data, MonthEnd).Count());
        TransferMatching.Reject(data, data.Transactions[0].Id, data.Transactions[1].Id, MonthEnd);
        Confirm(data);
        Assert.True(Assert.Single(data.TransferMatches).IsConfirmed);
    }

    [Fact]
    public void ManualMatchingCanUseALargerDateWindowButNeverInventsAMissingSide()
    {
        var data = CreateData();
        data.Transactions[1].Date = Date.AddDays(10);
        Assert.Empty(TransferMatching.Candidates(data, MonthEnd));
        Confirm(data);
        Assert.Equal(2, data.Transactions.Count);
        Assert.Throws<InvalidOperationException>(() => TransferMatching.Confirm(data, data.Transactions[0].Id, Guid.NewGuid(), MonthEnd));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("account")]
    [InlineData("future")]
    [InlineData("recurring")]
    [InlineData("manualTransfer")]
    public void InvalidOrUnsafePairsAreNotConfirmed(string change)
    {
        var data = CreateData();
        switch (change)
        {
            case "amount": data.Transactions[1].Amount = 499; break;
            case "account": data.Transactions[1].AccountId = data.Transactions[0].AccountId; break;
            case "future": data.Transactions[1].Date = MonthEnd.AddDays(1); break;
            case "recurring": data.Transactions[0].RecurringPaymentId = Guid.NewGuid(); break;
            case "manualTransfer": data.Transactions[0].Kind = TransactionKind.Transfer; data.Transactions[0].CounterAccountId = data.Accounts[1].Id; break;
        }
        Assert.Empty(TransferMatching.Candidates(data, MonthEnd));
        Assert.Throws<InvalidOperationException>(() => Confirm(data));
        Assert.Empty(data.TransferMatches);
    }

    [Fact]
    public void OneSidedImportKeepsItsExistingReportAndOtherAccountUntouched()
    {
        var data = CreateData();
        data.Transactions.RemoveAt(1);
        Assert.Empty(TransferMatching.Candidates(data, MonthEnd));
        Assert.Equal(500m, Finance.Report(data, 2026, 11, MonthEnd).Outflow);
        Assert.Equal(0m, Finance.Report(data, 2026, 11, MonthEnd).Savings);
        Assert.Equal(2000m, Finance.Balance(data, data.Accounts[1], MonthEnd));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("date")]
    [InlineData("account")]
    [InlineData("kind")]
    [InlineData("recurring")]
    public void FinancialEditsInvalidateTheDecision(string change)
    {
        var data = CreateData();
        Confirm(data);
        var entry = data.Transactions[0];
        var changed = change switch
        {
            "amount" => entry with { Amount = -600 },
            "date" => entry with { Date = Date.AddDays(1) },
            "account" => entry with { AccountId = data.Accounts[1].Id },
            "kind" => entry with { Kind = TransactionKind.Transfer, CounterAccountId = data.Accounts[1].Id },
            _ => entry with { RecurringPaymentId = Guid.NewGuid() }
        };
        HouseholdEdits.SaveTransaction(data, changed);
        Assert.Empty(data.TransferMatches);
        Assert.Equal(500m, data.Transactions[1].Amount);
    }

    [Fact]
    public void LabelsAndDescriptionsKeepTheMatchAndRulesCannotRelinkItAsABill()
    {
        var data = CreateData();
        Confirm(data);
        HouseholdEdits.SaveTransaction(data, data.Transactions[0] with { UserDescription = "Vakantiepot", CategoryId = null });
        Assert.Single(data.TransferMatches);
        data.RecurringPayments.Add(new RecurringPayment { AccountId = data.Accounts[0].Id, Name = "Vergelijkbaar", Amount = -500,
            DayOfMonth = Date.Day, StartDate = new(2026, 1, 1), MatchText = "Naar" });
        data.Rules.Add(new CategorizationRule { Contains = "Naar", CategoryId = data.Categories[0].Id });
        HouseholdEdits.ApplyRules(data);
        Assert.Null(data.Transactions[0].RecurringPaymentId);
        Assert.Null(data.Transactions[0].CategoryId);
        Assert.Empty(Categorization.Preview(data, "Naar", data.Categories[0].Id).Matches);
        Assert.Throws<InvalidOperationException>(() => Categorization.Assign(data, new HashSet<Guid> { data.Transactions[0].Id }, data.Categories[0].Id, "Naar"));
        Assert.Equal((0m, 0m), Categorization.ReviewTotals(data.Transactions, TransferMatching.ConfirmedTransactionIds(data)));
        Validation.Validate(data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletionAndImportUndoRemoveLinksButNotTheOtherBankEntry(bool undo)
    {
        var data = CreateData();
        Confirm(data);
        var receipt = data.Transactions[1] with { };
        if (undo) HouseholdEdits.UndoImport(data, data.ImportBatches[0].Id);
        else HouseholdEdits.DeleteTransaction(data, data.Transactions[0].Id);
        Validation.Validate(data);
        Assert.Empty(data.TransferMatches);
        Assert.Equal(receipt, Assert.Single(data.Transactions));
        Assert.Equal(500m, Finance.Report(data, 2026, 11, MonthEnd).Income);
        Assert.Equal(2500m, Finance.Balance(data, data.Accounts[1], MonthEnd));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("reused")]
    [InlineData("amount")]
    [InlineData("duplicate")]
    [InlineData("recurring")]
    public void AggregateValidationRejectsInvalidLinksFromOtherClients(string change)
    {
        var data = CreateData();
        Confirm(data);
        switch (change)
        {
            case "missing": data.Transactions.RemoveAt(1); break;
            case "amount": data.Transactions[1].Amount = 501; break;
            case "duplicate": data.TransferMatches.Add(data.TransferMatches[0] with { Id = Guid.NewGuid() }); break;
            case "recurring":
                var recurring = new RecurringPayment { Name = "Post", AccountId = data.Accounts[0].Id, Amount = -500, StartDate = Date };
                data.RecurringPayments.Add(recurring); data.Transactions[0].RecurringPaymentId = recurring.Id; break;
            case "reused":
                data.Transactions.Add(data.Transactions[1] with { Id = Guid.NewGuid() });
                data.TransferMatches.Add(new TransferMatch { DebitTransactionId = data.Transactions[0].Id, CreditTransactionId = data.Transactions[2].Id }); break;
        }
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
    }

    [Fact]
    public void DatabaseBackupsAndJsonClonesPreserveConfirmedAndRejectedDecisions()
    {
        using var files = new TestFiles();
        var data = CreateData();
        data.Transactions.Add(data.Transactions[1] with { Id = Guid.NewGuid() });
        TransferMatching.Reject(data, data.Transactions[0].Id, data.Transactions[2].Id, MonthEnd);
        Confirm(data);
        var store = new SqliteHouseholdStore(files.Database);
        store.Save(data);
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(store.Load()));
        var backup = Path.Combine(files.Directory, "backup.db");
        store.Backup(backup);
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(SqliteHouseholdStore.ReadBackup(backup)));
        var clone = JsonSerializer.Deserialize<HouseholdData>(JsonSerializer.Serialize(data))!;
        Assert.Equal(data.TransferMatches, clone.TransferMatches);
        ImportService.Export(Path.Combine(files.Directory, "export.csv"), data);
        var csv = File.ReadAllText(Path.Combine(files.Directory, "export.csv"));
        Assert.Contains("Gekoppelde transactie-ID", csv);
        Assert.Contains(data.TransferMatches.Single(m => m.IsConfirmed).Id.ToString(), csv);
        Assert.DoesNotContain(data.TransferMatches.Single(m => !m.IsConfirmed).Id.ToString(), csv);
        clone.TransferMatches.Clear();
        store.Save(clone);
        Assert.Empty(store.Load().TransferMatches);
        store.Restore(backup);
        Assert.Equal(data.TransferMatches, store.Load().TransferMatches);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PreviousSchemasAreReadWithoutMutationAndUpgradeWithASafetyBackup(int version)
    {
        using var files = new TestFiles();
        var data = CreateData();
        CreatePreviousDatabase(files.Database, data, version);
        var originalBytes = File.ReadAllBytes(files.Database);
        var imported = SqliteHouseholdStore.ReadBackup(files.Database);
        Assert.Empty(imported.TransferMatches);
        Assert.Equal(data.Transactions.Select(t => t.Amount), imported.Transactions.Select(t => t.Amount));
        Assert.Equal(originalBytes, File.ReadAllBytes(files.Database));
        Assert.Throws<InvalidDataException>(() => new SqliteHouseholdStore(files.Database, allowCreate: false, allowUpgrade: false));
        var upgraded = new SqliteHouseholdStore(files.Database, allowCreate: false, allowUpgrade: true);
        Assert.Equal(11L, ReadVersion(files.Database));
        Assert.Empty(upgraded.Load().TransferMatches);
        var safety = Assert.Single(System.IO.Directory.GetFiles(files.Directory, "*.voor-versie-11-*.db"));
        Assert.Equal((long)version, ReadVersion(safety));
        Assert.Equal(data.Transactions.Select(t => t.Amount), SqliteHouseholdStore.ReadBackup(safety).Transactions.Select(t => t.Amount));
        Confirm(imported);
        upgraded.Save(imported);
        Assert.Single(upgraded.Load().TransferMatches);
    }

    [Fact]
    public void UnknownSchemaIsNeverUpgradedOrReplaced()
    {
        using var files = new TestFiles();
        new SqliteHouseholdStore(files.Database).Save(CreateData());
        Execute(files.Database, "PRAGMA user_version=99");
        var original = File.ReadAllBytes(files.Database);
        Assert.Throws<InvalidDataException>(() => new SqliteHouseholdStore(files.Database, allowCreate: false, allowUpgrade: true));
        Assert.Equal(original, File.ReadAllBytes(files.Database));
        Assert.Empty(System.IO.Directory.GetFiles(files.Directory, "*.voor-versie-11-*.db"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void AutomaticSuggestionWindowIsInclusiveAndDoesNotRequireBankReferences(int days, bool suggested)
    {
        var data = CreateData();
        data.Transactions[1].Date = Date.AddDays(days);
        data.Transactions[0].Description = "Een betaling";
        var candidates = TransferMatching.Candidates(data, MonthEnd).ToList();
        Assert.Equal(suggested ? 1 : 0, candidates.Count);
        if (suggested) Assert.False(Assert.Single(candidates).HasAccountReference);
    }

    [Fact]
    public async Task RetiredWorkspaceHasNoMatchingActionsAndPreservesExistingHistory()
    {
        var data = CreateData();
        // Keep this rendering test independent of the host's current month/year.
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var account in data.Accounts) account.OpeningDate = today.AddDays(-10);
        data.Transactions[0].Date = today;
        data.Transactions[1].Date = today;
        var proposedHtml = await RenderAsync<Transfers>(data);
        Assert.Contains("Transfers koppelen is vervangen door categorie", System.Net.WebUtility.HtmlDecode(proposedHtml));
        Assert.Contains("categoriseren", proposedHtml);
        Assert.DoesNotContain("Transfer bevestigen", proposedHtml);
        Assert.DoesNotContain("Handmatig twee bestaande boekingen koppelen", proposedHtml);
        TransferMatching.Confirm(data, data.Transactions[0].Id, data.Transactions[1].Id, today);
        var confirmedHtml = await RenderAsync<Transfers>(data);
        Assert.Contains("Naar BE00 0000 0000 02", confirmedHtml);
        Assert.Contains("Ontvangst", confirmedHtml);
        Assert.DoesNotContain("Transfer bevestigen", confirmedHtml);
        var transactionsHtml = await RenderAsync<Transactions>(data);
        Assert.Contains("Gekoppelde transfer", transactionsHtml);
        Assert.Contains("Naar BE00 0000 0000 02", transactionsHtml);
        Assert.Contains("Ontvangst", transactionsHtml);
    }

    private static async Task<string> RenderAsync<T>(HouseholdData data) where T : IComponent
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");
        try
        {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IWebAdministration>(new ReadOnlyBackend(data));
        services.AddSingleton<WebState>();
        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<WebState>().LoadAsync();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<T>(ParameterView.Empty);
            return component.ToHtmlString();
        });
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    private sealed class ReadOnlyBackend(HouseholdData data) : IWebAdministration
    {
        public Task<AdministrationSnapshot> ReadAsync() => Task.FromResult(new AdministrationSnapshot("test-revision", data));
        public Task<AdministrationSnapshot> UpdateAsync(string revision, Action<HouseholdData> change) => throw new NotSupportedException();
        public Task<UploadResponse> UploadAsync(Stream source, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PreviewResponse> PreviewAsync(PreviewRequest request, string revision) => throw new NotSupportedException();
        public Task<PreviewRow> AdjustAsync(AdjustImportRequest request) => throw new NotSupportedException();
        public Task<CommitImportResponse> CommitAsync(CommitImportRequest request, string revision) => throw new NotSupportedException();
        public Task<string> RestoreAsync(Stream source, string revision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public void CoordinatorUpgradesUnderLeaseAndEnforcesStaleRevisionProtection()
    {
        using var files = new TestFiles();
        using (var initial = new AdministrationCoordinator(files.Directory, initialize: true))
        {
            var seed = CreateData();
            initial.Replace(initial.Read().Revision, seed);
        }
        CreatePreviousDatabase(files.Database, CreateData(), 3);
        using var coordinator = new AdministrationCoordinator(files.Directory, allowUpgrade: true);
        var before = coordinator.Read();
        var saved = coordinator.Update(before.Revision, Confirm);
        Assert.Single(saved.Data.TransferMatches);
        Assert.Throws<AdministrationConflictException>(() => coordinator.Update(before.Revision, d => TransferMatching.Remove(d, saved.Data.TransferMatches[0].Id)));
        Assert.Single(coordinator.Read().Data.TransferMatches);
        var replacement = JsonSerializer.Deserialize<HouseholdData>(JsonSerializer.Serialize(saved.Data))!;
        Assert.Single(coordinator.Replace(saved.Revision, replacement).Data.TransferMatches);
        Assert.Single(System.IO.Directory.GetFiles(files.Directory, "*.voor-versie-11-*.db"));
    }

    private static void CreatePreviousDatabase(string path, HouseholdData data, int version)
    {
        new SqliteHouseholdStore(path).Save(data);
        var sql = "ALTER TABLE [Category] DROP COLUMN [Purpose]; DROP TABLE [AccountReview]; DROP TABLE [TransactionAllocation]; DROP TABLE [ReimbursementClaim]; DROP TABLE [TransferMatch]; ALTER TABLE [Account] DROP COLUMN [MinimumCashBuffer];" +
            "ALTER TABLE [Transaction] DROP COLUMN [RecurringOccurrenceDate];" +
            "ALTER TABLE [RecurringPayment] DROP COLUMN [Frequency];" +
            "ALTER TABLE [RecurringPayment] DROP COLUMN [MatchTolerance];" +
            "ALTER TABLE [RecurringPayment] DROP COLUMN [MatchWindowDays];";
        if (version < 3) sql += "ALTER TABLE [Transaction] DROP COLUMN [UserDescription];";
        if (version == 1) sql += "ALTER TABLE [ImportProfile] DROP COLUMN [CounterAccountReferenceColumn];";
        Execute(path, PreviousSchema.RemoveSchema9Columns + sql + $"PRAGMA user_version={version};");
    }

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    private static long ReadVersion(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version";
        return (long)command.ExecuteScalar()!;
    }

    private sealed class TestFiles : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "opencode", "huishoudboek-tests", Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(Directory, "huishoudboek.db");
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true); }
    }
}
