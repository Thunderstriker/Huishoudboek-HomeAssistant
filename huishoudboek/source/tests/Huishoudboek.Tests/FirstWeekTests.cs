using System.Globalization;
using System.Net;
using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
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

public sealed class FirstWeekTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Test owner" };
        return new() { Owners = [owner], Accounts = [new() { Name = "Test account", OwnerIds = [owner.Id], OpeningDate = Today.AddMonths(-1), OpeningBalance = 100 }],
            Categories = [new() { Name = "Groceries", MonthlyBudget = 200 }] };
    }
    private static Transaction Entry(HouseholdData data, decimal amount = -20, DateOnly? date = null)
    {
        var entry = new Transaction { AccountId = data.Accounts[0].Id, Date = date ?? Today, Amount = amount, Description = "Test purchase", CategoryId = data.Categories[0].Id };
        data.Transactions.Add(entry); return entry;
    }

    [Fact]
    public void EmptyChecklistCannotReportCompletionAndZeroOpeningBalanceRequiresConfirmation()
    {
        Assert.All(FirstWeek.Checklist(HouseholdData.CreateEmpty(), Today), s => Assert.False(s.Complete));
        var data = Data(); var account = data.Accounts[0]; account.OpeningBalance = 0;
        Assert.False(FirstWeek.OpeningConfirmed(data, account));
        FirstWeek.ConfirmOpening(data, account.Id);
        Assert.True(FirstWeek.OpeningConfirmed(data, account));
        account.OpeningDate = account.OpeningDate.AddDays(-1);
        Assert.False(FirstWeek.OpeningConfirmed(data, account));
        FirstWeek.ConfirmOpening(data, account.Id); account.OpeningBalance = 10;
        Assert.False(FirstWeek.OpeningConfirmed(data, account));
    }

    [Fact]
    public void ChecklistFollowsAllAccountsImportsBalancesCategoriesAndPlans()
    {
        var data = Data(); var account = data.Accounts[0]; var entry = Entry(data);
        Assert.True(FirstWeek.Checklist(data, Today)[0].Complete);
        FirstWeek.ConfirmOpening(data, account.Id);
        var batch = new ImportBatch { AccountId = account.Id, RowCount = 1 }; data.ImportBatches.Add(batch); entry.ImportBatchId = batch.Id;
        FirstWeek.CheckBalance(data, account.Id, Today, 80, Today);
        data.RecurringPayments.Add(new() { Name = "Rent", AccountId = account.Id, Amount = -50, StartDate = Today, DayOfMonth = Today.Day });
        Assert.All(FirstWeek.Checklist(data, Today), s => Assert.True(s.Complete));
        batch.Undone = true; Assert.False(FirstWeek.Checklist(data, Today)[2].Complete); batch.Undone = false;
        entry.CategoryId = null; Assert.False(FirstWeek.Checklist(data, Today)[4].Complete); entry.CategoryId = data.Categories[0].Id;
        var second = account with { Id = Guid.NewGuid(), Name = "Second account" }; data.Accounts.Add(second);
        var steps = FirstWeek.Checklist(data, Today);
        Assert.False(steps[1].Complete); Assert.False(steps[2].Complete); Assert.False(steps[3].Complete);
    }

    [Fact]
    public void BalanceChecksNeverCorrectCashAndRejectMismatchesOrFutureDates()
    {
        var data = Data(); var account = data.Accounts[0]; Entry(data);
        var before = JsonSerializer.Serialize(data);
        Assert.Throws<InvalidOperationException>(() => FirstWeek.CheckBalance(data, account.Id, Today, 81, Today));
        Assert.Throws<InvalidOperationException>(() => FirstWeek.CheckBalance(data, account.Id, Today.AddDays(1), 80, Today));
        Assert.Throws<InvalidOperationException>(() => FirstWeek.CheckBalance(data, account.Id, account.OpeningDate.AddDays(-1), 0, Today));
        Assert.Equal(before, JsonSerializer.Serialize(data));
        FirstWeek.CheckBalance(data, account.Id, Today, 80, Today);
        Assert.True(FirstWeek.BalanceChecked(data, account, Today)); Assert.Equal(80m, Finance.Balance(data, account, Today));
        Validation.Validate(data);
    }

    [Fact]
    public void CashChangesReopenChecksEvenWhenTheNetBalanceDoesNotChange()
    {
        var data = Data(); var account = data.Accounts[0]; var entry = Entry(data);
        FirstWeek.CheckBalance(data, account.Id, Today, 80, Today);
        entry.UserDescription = "Edited label"; entry.CategoryId = null;
        Assert.True(FirstWeek.BalanceChecked(data, account, Today));
        Entry(data, -10); Entry(data, 10);
        Assert.Equal(80m, Finance.Balance(data, account, Today));
        Assert.False(FirstWeek.BalanceChecked(data, account, Today));
        FirstWeek.CheckBalance(data, account.Id, Today, 80, Today);
        account.OpeningBalance += 10; entry.Amount -= 10;
        Assert.False(FirstWeek.BalanceChecked(data, account, Today));
    }

    [Fact]
    public void FutureTransactionsReopenChecksOnlyWhenTheyBecomeActual()
    {
        var data = Data(); var account = data.Accounts[0]; Entry(data); Entry(data, -10, Today.AddDays(1));
        FirstWeek.CheckBalance(data, account.Id, Today, 80, Today);
        Assert.True(FirstWeek.BalanceChecked(data, account, Today));
        Assert.False(FirstWeek.BalanceChecked(data, account, Today.AddDays(1)));
    }

    [Fact]
    public void ManualTransfersReopenBothAccountChecksButReportingLinksDoNot()
    {
        var data = Data(); var first = data.Accounts[0]; var second = first with { Id = Guid.NewGuid(), Name = "Savings" }; data.Accounts.Add(second);
        FirstWeek.CheckBalance(data, first.Id, Today, 100, Today); FirstWeek.CheckBalance(data, second.Id, Today, 100, Today);
        var debit = Entry(data, -10); var credit = debit with { Id = Guid.NewGuid(), AccountId = second.Id, Amount = 10 }; data.Transactions.Add(credit);
        Assert.False(FirstWeek.BalanceChecked(data, first, Today)); Assert.False(FirstWeek.BalanceChecked(data, second, Today));
        FirstWeek.CheckBalance(data, first.Id, Today, 90, Today); FirstWeek.CheckBalance(data, second.Id, Today, 110, Today);
        TransferMatching.Confirm(data, debit.Id, credit.Id, Today);
        Assert.True(FirstWeek.BalanceChecked(data, first, Today)); Assert.True(FirstWeek.BalanceChecked(data, second, Today));
        data.Transactions.Add(new() { AccountId = first.Id, CounterAccountId = second.Id, Kind = TransactionKind.Transfer, Date = Today, Amount = -5, Description = "Manual transfer" });
        Assert.False(FirstWeek.BalanceChecked(data, first, Today)); Assert.False(FirstWeek.BalanceChecked(data, second, Today));
    }

    [Fact]
    public void UncategorizedReviewUsesAllocationCategoriesAndExcludesTransfersAndFutureEntries()
    {
        var data = Data(); var entry = Entry(data); entry.CategoryId = null;
        Assert.Equal(1, FirstWeek.UncategorizedCount(data, Today));
        TransactionAccounting.SaveAllocations(data, entry.Id, [new() { TransactionId = entry.Id, Amount = -20, CategoryId = data.Categories[0].Id }]);
        Assert.Equal(0, FirstWeek.UncategorizedCount(data, Today));
        Entry(data, -10, Today.AddDays(1)).CategoryId = null;
        Assert.Equal(0, FirstWeek.UncategorizedCount(data, Today));
        var other = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Other" }; data.Accounts.Add(other);
        var debit = Entry(data, -50); debit.CategoryId = null;
        var credit = debit with { Id = Guid.NewGuid(), AccountId = other.Id, Amount = 50 }; data.Transactions.Add(credit);
        TransferMatching.Confirm(data, debit.Id, credit.Id, Today);
        Assert.Equal(0, FirstWeek.UncategorizedCount(data, Today));
        Assert.Equal(0, FirstWeek.UncategorizedCount(data, Today, new HashSet<Guid>()));
    }

    [Fact]
    public void WeeklyCoverageUsesActualBankDatesAndIgnoresUndoneBatchesAndManualEntries()
    {
        var data = Data(); var account = data.Accounts[0]; Entry(data);
        Assert.Null(FirstWeek.ImportedThrough(data, account.Id, Today));
        var batch = new ImportBatch { AccountId = account.Id, ImportedAt = Today.ToDateTime(TimeOnly.MinValue), RowCount = 2 }; data.ImportBatches.Add(batch);
        Entry(data, -10, Today.AddDays(-20)).ImportBatchId = batch.Id;
        Entry(data, -10, Today.AddDays(1)).ImportBatchId = batch.Id;
        Assert.Equal(Today.AddDays(-20), FirstWeek.ImportedThrough(data, account.Id, Today));
        batch.Undone = true; Assert.Null(FirstWeek.ImportedThrough(data, account.Id, Today));
    }

    [Fact]
    public void UpcomingBillsCrossMonthBoundaryExcludeIncomeAndLinkedOccurrencesAndHonorScope()
    {
        var data = Data(); var monthEnd = new DateOnly(2026, 11, 28); var account = data.Accounts[0];
        var rent = new RecurringPayment { Name = "Rent", AccountId = account.Id, Amount = -50, StartDate = new(2026, 11, 1), DayOfMonth = 2 };
        data.RecurringPayments.Add(rent); data.RecurringPayments.Add(rent with { Id = Guid.NewGuid(), Name = "Salary", Amount = 200 });
        var once = rent with { Id = Guid.NewGuid(), Name = "Annual bill", StartDate = monthEnd.AddDays(7), Frequency = PaymentFrequency.Once }; data.RecurringPayments.Add(once);
        Assert.Equal(new[] { new DateOnly(2026, 12, 2), monthEnd.AddDays(7) }, FirstWeek.Upcoming(data, monthEnd).Select(b => b.DueDate));
        var paid = Entry(data, -50, monthEnd); RecurringSchedule.Link(data, paid.Id, rent.Id, new(2026, 12, 2));
        Assert.Equal(once.Id, Assert.Single(FirstWeek.Upcoming(data, monthEnd)).Payment.Id);
        Assert.Empty(FirstWeek.Upcoming(data, monthEnd, new HashSet<Guid>()));
    }

    [Fact]
    public void ReviewsRoundTripThroughDatabaseBackupAndRestoreAndSchema6UpgradesSafely()
    {
        using var files = new TestFiles(); var data = Data(); var account = data.Accounts[0]; Entry(data);
        FirstWeek.ConfirmOpening(data, account.Id); FirstWeek.CheckBalance(data, account.Id, Today, 80, Today);
        var store = new SqliteHouseholdStore(files.Path("current.db")); store.Save(data); var backup = files.Path("backup.db"); store.Backup(backup);
        store.Save(Data()); store.Restore(backup);
        Assert.Equal(data.AccountReviews, store.Load().AccountReviews);
        Assert.True(FirstWeek.BalanceChecked(store.Load(), account, Today));
        var legacy = files.Path("schema6.db"); new SqliteHouseholdStore(legacy).Save(data);
        using (var connection = new SqliteConnection($"Data Source={legacy};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE [AccountReview]; PRAGMA user_version=6"; command.ExecuteNonQuery();
        }
        var bytes = File.ReadAllBytes(legacy); Assert.Empty(SqliteHouseholdStore.ReadBackup(legacy).AccountReviews); Assert.Equal(bytes, File.ReadAllBytes(legacy));
        var migrated = new SqliteHouseholdStore(legacy, allowCreate: false);
        Assert.Empty(migrated.Load().AccountReviews); Assert.Equal(data.Transactions, migrated.Load().Transactions);
        Assert.Equal(data.Categories, migrated.Load().Categories);
        Assert.Single(Directory.GetFiles(files.Directory, "*.voor-versie-7-*.db"));
    }

    [Theory]
    [InlineData("nl-NL", false, "12,34", -12.34)]
    [InlineData("en-GB", false, "12.34", -12.34)]
    [InlineData("nl-NL", true, "12,34", 12.34)]
    [InlineData("en-GB", true, "12.34", 12.34)]
    public async Task SimpleEntryUsesPositiveAmountsAndOnlyEssentialFields(string culture, bool income, string input, double expected)
    {
        using var language = new CultureScope(culture); var data = Data(); var state = new WebState(new Backend(data)); await state.LoadAsync();
        var draft = EditorDraft.SimpleTransaction(state, income);
        Assert.Equal(new[] { "date", "account", "description", "amount", "category" }, draft.Fields.Select(f => f.Key));
        draft.Fields.Single(f => f.Key == "description").Value = "Coffee"; draft.Fields.Single(f => f.Key == "amount").Value = input;
        draft.Apply(data); Assert.Equal((decimal)expected, Assert.Single(data.Transactions).Amount);
        Assert.Equal(TransactionKind.Normal, data.Transactions[0].Kind); Assert.Null(data.Transactions[0].CounterAccountId); Assert.Equal(0m, data.Transactions[0].PrincipalComponent);
        draft.Fields.Single(f => f.Key == "amount").Value = "-1";
        Assert.Throws<InvalidOperationException>(() => draft.Apply(data));
        Validation.Validate(data);
    }

    [Fact]
    public async Task AdvancedEditorShowsOnlyRelevantTypeAndRecurringFieldsWithoutDiscardingDraftValues()
    {
        var data = Data(); var state = new WebState(new Backend(data)); await state.LoadAsync(); var draft = EditorDraft.Transaction(state);
        string[] Visible() => draft.Fields.Where(draft.IsFieldVisible).Select(f => f.Key).ToArray();
        Assert.DoesNotContain("counter", Visible()); Assert.DoesNotContain("principal", Visible()); Assert.DoesNotContain("occurrence", Visible());
        Assert.DoesNotContain(draft.Options(draft.Fields.Single(f => f.Key == "category")), o => o.Value == "default");
        draft.Fields.Single(f => f.Key == "recurring").Value = Guid.NewGuid().ToString(); Assert.Contains("occurrence", Visible());
        draft.Fields.Single(f => f.Key == "kind").Value = "Transfer";
        Assert.Contains("counter", Visible()); Assert.DoesNotContain("principal", Visible()); Assert.DoesNotContain("recurring", Visible()); Assert.DoesNotContain("occurrence", Visible());
        draft.Fields.Single(f => f.Key == "kind").Value = "Mortgage";
        Assert.Contains("principal", Visible()); Assert.DoesNotContain("counter", Visible()); Assert.Contains("recurring", Visible());
    }

    [Theory]
    [InlineData("nl-NL", "Uw eerste week", "Details en acties uitklappen", "Wekelijkse controle")]
    [InlineData("en-GB", "Your first week", "Expand details and actions", "Weekly review")]
    public async Task ChecklistMobileListAndWeeklyReviewRenderInBothLanguages(string culture, string checklistTitle, string detailsLabel, string weeklyTitle)
    {
        using var language = new CultureScope(culture); var data = Data(); var today = DateOnly.FromDateTime(DateTime.Today);
        data.Accounts[0].OpeningDate = today.AddMonths(-1); var entry = Entry(data, date: today); entry.Description = "<script>bank text</script>"; entry.UserDescription = "Coffee";
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization();
        services.AddSingleton<IWebAdministration>(new Backend(data)); services.AddSingleton<WebState>(); services.AddSingleton<IJSRuntime>(new StaticJs());
        using var provider = services.BuildServiceProvider(); await provider.GetRequiredService<WebState>().LoadAsync();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        async Task<string> Render<T>() where T : IComponent => await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<T>()).ToHtmlString());
        var checklist = WebUtility.HtmlDecode(await Render<FirstUseChecklist>()); Assert.Contains(checklistTitle, checklist); Assert.Contains("rekeningen#balance-checks", checklist);
        var weekly = WebUtility.HtmlDecode(await Render<WeeklyReview>()); Assert.Contains(weeklyTitle, weekly); Assert.Contains("importeren", weekly); Assert.Contains("overboekingen", weekly);
        var html = await Render<Transactions>();
        Assert.Contains("mobile-transactions", html); Assert.Contains("desktop-transactions", html); Assert.Contains("<details class=\"transaction-item\"", html);
        Assert.Contains(detailsLabel, WebUtility.HtmlDecode(html)); Assert.Contains("Coffee", html); Assert.Contains("&lt;script&gt;bank text&lt;/script&gt;", html); Assert.DoesNotContain("<script>bank text", html);
        var editor = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<Editor>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(Editor.Draft)] = EditorDraft.SimpleTransaction(provider.GetRequiredService<WebState>()) }))).ToHtmlString());
        Assert.DoesNotContain("id=\"principal\"", editor); Assert.DoesNotContain("id=\"counter\"", editor); Assert.DoesNotContain("id=\"kind\"", editor);
        Assert.Contains("beheer#database", WebUtility.HtmlDecode(await RenderEmpty()));

        async Task<string> RenderEmpty()
        {
            data.Accounts.Clear(); return await Render<FirstUseChecklist>();
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture, previousUi = CultureInfo.CurrentUICulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }
    private sealed class StaticJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException();
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
    private sealed class TestFiles : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "huishoudboek-first-week-" + Guid.NewGuid().ToString("N"));
        public TestFiles() => System.IO.Directory.CreateDirectory(Directory);
        public string Path(string name) => System.IO.Path.Combine(Directory, name);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
