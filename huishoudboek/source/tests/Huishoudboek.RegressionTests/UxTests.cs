using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Huishoudboek.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

internal static class UxTests
{
    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("Toolbar scopes hide irrelevant month controls and booking links remain ingress-relative", () =>
        {
            Equal(ToolbarPeriod.Month, UiNavigation.Period("maand?test=1"));
            Equal(ToolbarPeriod.Year, UiNavigation.Period("/jaar"));
            Equal(ToolbarPeriod.Independent, UiNavigation.Period("kalender"));
            Equal(ToolbarPeriod.Independent, UiNavigation.Period("categorietrends"));
            Equal(ToolbarPeriod.Today, UiNavigation.Period("rekeningen"));
            Equal(ToolbarPeriod.History, UiNavigation.Period("inzichten"));
            Equal(ToolbarPeriod.None, UiNavigation.Period("beheer"));
            var id = Guid.NewGuid();
            var url = UiNavigation.Transactions(category: Guid.Empty, account: id, from: new(2026, 11, 1), through: new(2026, 11, 23), transaction: id, owner: id);
            True(url.StartsWith("transacties?allDates=true&")); True(url.Contains("from=2026-11-01"));
            True(url.Contains("through=2026-11-23")); True(url.Contains("categorie=" + Guid.Empty));
            True(url.Contains("owner=" + id)); True(url.Contains("transaction=" + id));
        });
        yield return ("Category identity colours remain stable across rank, language and aggregation", () => WithCultures(() =>
        {
            var id = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");
            Equal(CategoryColors.For(id), CategoryColors.For(Guid.Parse(id.ToString())));
            True(Regex.IsMatch(CategoryColors.For(id), "^#[0-9a-f]{6}$"));
            Equal(CategoryColors.Uncategorized, CategoryColors.For(Guid.Empty));
            True(CategoryColors.Other != CategoryColors.Uncategorized);
        }));
        yield return ("Transaction category filters follow splits and inherited refund categories without editing bank data", () =>
        {
            var s = Fixture(); var account = s.Data.Accounts[0]; var food = s.Data.Categories[0]; var other = s.Data.Categories[1];
            var debit = new Transaction { AccountId = account.Id, Date = new(2026, 11, 10), Description = "Shop", Amount = -100 };
            var refund = new Transaction { AccountId = account.Id, Date = new(2026, 11, 12), Description = "Refund", Amount = 10 };
            s.Data.Transactions.AddRange([debit, refund]);
            var split = new TransactionAllocation { TransactionId = debit.Id, CategoryId = food.Id, Amount = -40 };
            s.Data.TransactionAllocations.AddRange([split, new() { TransactionId = debit.Id, CategoryId = other.Id, Amount = -60 },
                new() { TransactionId = refund.Id, Amount = 10, Treatment = AllocationTreatment.Refund, OriginalTransactionId = debit.Id, OriginalAllocationId = split.Id }]);
            var snapshot = JsonSerializer.Serialize(s.Data);
            var rows = new TransactionFilter(CategoryId: food.Id, From: new(2026, 11, 1), Through: new(2026, 11, 30)).Apply(s.Data, s.Scope);
            Equal(2, rows.Count); Equal(refund.Id, rows[0].Id);
            Equal(1, new TransactionFilter(CategoryId: other.Id, Search: "shop").Apply(s.Data, s.Scope).Count);
            Equal(0, new TransactionFilter(Uncategorized: true).Apply(s.Data, s.Scope).Count);
            Equal(snapshot, JsonSerializer.Serialize(s.Data));
        });
        yield return ("Transaction bank-direction filters use the selected side of transfers and never expand owner scope", () =>
        {
            var s = Fixture(); var a = s.Data.Accounts[0]; var b = new Account { Name = "Savings", OwnerIds = a.OwnerIds };
            s.Data.Accounts.Add(b);
            var transfer = new Transaction { AccountId = a.Id, CounterAccountId = b.Id, Kind = TransactionKind.Transfer, Amount = -50, Date = new(2026, 11, 2) };
            var privateAccount = new Account { Name = "Other person", OwnerIds = [Guid.NewGuid()] }; s.Data.Accounts.Add(privateAccount);
            s.Data.Transactions.AddRange([transfer, new() { AccountId = privateAccount.Id, Amount = 100, Date = transfer.Date }]);
            s.OwnerId = a.OwnerIds[0];
            var filter = new TransactionFilter(AccountId: b.Id, Direction: "credit");
            Equal(transfer.Id, filter.Apply(s.Data, s.Scope).Single().Id); Equal(50m, filter.Effect(transfer, s.Scope));
            Equal(0m, new TransactionFilter().Effect(transfer, s.Scope));
            Equal(0, new TransactionFilter(AccountId: privateAccount.Id).Apply(s.Data, s.Scope).Count);
            Equal(0, new TransactionFilter(AccountId: b.Id, Direction: "debit").Apply(s.Data, s.Scope).Count);
        });
        yield return ("Transaction amount and date filters reject malformed input rather than silently widening the result", () =>
        {
            foreach (var input in new[] { "12,34", "12.34", "0", "" }) True(TransactionFilter.TryAmount(input, out _));
            foreach (var input in new[] { "-1", "1e3", "12,345", "1.234,56", "invalid" }) True(!TransactionFilter.TryAmount(input, out _));
            True(TransactionFilter.TryDate("2026-11-23", out _));
            foreach (var input in new[] { "23-11-2026", "2026-02-30", "1899-12-31", "2201-01-01" }) True(!TransactionFilter.TryDate(input, out _));
        });
        yield return ("Budget pace distinguishes early spending, closed months, fixed bills and missing coverage", () =>
        {
            var s = Fixture(); var a = s.Data.Accounts[0]; var c = s.Data.Categories[0]; var today = new DateOnly(2026, 11, 15);
            BudgetPaceResult Pace(decimal spent, DateOnly date) => BudgetPace.Describe(s.Data, c.Id, spent, 1000, 2026, 11, date, s.Scope);
            Equal("BudgetPaceIncomplete", Pace(200, today).StatusKey);
            BankImportCoverage.ConfirmAccountPeriod(s.Data, a.Id, new(2026, 11, 1), new(2026, 11, 30), new(2026, 11, 30));
            Equal("BudgetPaceAhead", Pace(200, new(2026, 11, 1)).StatusKey);
            Equal("BudgetPaceOnTrack", Pace(200, today).StatusKey);
            Equal(50m, Pace(200, today).ExpectedUsage!.Value);
            Equal("BudgetPaceCompleted", Pace(900, new(2026, 12, 1)).StatusKey);
            Equal("Exceeded", Pace(1200, today).StatusKey); Equal(120m, Pace(1200, today).Usage);
            s.Data.RecurringPayments.Add(new() { AccountId = a.Id, CategoryId = c.Id, Name = "Bill", Amount = -100, DayOfMonth = 20, StartDate = new(2026, 1, 1) });
            Equal("BudgetPaceScheduled", Pace(200, today).StatusKey);
            Equal("BudgetPaceNotStarted", Pace(0, new(2026, 10, 31)).StatusKey);
        });
        yield return ("Cash timeline preserves forecast balances and omits undated reserves from payment markers", () =>
        {
            var today = new DateOnly(2026, 11, 23); var account = new Account { Name = "Current", MinimumCashBuffer = 100 };
            var day = new DailyBalance(today.AddDays(1), 500, 1000, 200, 10, 290, 1290);
            var events = new CashFlowEvent[] { new(account.Id, day.Date, day.Date, "Payday", 1000, null, null),
                new(account.Id, day.Date, day.Date, "Rent", -200, null, null),
                new(account.Id, day.Date, today.AddDays(-3), "Uncertain", -50, null, null, IsUncertain: true),
                new(account.Id, day.Date, day.Date, "Already purchased", -30, null, null) { PurchaseDate = today } };
            var forecast = new AccountCashFlow(account, 500, day.Date, day.Date, 100, 290, day.Date, 10, today, today, false, false, [], events, [day, day with { Date = today.AddDays(31) }]);
            var original = JsonSerializer.Serialize(forecast); var points = CashTimeline.Points(forecast, today, 30);
            Equal(2, points.Count); Equal(500m, points[0].Closing); Equal(290m, points[1].Lowest); Equal(1290m, points[1].Closing);
            Equal("Payday", points[1].Income.Single()); Equal("Rent", points[1].Payments.Single());
            Equal(1000m, points[1].IncomeAmount); Equal(200m, points[1].PaymentAmount);
            Equal(original, JsonSerializer.Serialize(forecast)); Equal(3, CashTimeline.Points(forecast, today, 90).Count);
        });
        yield return ("Shared editor validation is field-specific, culture-independent and presentation-only", () => WithCultures(() =>
        {
            var s = Fixture(); var snapshot = JsonSerializer.Serialize(s.Data); var draft = EditorDraft.SimpleTransaction(s);
            var fingerprint = draft.Fingerprint;
            True(!draft.ValidateFields()); True(draft.Fields.Single(f => f.Key == "description").Error.Length > 0);
            True(draft.Fields.Single(f => f.Key == "amount").Error.Length > 0);
            draft.Fields.Single(f => f.Key == "description").Value = "Shop";
            draft.Fields.Single(f => f.Key == "amount").Value = "12,34";
            True(draft.ValidateFields()); Equal(12.34m, draft.Number("amount")); True(draft.Fingerprint != fingerprint);
            draft.Fields.Single(f => f.Key == "amount").Value = "12.345"; True(!draft.ValidateFields());
            draft.Fields.Single(f => f.Key == "amount").Value = "12.34"; True(draft.ValidateFields());
            Equal(snapshot, JsonSerializer.Serialize(s.Data));
        }));
        yield return ("Shared field errors provide explicit ARIA states and associated error descriptions", () => WithCultures(() =>
        {
            var s = Fixture(); var draft = EditorDraft.SimpleTransaction(s); draft.ValidateFields();
            var html = Render<Editor>(s, new Dictionary<string, object?> { [nameof(Editor.Draft)] = draft });
            True(html.Contains("aria-invalid=\"true\"")); True(html.Contains("aria-describedby=\"description-error\""));
            True(html.Contains("id=\"description-error\""));
        }));
        yield return ("Import review groups invalid, uncategorized, future and unlinked rows without selecting invalid records", () =>
        {
            var today = new DateOnly(2026, 11, 23);
            var source = new PreviewRow(1, today, "Shop", -20, null, true, true, "Ready", Guid.NewGuid(), null, "Normal");
            True(!new ImportReviewRow(source).NeedsReview(today));
            foreach (var row in new[] { source with { IsValid = false }, source with { CategoryId = null }, source with { Date = today.AddDays(1) },
                source with { AmbiguousIdentity = true }, source with { HasPlannedIdentity = true }, source with { Include = false } })
                True(new ImportReviewRow(row).NeedsReview(today));
            True(!new ImportReviewRow(source with { IsValid = false, Include = true }).Include);
        });
        yield return ("Dashboard quick entry, transaction filters and source links render translated controls in both languages", () => WithCultures(() =>
        {
            var s = Fixture(); s.Data.Transactions.Add(new() { AccountId = s.Data.Accounts[0].Id, Date = new(2026, 11, 12), Amount = -20, Description = "Shop", CategoryId = s.Data.Categories[0].Id });
            var home = WebUtility.HtmlDecode(Render<Home>(s)); True(home.Contains(UiCulture.Text("QuickActions")));
            True(home.Contains(UiCulture.Text("AddExpense"))); True(home.Contains("transaction=" + s.Data.Transactions[0].Id));
            var transactions = WebUtility.HtmlDecode(Render<Transactions>(s));
            True(transactions.Contains(UiCulture.Text("AdvancedFilters"))); True(transactions.Contains(UiCulture.Text("BankDirection")));
            True(transactions.Contains(UiCulture.Text("FilterTotalsNote"))); True(transactions.Contains(UiCulture.Text("AllCategories")));
            True(!transactions.Contains(">AllCategories<"));
        }));
        yield return ("Nested dialogs and mobile navigation have distinct accessible label and submenu targets", () => WithCultures(() =>
        {
            var s = Fixture(); var modalA = Render<Modal>(s); var modalB = Render<Modal>(s);
            var idA = Regex.Match(modalA, "aria-labelledby=\"([^\"]+)\"").Groups[1].Value;
            var idB = Regex.Match(modalB, "aria-labelledby=\"([^\"]+)\"").Groups[1].Value;
            True(idA != idB); True(modalA.Contains("id=\"" + idA + "\""));
            var desktop = Render<MainNavigation>(s); var mobile = Render<MainNavigation>(s, new Dictionary<string, object?> { [nameof(MainNavigation.IdPrefix)] = "mobile" });
            True(desktop.Contains("id=\"nav-overviews\"")); True(mobile.Contains("id=\"mobile-nav-overviews\""));
            True(mobile.Contains("aria-controls=\"mobile-nav-overviews\""));
        }));
    }

    private static WebState Fixture()
    {
        var s = new WebState(null!) { Year = 2026, Month = 11 };
        var owner = new Owner { Name = "Owner" }; s.Data.Owners.Add(owner);
        s.Data.Accounts.Add(new() { Name = "Current", OwnerIds = [owner.Id], OpeningDate = new(2026, 1, 1) });
        s.Data.Categories.Clear(); s.Data.Categories.AddRange([new() { Name = "Food", MonthlyBudget = 1000 }, new() { Name = "Other" }]);
        return s;
    }
    internal static string Render<T>(WebState state, IDictionary<string, object?>? parameters = null) where T : IComponent
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization(); services.AddSingleton(state);
        services.AddSingleton<IInvestmentAdministration>(new NoInvestments());
        services.AddSingleton<IReceiptAdministration>(new NoReceipts());
        services.AddSingleton<IJSRuntime>(new NoJs()); services.AddSingleton<UiPreferences>(); services.AddSingleton<NavigationManager>(new TestNavigation());
        using var provider = services.BuildServiceProvider(); return Run().GetAwaiter().GetResult();
        async Task<string> Run()
        {
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()))).ToHtmlString());
        }
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/ingress/", "http://localhost/ingress/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
    private sealed class NoReceipts : IReceiptAdministration
    {
        public Task<ReceiptScan> RecognizeAsync(Stream source, string name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveAsync(string revision, PurchaseReceipt receipt, IReadOnlyList<PurchaseReceiptItem> items,
            bool keepDocument, Guid? uploadId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void DiscardUpload(Guid uploadId) { }
    }
    private sealed class NoInvestments : IInvestmentAdministration
    {
        public Task<InvestmentMarketSnapshot> ReadAsync() => throw new UnauthorizedAccessException();
        public Task SaveSettingsAsync(InvestmentQuoteSettings settings, string? key, bool removeKey = false) => throw new NotSupportedException();
        public Task TestAsync() => throw new NotSupportedException();
        public Task TestAmznAsync() => throw new NotSupportedException();
        public Task RefreshAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<InvestmentSymbol>> SearchAsync(string keywords) => throw new NotSupportedException();
    }
    private static void WithCultures(Action run)
    {
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try { foreach (var name in new[] { "nl-NL", "en-GB" }) { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name); run(); } }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
}
