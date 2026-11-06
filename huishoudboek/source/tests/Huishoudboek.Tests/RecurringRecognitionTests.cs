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
using Xunit;

namespace Huishoudboek.Tests;

public sealed class RecurringRecognitionTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateOnly Due = new(2026, 11, 3);

    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Owner" };
        var account = new Account { Name = "Current", OwnerIds = [owner.Id], OpeningDate = new(2026, 1, 1),
            OpeningBalance = 5000, MinimumCashBuffer = 100 };
        return new HouseholdData { Owners = [owner], Accounts = [account], RecurringPayments = [new()
        {
            Name = "Energy", AccountId = account.Id, Amount = -100, StartDate = new(2026, 1, 1), DayOfMonth = 3,
            MatchText = "ENERGY COMPANY", MatchWindowDays = 5
        }] };
    }

    private static Transaction Entry(HouseholdData data, decimal amount = -100, DateOnly? date = null, string description = "ENERGY COMPANY invoice")
    {
        var entry = new Transaction { AccountId = data.Accounts[0].Id, Amount = amount, Date = date ?? Due, Description = description };
        data.Transactions.Add(entry); return entry;
    }

    private static ImportBatch Coverage(HouseholdData data, DateOnly from, DateOnly through, Guid? accountId = null)
    {
        var batch = new ImportBatch { AccountId = accountId ?? data.Accounts[0].Id, FileName = "statement.csv",
            ImportedAt = Today.ToDateTime(TimeOnly.MinValue), CoverageFrom = from, CoverageThrough = through };
        data.ImportBatches.Add(batch); return batch;
    }

    [Fact]
    public void MissingImportsProduceNeutralPendingStatusesNotAnOverdueWallOrFictionalActuals()
    {
        var data = Data();
        var template = data.RecurringPayments[0];
        for (var i = 1; i < 30; i++) data.RecurringPayments.Add(template with { Id = Guid.NewGuid(), Name = $"Bill {i}" });
        var before = JsonSerializer.Serialize(data);
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(30, account.AwaitingBankDataCount);
        Assert.Equal(0, account.PaymentsToReviewCount);
        Assert.Equal(3000m, account.UncertainReserve);
        Assert.Equal(0m, account.BillsUntil(Today.AddDays(7)));
        Assert.Equal(3000m, account.BillsUntil(Today.AddDays(30))); // December's dated plans, not November's uncertain reserves.
        Assert.Equal(30, account.Events.Count(e => e.IsUncertain));
        Assert.DoesNotContain(account.Events, e => e.IsOverdue);
        Assert.True(account.HasUncertainty);
        Assert.True(account.Warnings.Count < 6);
        Assert.DoesNotContain(account.Warnings, w => w.Contains("Energy") || w.Contains("Bill"));
        Assert.Equal(5000m, account.CurrentBalance);
        Assert.All(data.RecurringPayments, p => Assert.Equal(RecurringPaymentStatus.AwaitingBankData,
            RecurringSchedule.Status(data, p, Due, Today)));
        var report = Finance.Report(data, 2026, 11, Today);
        Assert.Equal(0, report.Forecast.OverdueCount);
        Assert.Equal(0m, report.Outflow);
        Assert.Equal(before, JsonSerializer.Serialize(data));
    }

    [Fact]
    public void UploadTimeAndNewestTransactionDoNotProveCompleteCoverage()
    {
        var data = Data();
        var entry = Entry(data, date: Today, description: "Unrelated expense");
        var batch = new ImportBatch { AccountId = entry.AccountId, ImportedAt = Today.ToDateTime(TimeOnly.MinValue), RowCount = 1 };
        data.ImportBatches.Add(batch); entry.ImportBatchId = batch.Id;
        Assert.Equal(Today, FirstWeek.ImportedThrough(data, entry.AccountId, Today));
        Assert.False(BankImportCoverage.Covers(data, entry.AccountId, Due.AddDays(-5), Due.AddDays(5)));
        Assert.Equal(RecurringPaymentStatus.AwaitingBankData, RecurringSchedule.Status(data, data.RecurringPayments[0], Due, Today));
    }

    [Theory]
    [InlineData("adjacent", RecurringPaymentStatus.NotFound)]
    [InlineData("gap", RecurringPaymentStatus.AwaitingBankData)]
    [InlineData("other account", RecurringPaymentStatus.AwaitingBankData)]
    [InlineData("undone", RecurringPaymentStatus.AwaitingBankData)]
    public void CompleteRecognitionWindowsRespectGapsAccountBoundariesAndUndo(string scenario, RecurringPaymentStatus expected)
    {
        var data = Data();
        Coverage(data, new(2026, 10, 1), new(2026, 10, 31));
        var second = Coverage(data, scenario == "gap" ? new(2026, 11, 2) : new(2026, 11, 1), Today);
        if (scenario == "other account")
        {
            var other = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Other" }; data.Accounts.Add(other);
            second.AccountId = other.Id;
        }
        if (scenario == "undone") HouseholdEdits.UndoImport(data, second.Id);
        Assert.Equal(expected, RecurringSchedule.Status(data, data.RecurringPayments[0], Due, Today));
        var account = CashFlow.Project(data, Today).Accounts[0];
        Assert.Equal(expected == RecurringPaymentStatus.NotFound ? 1 : 0, account.PaymentsToReviewCount);
        Assert.Equal(expected == RecurringPaymentStatus.NotFound, Assert.Single(account.Events, e => e.IsUncertain).IsOverdue);
        Validation.Validate(data);
    }

    [Fact]
    public void FullPaymentWindowMustHaveElapsedAndQuietStatementDaysAreStillCovered()
    {
        var data = Data(); Coverage(data, new(2026, 10, 1), Today);
        var plan = data.RecurringPayments[0];
        Assert.Equal(RecurringPaymentStatus.AwaitingBankData, RecurringSchedule.Status(data, plan, Due, Due.AddDays(4)));
        Assert.Equal(RecurringPaymentStatus.NotFound, RecurringSchedule.Status(data, plan, Due, Due.AddDays(5)));
        Assert.Equal(RecurringPaymentStatus.Expected, RecurringSchedule.Status(data, plan, Due.AddMonths(1), Today));
        Assert.Equal(Today, FirstWeek.ImportedThrough(data, plan.AccountId, Today));
        Assert.Equal(5000m, Finance.Balance(data, data.Accounts[0], Today));
    }

    [Fact]
    public void ConfirmedQuietPeriodsAreFreshEvenWhenTheOriginalUploadIsOld()
    {
        var data = Data(); var batch = Coverage(data, new(2026, 10, 1), Today);
        batch.ImportedAt = Today.AddDays(-30).ToDateTime(TimeOnly.MinValue);
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.False(account.StaleImports);
        Assert.Equal(Today, account.StatementThrough);
        Assert.True(FirstWeek.Checklist(data, Today).Single(s => s.Key == "SetupImport").Complete);
    }

    [Theory]
    [InlineData(-147.32)]
    [InlineData(-60)]
    public void VariableAmountsAndHarmlessBankFormattingAreRecognizedWithoutChangingFutureEstimates(double amount)
    {
        var data = Data(); var plan = data.RecurringPayments[0]; plan.AmountMode = PaymentAmountMode.Variable;
        var actual = Entry(data, (decimal)amount, description: "Energy---Company / reference 12345");
        var bank = actual with { };
        Finance.ApplySuggestions(data, actual);
        Assert.Equal(plan.Id, actual.RecurringPaymentId);
        Assert.Equal(Due, actual.RecurringOccurrenceDate);
        Assert.Equal(bank with { RecurringPaymentId = plan.Id, RecurringOccurrenceDate = Due }, actual);
        Assert.Equal(-100m, plan.Amount);
        Assert.Equal(RecurringPaymentStatus.Recognized, RecurringSchedule.Status(data, plan, Due, Today));
        var account = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(0m, account.UncertainReserve);
        Assert.Equal(-100m, Assert.Single(account.Events).Amount);
        Assert.Equal(5000m + (decimal)amount, account.CurrentBalance);
        Validation.Validate(data);
    }

    [Fact]
    public void ChangedFixedAmountsAndNearbyDatesAreReviewSuggestionsNotSilentLinks()
    {
        var data = Data(); var plan = data.RecurringPayments[0];
        var actual = Entry(data, -115);
        Finance.ApplySuggestions(data, actual);
        Assert.Null(actual.RecurringPaymentId);
        Assert.True(Assert.Single(RecurringSchedule.Candidates(data, Today)).RequiresReview);
        Assert.Empty(RecurringSchedule.BatchForReview(data, Today));
        Assert.Equal(RecurringPaymentStatus.NeedsReview, RecurringSchedule.Status(data, plan, Due, Today));
        actual.Amount = plan.Amount; actual.Date = Due.AddDays(7);
        Finance.ApplySuggestions(data, actual);
        Assert.Null(actual.RecurringPaymentId);
        Assert.True(Assert.Single(RecurringSchedule.Candidates(data, Today)).RequiresReview);
        Assert.Empty(RecurringSchedule.BatchForReview(data, Today));
        RecurringSchedule.LinkBatch(data, [(actual.Id, plan.Id, Due)]);
        Assert.Equal(plan.Id, data.Transactions[0].RecurringPaymentId);
        Assert.Equal(Due.AddDays(7), data.Transactions[0].Date);
    }

    [Fact]
    public void ChangingReferenceDigitsAreNeverStrippedToInventAnIdentity()
    {
        var data = Data(); data.RecurringPayments[0].MatchText = "ENERGY COMPANY reference 12345";
        var actual = Entry(data, description: "Energy Company reference 67890");
        Finance.ApplySuggestions(data, actual);
        Assert.Null(actual.RecurringPaymentId); Assert.Empty(RecurringSchedule.Candidates(data, Today));
    }

    [Fact]
    public void AConfirmedExampleTeachesItsExactDescriptionEvenWithoutConfiguredText()
    {
        var data = Data(); var plan = data.RecurringPayments[0]; plan.MatchText = "";
        var example = Entry(data, date: Due.AddMonths(-1), description: "ENERGY COMPANY standing order");
        RecurringSchedule.Link(data, example.Id, plan.Id, Due.AddMonths(-1));
        var actual = Entry(data, description: "Energy Company standing-order");
        Finance.ApplySuggestions(data, actual);
        Assert.Equal(plan.Id, actual.RecurringPaymentId);
        Assert.Equal(Due, actual.RecurringOccurrenceDate);
        Validation.Validate(data);
    }

    [Theory]
    [InlineData("direction")]
    [InlineData("account")]
    [InlineData("purpose")]
    [InlineData("conflicting plan")]
    [InlineData("competing transaction")]
    public void VariableRecognitionStillProtectsAccountDirectionPurposeAndAmbiguity(string scenario)
    {
        var data = Data(); var plan = data.RecurringPayments[0]; plan.AmountMode = PaymentAmountMode.Variable;
        var actual = Entry(data, -190);
        switch (scenario)
        {
            case "direction": actual.Amount = 190; break;
            case "account":
                var other = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Other" }; data.Accounts.Add(other);
                actual.AccountId = other.Id; break;
            case "purpose":
                var category = new Category { Name = "Saving", Purpose = CategoryPurpose.Savings }; data.Categories.Add(category);
                actual.CategoryId = category.Id; break;
            case "conflicting plan": data.RecurringPayments.Add(plan with { Id = Guid.NewGuid(), Name = "Other plan" }); break;
            case "competing transaction": Entry(data, -200); break;
        }
        Finance.ApplySuggestions(data, actual);
        Assert.Null(actual.RecurringPaymentId);
        Validation.Validate(data);
    }

    [Fact]
    public void BulkLinksRejectConflictsAndValidateAllSelectionsBeforeChangingAnyEntry()
    {
        var data = Data(); var plan = data.RecurringPayments[0];
        var first = Entry(data); var second = Entry(data, -110);
        var before = JsonSerializer.Serialize(data);
        Assert.Throws<InvalidOperationException>(() => RecurringSchedule.LinkBatch(data, [(first.Id, plan.Id, Due), (second.Id, plan.Id, Due)]));
        Assert.Equal(before, JsonSerializer.Serialize(data));
        Assert.Throws<InvalidOperationException>(() => RecurringSchedule.LinkBatch(data, [(first.Id, plan.Id, Due), (first.Id, plan.Id, Due.AddMonths(-1))]));
        Assert.Equal(before, JsonSerializer.Serialize(data));
        Assert.Throws<InvalidOperationException>(() => RecurringSchedule.LinkBatch(data, [(first.Id, plan.Id, Due), (second.Id, plan.Id, Due.AddDays(1))]));
        Assert.Equal(before, JsonSerializer.Serialize(data));
        RecurringSchedule.LinkBatch(data, [(first.Id, plan.Id, Due)]);
        Assert.Equal(plan.Id, data.Transactions[0].RecurringPaymentId); Assert.Null(data.Transactions[1].RecurringPaymentId);
    }

    [Fact]
    public async Task TransactionFirstDraftPrefillsSourceAndHistoricalPreviewCanLinkOnlySelectedEntries()
    {
        var data = Data(); data.RecurringPayments.Clear();
        var first = Entry(data); Entry(data, date: Due.AddMonths(-1));
        var state = new WebState(new Backend(data)); await state.LoadAsync();
        var draft = EditorDraft.RecurringFromTransaction(state, first);
        Assert.Equal(first.Description, draft.Text("text"));
        Assert.Equal(first.AccountId.ToString(), draft.Text("account"));
        Assert.Equal(Due.Day.ToString(CultureInfo.InvariantCulture), draft.Text("day"));
        draft.Fields.Single(f => f.Key == "start").Value = "2026-01-01";
        var preview = draft.PrepareSave(state.Data, Today);
        Assert.Equal(2, preview.MatchCount);
        Assert.Equal(2, preview.Matches.Count);
        Assert.Empty(state.Data.RecurringPayments);
        preview.SaveSelected!(state.Data, new HashSet<Guid> { first.Id });
        var savedPlan = Assert.Single(state.Data.RecurringPayments);
        Assert.Equal(savedPlan.Id, state.Data.Transactions.Single(t => t.Id == first.Id).RecurringPaymentId);
        Assert.Null(state.Data.Transactions.Single(t => t.Id != first.Id).RecurringPaymentId);
        Assert.Equal(4800m, Finance.Balance(state.Data, state.Data.Accounts[0], Today));
        Validation.Validate(state.Data);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("date")]
    [InlineData("amount")]
    [InlineData("description")]
    public void ChangingImportedBankDataInvalidatesItsCompletePeriod(string change)
    {
        var data = Data(); var actual = Entry(data); var batch = Coverage(data, new(2026, 10, 1), Today);
        actual.ImportBatchId = batch.Id; batch.RowCount = 1;
        switch (change)
        {
            case "delete": HouseholdEdits.DeleteTransaction(data, actual.Id); break;
            case "date": HouseholdEdits.SaveTransaction(data, actual with { Date = Due.AddDays(1) }); break;
            case "amount": HouseholdEdits.SaveTransaction(data, actual with { Amount = -101 }); break;
            case "description": HouseholdEdits.SaveTransaction(data, actual with { Description = "Edited bank text" }); break;
        }
        Assert.Null(batch.CoverageFrom); Assert.Null(batch.CoverageThrough);
        Assert.False(BankImportCoverage.Covers(data, actual.AccountId, Due.AddDays(-5), Due.AddDays(5)));
        Validation.Validate(data);
    }

    [Fact]
    public void LinkingAndCategorizingDoNotInvalidateStatementCoverage()
    {
        var data = Data(); var actual = Entry(data); var batch = Coverage(data, new(2026, 10, 1), Today);
        batch.RowCount = 1; actual.ImportBatchId = batch.Id;
        var category = new Category { Name = "Utilities" }; data.Categories.Add(category);
        HouseholdEdits.SaveTransaction(data, actual with { CategoryId = category.Id });
        RecurringSchedule.Link(data, actual.Id, data.RecurringPayments[0].Id, Due);
        Assert.True(BankImportCoverage.Covers(data, actual.AccountId, Due.AddDays(-5), Due.AddDays(5)));
        RecurringSchedule.Unlink(data, actual.Id);
        Assert.True(BankImportCoverage.Covers(data, actual.AccountId, Due.AddDays(-5), Due.AddDays(5)));
    }

    [Fact]
    public void Schema8IsReadWithoutMutationAndUpgradesWithUnknownCoverageAndFixedAmounts()
    {
        using var files = new Files(); var path = files.Path("legacy.db"); var data = Data(); Entry(data, description: "Other payment");
        var batch = new ImportBatch { AccountId = data.Accounts[0].Id, FileName = "old.csv", RowCount = 1 };
        data.ImportBatches.Add(batch); data.Transactions[0].ImportBatchId = batch.Id;
        var store = new SqliteHouseholdStore(path); store.Save(data);
        Sql(path, PreviousSchema.RemoveSchema9Columns + "PRAGMA user_version=8;");
        var bytes = File.ReadAllBytes(path);
        var old = SqliteHouseholdStore.ReadBackup(path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(PaymentAmountMode.Fixed, old.RecurringPayments[0].AmountMode);
        Assert.Null(Assert.Single(old.ImportBatches).CoverageThrough);
        Assert.Equal(RecurringPaymentStatus.AwaitingBankData, RecurringSchedule.Status(old, old.RecurringPayments[0], Due, Today));
        var migrated = new SqliteHouseholdStore(path, allowCreate: false).Load();
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(migrated));
        var safety = Assert.Single(Directory.GetFiles(files.Directory, "*.voor-versie-10-*.db"));
        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(SqliteHouseholdStore.ReadBackup(safety)));
        migrated.RecurringPayments[0].AmountMode = PaymentAmountMode.Variable;
        Coverage(migrated, new(2026, 10, 1), Today);
        store.Save(migrated); store.Backup(files.Path("backup.db"));
        store.Save(HouseholdData.CreateEmpty()); store.Restore(files.Path("backup.db"));
        Assert.Equal(JsonSerializer.Serialize(migrated), JsonSerializer.Serialize(store.Load()));
    }

    [Fact]
    public async Task ExistingImportPeriodsCanBeConfirmedWithoutReimportingOrChangingBankEntries()
    {
        var now = DateOnly.FromDateTime(DateTime.Today); var due = now.AddDays(-10);
        var data = Data(); data.Accounts[0].OpeningDate = now.AddMonths(-2);
        data.RecurringPayments[0].StartDate = due; data.RecurringPayments[0].Frequency = PaymentFrequency.Once;
        var actual = Entry(data, date: due, description: "Other payment");
        var batch = new ImportBatch { AccountId = actual.AccountId, FileName = "old.csv", RowCount = 1 };
        data.ImportBatches.Add(batch); actual.ImportBatchId = batch.Id;
        var state = new WebState(new Backend(data)); await state.LoadAsync();
        var bank = JsonSerializer.Serialize(data.Transactions); var balance = Finance.Balance(data, data.Accounts[0], now);
        var draft = EditorDraft.ImportCoverage(state, batch);
        draft.Fields.Single(f => f.Key == "from").Value = due.AddDays(-5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        draft.Fields.Single(f => f.Key == "through").Value = due.AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        draft.Apply(data);
        Assert.Equal(bank, JsonSerializer.Serialize(data.Transactions));
        Assert.Equal(balance, Finance.Balance(data, data.Accounts[0], now));
        Assert.Equal(RecurringPaymentStatus.NotFound, RecurringSchedule.Status(data, data.RecurringPayments[0], due, now));
        HouseholdEdits.DeleteTransaction(data, actual.Id);
        Assert.Null(batch.CoverageThrough);
        Assert.Throws<InvalidOperationException>(() => draft.Apply(data));
        Assert.Null(batch.CoverageThrough);
    }

    [Fact]
    public void NewRecognitionAndCoverageValuesAreValidated()
    {
        var data = Data(); data.RecurringPayments[0].AmountMode = (PaymentAmountMode)99;
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
        data.RecurringPayments[0].AmountMode = PaymentAmountMode.Fixed;
        var batch = Coverage(data, Today, Today.AddDays(-1));
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
        batch.CoverageThrough = null;
        Assert.Throws<InvalidOperationException>(() => Validation.Validate(data));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ImportCoverageIsExplicitSupportsQuietPeriodsAndSurvivesPersistenceUntilUndo(bool confirm, bool empty)
    {
        using var files = new Files();
        using var coordinator = new AdministrationCoordinator(files.Path("administration"), initialize: true);
        coordinator.Replace(coordinator.Read().Revision, Data());
        using var workspace = new ImportWorkspace(files.Directory);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Datum;Omschrijving;Bedrag\n" + (empty ? "" : "2026-11-03;Other payment;-10,00")));
        var upload = await workspace.UploadAsync(stream, "statement.csv", CancellationToken.None);
        var snapshot = coordinator.Read();
        var preview = workspace.Preview(new(upload.UploadId, snapshot.Data.Accounts[0].Id, new(2026, 11, 1), new(2026, 11, 30)), snapshot);
        var result = workspace.Commit(new(preview.PreviewId, preview.Rows.Select(r => new ImportSelection(r.RowNumber, r.CategoryId, null)).ToArray(), confirm),
            preview.Revision, coordinator, Today);
        var saved = coordinator.Read(); var batch = Assert.Single(saved.Data.ImportBatches);
        Assert.Equal(confirm ? new DateOnly(2026, 11, 1) : (DateOnly?)null, batch.CoverageFrom);
        Assert.Equal(confirm ? Today : (DateOnly?)null, batch.CoverageThrough);
        Assert.Equal(empty ? 5000m : 4990m, Finance.Balance(saved.Data, saved.Data.Accounts[0], Today));
        Assert.Equal(confirm, BankImportCoverage.Covers(saved.Data, batch.AccountId, new(2026, 11, 1), Today));
        var after = coordinator.Update(saved.Revision, d => HouseholdEdits.UndoImport(d, result.BatchId));
        Assert.False(BankImportCoverage.Covers(after.Data, batch.AccountId, new(2026, 11, 1), Today));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteOrInvalidImportCannotClaimCompleteCoverageAndDoesNotSaveAnything(bool invalid)
    {
        using var files = new Files();
        using var coordinator = new AdministrationCoordinator(files.Path("administration"), initialize: true);
        coordinator.Replace(coordinator.Read().Revision, Data());
        using var workspace = new ImportWorkspace(files.Directory);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Datum;Omschrijving;Bedrag\n2026-11-03;First;-10,00\n" +
            (invalid ? "2026-11-04;Second;not a number" : "2026-11-04;Second;-20,00")));
        var upload = await workspace.UploadAsync(stream, "statement.csv", CancellationToken.None); var snapshot = coordinator.Read();
        var preview = workspace.Preview(new(upload.UploadId, snapshot.Data.Accounts[0].Id, new(2026, 11, 1), Today), snapshot);
        var first = preview.Rows[0];
        Assert.Throws<InvalidOperationException>(() => workspace.Commit(new(preview.PreviewId, [new(first.RowNumber, first.CategoryId, null)], true), preview.Revision, coordinator, Today));
        Assert.Equal(snapshot.Revision, coordinator.Read().Revision);
        Assert.Empty(coordinator.Read().Data.Transactions); Assert.Empty(coordinator.Read().Data.ImportBatches);
        workspace.Commit(new(preview.PreviewId, [new(first.RowNumber, first.CategoryId, null)]), preview.Revision, coordinator, Today);
        Assert.Null(Assert.Single(coordinator.Read().Data.ImportBatches).CoverageThrough);
    }

    [Theory]
    [InlineData("nl-NL")]
    [InlineData("en-GB")]
    public async Task MonthlyChecklistAndOutlookRenderNeutralImportStatusesAndKeepWarningsCollapsed(string culture)
    {
        var previous = CultureInfo.CurrentCulture; var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var data = Data(); var now = DateOnly.FromDateTime(DateTime.Today);
            data.Accounts[0].OpeningDate = now.AddMonths(-2);
            var plan = data.RecurringPayments[0]; plan.StartDate = now.AddMonths(-1); plan.DayOfMonth = now.Day;
            for (var i = 0; i < 20; i++) data.RecurringPayments.Add(plan with { Id = Guid.NewGuid(), Name = $"Unimported bill {i}" });
            var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization();
            services.AddSingleton<IWebAdministration>(new Backend(data)); services.AddSingleton<WebState>();
            using var provider = services.BuildServiceProvider(); await provider.GetRequiredService<WebState>().LoadAsync();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var recurring = await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<Recurring>()).ToHtmlString()));
            Assert.Contains("id=\"payment-status\"", recurring);
            Assert.Contains(UiCulture.Text("RecurringStatusAwaitingBankData"), recurring);
            Assert.Contains(UiCulture.Text("RecurringAwaitingSummary", 21), recurring);
            Assert.DoesNotContain("<span class=\"badge\">" + UiCulture.Text("RecurringStatusNotFound"), recurring);
            var outlook = await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<SafeSpending>()).ToHtmlString()));
            Assert.Contains(UiCulture.Text("RecurringAwaitingSummary", 21), outlook);
            Assert.Contains("<details class=\"notice warning\"><summary>", outlook);
            Assert.DoesNotContain("<details class=\"notice warning\" open", outlook);
            Assert.Contains(UiCulture.Text("PaymentTimingUnknown"), outlook);
            Assert.DoesNotContain(UiCulture.Text("OverduePaymentWarning", plan.Name, now.ToString("dd-MM-yyyy")), outlook);
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    private static void Sql(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    private sealed class Files : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opencode", "huishoudboek-tests", Guid.NewGuid().ToString("N"));
        public string Path(string name) => System.IO.Path.Combine(Directory, name);
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true); }
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
}
