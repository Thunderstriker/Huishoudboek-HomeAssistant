using System.Globalization;
using System.Net;
using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Huishoudboek.Web.Components.Shared;
using Microsoft.Data.Sqlite;

internal static class CategoryHierarchyTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("Category hierarchy orders parents before children and renders complete nested paths", () =>
        {
            var f = new Fixture();
            var entries = CategoryHierarchy.Ordered(f.Data);
            True(entries.Select(e => e.Category.Id).SequenceEqual(new[] { f.Other.Id, f.Root.Id, f.Child.Id, f.Leaf.Id }));
            Equal(0, entries[0].Depth); Equal(1, entries[2].Depth); Equal(2, entries[3].Depth);
            Equal("Wonen en Hypotheek › Elektronica › Verlichting", entries[3].Path);
            Equal(entries[3].Path, CategoryHierarchy.Path(f.Data, f.Leaf.Id));
            True(CategoryHierarchy.SubtreeIds(f.Data, f.Child.Id).SetEquals(new[] { f.Child.Id, f.Leaf.Id }));
            var added = new Category { Name = "Lampen", ParentId = f.Leaf.Id };
            HouseholdEdits.SaveCategory(f.Data, added); Validation.Validate(f.Data);
            Equal(3, CategoryHierarchy.Ordered(f.Data).Single(e => e.Category.Id == added.Id).Depth);
        });
        yield return ("Moving root categories and entire subtrees preserves financial data and category settings", () =>
        {
            var f = new Fixture(); var before = FinancialSnapshot(f.Data);
            HouseholdEdits.SaveCategory(f.Data, f.Root with { ParentId = f.Other.Id });
            Validation.Validate(f.Data);
            Equal("Overige › Wonen en Hypotheek › Elektronica › Verlichting", CategoryHierarchy.Path(f.Data, f.Leaf.Id));
            Equal(before, FinancialSnapshot(f.Data));
            HouseholdEdits.SaveCategory(f.Data, f.Child with { ParentId = f.Other.Id });
            Equal("Overige › Elektronica › Verlichting", CategoryHierarchy.Path(f.Data, f.Leaf.Id));
            HouseholdEdits.SaveCategory(f.Data, f.Child with { ParentId = null });
            Validation.Validate(f.Data); Equal("Elektronica › Verlichting", CategoryHierarchy.Path(f.Data, f.Leaf.Id));
            Equal(before, FinancialSnapshot(f.Data));
            Equal(30m, f.Data.Categories.Single(c => c.Id == f.Child.Id).MonthlyBudget);
            True(f.Data.Categories.Single(c => c.Id == f.Child.Id).IsEssential);
        });
        yield return ("Invalid category parents and descendant cycles are rejected before save mutation", () =>
        {
            var f = new Fixture(); var before = JsonSerializer.Serialize(f.Data);
            foreach (var parentId in new[] { f.Root.Id, f.Child.Id, f.Leaf.Id, Guid.Empty, Guid.NewGuid() })
            {
                Throws(() => HouseholdEdits.SaveCategory(f.Data, f.Root with { ParentId = parentId }));
                Equal(before, JsonSerializer.Serialize(f.Data));
            }
            var newItem = new Category { Name = "New" }; newItem.ParentId = newItem.Id;
            Throws(() => HouseholdEdits.SaveCategory(f.Data, newItem)); Equal(before, JsonSerializer.Serialize(f.Data));
            Throws(() => HouseholdEdits.SaveCategory(f.Data, new Category { Name = " elektronica ", ParentId = f.Other.Id }));
            Equal(before, JsonSerializer.Serialize(f.Data));
        });
        yield return ("Aggregate validation rejects imported missing parents, self references and multi-node cycles", () =>
        {
            foreach (var parent in new[] { Guid.NewGuid(), Guid.Empty })
            { var f = new Fixture(); f.Root.ParentId = parent; Throws(() => Validation.Validate(f.Data)); }
            var self = new Fixture(); self.Root.ParentId = self.Root.Id; Throws(() => Validation.Validate(self.Data));
            var cycle = new Fixture(); cycle.Root.ParentId = cycle.Leaf.Id; Throws(() => Validation.Validate(cycle.Data));
            var valid = new Fixture(); valid.Root.ParentId = valid.Other.Id; Validation.Validate(valid.Data);
        });
        yield return ("Renaming a category updates descendant paths without changing parentage or references", () =>
        {
            var f = new Fixture(); var before = FinancialSnapshot(f.Data);
            Categorization.RenameCategory(f.Data, f.Child.Id, " Apparaten ");
            Equal("Wonen en Hypotheek › Apparaten › Verlichting", CategoryHierarchy.Path(f.Data, f.Leaf.Id));
            Equal<Guid?>(f.Root.Id, f.Child.ParentId); Equal(before, FinancialSnapshot(f.Data));
            var draft = EditorDraft.Category(f.State, f.Child, renameOnly: true);
            Equal(1, draft.Fields.Count); draft.Fields[0].Value = "Elektronica"; draft.Apply(f.Data);
            Equal<Guid?>(f.Root.Id, f.Child.ParentId); Validation.Validate(f.Data);
        });
        yield return ("Deleting an intermediate parent promotes children and preserves their financial references", () =>
        {
            var f = new Fixture(); var sibling = new Category { Name = "Computers", ParentId = f.Child.Id, MonthlyBudget = 15 };
            f.Data.Categories.Add(sibling);
            var preview = Categorization.PreviewCategoryDeletion(f.Data, f.Child.Id); Equal(2, preview.ChildCategoryCount);
            Categorization.DeleteCategory(f.Data, f.Child.Id); Validation.Validate(f.Data);
            Equal<Guid?>(f.Root.Id, f.Leaf.ParentId); Equal<Guid?>(f.Root.Id, sibling.ParentId);
            Equal("Wonen en Hypotheek › Verlichting", CategoryHierarchy.Path(f.Data, f.Leaf.Id));
            Equal<Guid?>(null, f.Data.Transactions.Single().CategoryId);
            Equal<Guid?>(f.Leaf.Id, f.Data.RecurringPayments.Single().CategoryId);
            Equal(20m, f.Leaf.MonthlyBudget); Equal(0, f.Data.BudgetOverrides.Count);
            Equal<Guid?>(null, f.Data.Rules.Single().CategoryId);
        });
        yield return ("Deleting a root preserves the surviving subtree, budgets, receipts and learned settings", () =>
        {
            var f = new Fixture(); var before = FinancialSnapshot(f.Data);
            Equal(1, Categorization.PreviewCategoryDeletion(f.Data, f.Root.Id).ChildCategoryCount);
            Categorization.DeleteCategory(f.Data, f.Root.Id); Validation.Validate(f.Data);
            Equal<Guid?>(null, f.Child.ParentId); Equal<Guid?>(f.Child.Id, f.Leaf.ParentId);
            Equal("Elektronica › Verlichting", CategoryHierarchy.Path(f.Data, f.Leaf.Id));
            Equal(before, FinancialSnapshot(f.Data));
        });
        yield return ("Category parent drafts exclude self and descendants and support root moves in both languages", () => WithCultures(() =>
        {
            var f = new Fixture(); var before = JsonSerializer.Serialize(f.Data);
            var draft = EditorDraft.Category(f.State, f.Child); var parent = draft.Fields.Single(field => field.Key == "parent");
            Equal(f.Root.Id.ToString(), parent.Value);
            True(parent.Options.Any(o => o.Value == "" && o.Label == UiCulture.Text("RootCategory")));
            True(parent.Options.Any(o => o.Value == f.Other.Id.ToString()));
            True(!parent.Options.Any(o => o.Value == f.Child.Id.ToString() || o.Value == f.Leaf.Id.ToString()));
            parent.Value = f.Leaf.Id.ToString(); True(!draft.ValidateFields()); True(parent.Error.Length > 0);
            Throws(() => draft.Apply(f.Data)); Equal(before, JsonSerializer.Serialize(f.Data));
            parent.Value = ""; True(draft.ValidateFields()); Equal(before, JsonSerializer.Serialize(f.Data));
            draft.Apply(f.Data); Validation.Validate(f.Data);
            Equal<Guid?>(null, f.Data.Categories.Single(c => c.Id == f.Child.Id).ParentId);
        }));
        yield return ("New subcategory drafts preselect the parent with zero budget and matching counting purpose", () => WithCultures(() =>
        {
            var f = new Fixture(); var before = JsonSerializer.Serialize(f.Data);
            var draft = EditorDraft.Category(f.State, parentId: f.Child.Id);
            Equal(UiCulture.Text("AddSubcategory"), draft.Title); Equal(f.Child.Id, draft.Id("parent")); Equal(0m, draft.Number("budget"));
            draft.Fields.Single(field => field.Key == "name").Value = "Computers";
            True(draft.ValidateFields()); Equal(before, JsonSerializer.Serialize(f.Data)); draft.Apply(f.Data);
            var added = f.Data.Categories.Single(c => c.Name == "Computers"); Equal<Guid?>(f.Child.Id, added.ParentId);
            Equal(CategoryPurpose.Normal, added.Purpose); True(!added.IsEssential); Validation.Validate(f.Data);
            HouseholdEdits.SaveCategory(f.Data, f.Other with { Purpose = CategoryPurpose.Savings });
            var savings = EditorDraft.Category(f.State, parentId: f.Other.Id);
            Equal("Savings", savings.Text("purpose")); Equal(0m, savings.Number("budget"));
        }));
        yield return ("Category paths appear in budget, assignment, purchase, import choices and transaction editors", () => WithCultures(() =>
        {
            var f = new Fixture(); var path = CategoryHierarchy.Path(f.Data, f.Leaf.Id);
            Equal(path, f.State.CategoryName(f.Leaf.Id)); Equal(UiCulture.Text("Uncategorized"), f.State.CategoryName(null));
            Equal(path, EditorDraft.CategoryLabel(f.Data, f.Leaf));
            var transaction = EditorDraft.Transaction(f.State, f.Data.Transactions.Single());
            Equal(path, transaction.Fields.Single(field => field.Key == "category").Options.Single(o => o.Value == f.Leaf.Id.ToString()).Label);
            var table = new ImportTable(["Date", "Description", "Amount"], [[new("2026-11-15"), new("New purchase"), new("-10")]]);
            var profile = new ImportProfile { DateColumn = "Date", DescriptionColumn = "Description", AmountColumn = "Amount", DateFormat = "yyyy-MM-dd" };
            var import = ImportService.Preview(table, profile, f.Data.Accounts[0].Id, new(2026, 11, 1), Today, f.Data).Single();
            Equal(path, import.CategoryChoices.Single(c => c.Id == f.Leaf.Id).Name);
            var budgets = WebUtility.HtmlDecode(UxTests.Render<Budgets>(f.State));
            True(budgets.Contains(UiCulture.Text("ParentCategory"))); True(budgets.Contains(UiCulture.Text("Subcategory")));
            True(budgets.Contains("Wonen en Hypotheek › Elektronica"));
            var categorize = WebUtility.HtmlDecode(UxTests.Render<Categorize>(f.State)); True(categorize.Contains(path));
            f.Data.PurchaseReceiptItems.Add(new() { ReceiptId = f.Data.PurchaseReceipts.Single().Id, Name = "Lamp", CategoryId = f.Leaf.Id });
            var purchases = WebUtility.HtmlDecode(UxTests.Render<Purchases>(f.State));
            True(purchases.Contains(path));
            var editor = WebUtility.HtmlDecode(UxTests.Render<Editor>(f.State, new Dictionary<string, object?> { [nameof(Editor.Draft)] = EditorDraft.Category(f.State, f.Leaf) }));
            True(editor.Contains(UiCulture.Text("RootCategory"))); True(editor.Contains("Wonen en Hypotheek › Elektronica"));
        }));
        yield return ("Category organization keeps exact budgets and trends while transaction text search includes parent paths", () =>
        {
            var f = new Fixture(); var report = Finance.Report(f.Data, 2026, 11, Today);
            Equal(20m, report.Outflow); Equal(0m, report.Categories.Single(c => c.Id == f.Root.Id).Spent);
            Equal(20m, report.Categories.Single(c => c.Id == f.Child.Id).Spent);
            Equal(100m, report.Categories.Single(c => c.Id == f.Root.Id).Budget);
            Equal(45m, report.Categories.Single(c => c.Id == f.Child.Id).Budget);
            Equal(0m, CategoryHistory.Create(f.Data, f.Root.Id, new(2026, 11, 1), Today, Today).Total);
            Equal(20m, CategoryHistory.Create(f.Data, f.Child.Id, new(2026, 11, 1), Today, Today).Total);
            Equal(0, new TransactionFilter(CategoryId: f.Root.Id).Apply(f.Data, f.State.Scope).Count);
            Equal(1, new TransactionFilter(Search: "Wonen en Hypotheek").Apply(f.Data, f.State.Scope).Count);
            Equal(1, new TransactionFilter(CategoryId: f.Child.Id).Apply(f.Data, f.State.Scope).Count);
            var before = JsonSerializer.Serialize(f.Data.Transactions);
            HouseholdEdits.SaveCategory(f.Data, f.Child with { ParentId = f.Other.Id });
            Equal(0, new TransactionFilter(Search: "Wonen en Hypotheek").Apply(f.Data, f.State.Scope).Count);
            Equal(1, new TransactionFilter(Search: "Overige").Apply(f.Data, f.State.Scope).Count);
            Equal(before, JsonSerializer.Serialize(f.Data.Transactions)); Equal(20m, Finance.Report(f.Data, 2026, 11, Today).Outflow);
        });
        yield return ("SQLite and database backups preserve nested category parents and all financial references", () => InDirectory(directory =>
        {
            var f = new Fixture(); var store = new SqliteHouseholdStore(System.IO.Path.Combine(directory, "source.db")); store.Save(f.Data);
            Equal(18L, Scalar(store.DatabasePath, "PRAGMA user_version"));
            Equal(JsonSerializer.Serialize(f.Data.Categories), JsonSerializer.Serialize(store.Load().Categories));
            var backup = System.IO.Path.Combine(directory, "backup.db"); store.Backup(backup);
            var restored = new SqliteHouseholdStore(System.IO.Path.Combine(directory, "restored.db")); restored.Restore(backup);
            var loaded = restored.Load(); Equal("Wonen en Hypotheek › Elektronica › Verlichting", CategoryHierarchy.Path(loaded, f.Leaf.Id));
            Equal(FinancialSnapshot(f.Data), FinancialSnapshot(loaded));
        }));
        yield return ("Schema 17 migrates once with a pre-upgrade backup and old backup reads remain non-mutating", () => InDirectory(directory =>
        {
            var f = new Fixture(); f.Child.ParentId = null; f.Leaf.ParentId = null;
            var path = System.IO.Path.Combine(directory, "legacy.db"); new SqliteHouseholdStore(path).Save(f.Data);
            Sql(path, RecognitionTests.RemoveSchema18Columns + "PRAGMA user_version=17;");
            var old = SqliteHouseholdStore.ReadBackup(path); True(old.Categories.All(c => c.ParentId == null));
            Equal(17L, Scalar(path, "PRAGMA user_version")); Equal(0, Directory.GetFiles(directory, "*.voor-versie-*.db").Length);
            var upgraded = new SqliteHouseholdStore(path, allowCreate: false); var loaded = upgraded.Load();
            Equal(18L, Scalar(path, "PRAGMA user_version")); True(loaded.Categories.All(c => c.ParentId == null));
            Equal(FinancialSnapshot(f.Data), FinancialSnapshot(loaded));
            var backup = Directory.GetFiles(directory, "*.voor-versie-18-*.db").Single(); Equal(17L, Scalar(backup, "PRAGMA user_version"));
            _ = new SqliteHouseholdStore(path, allowCreate: false); Equal(1, Directory.GetFiles(directory, "*.voor-versie-18-*.db").Length);
            var target = new SqliteHouseholdStore(System.IO.Path.Combine(directory, "target.db")); target.Save(new Fixture().Data);
            target.Restore(backup); True(target.Load().Categories.All(c => c.ParentId == null));
            Equal(FinancialSnapshot(f.Data), FinancialSnapshot(target.Load()));
        }));
        yield return ("Category reparenting saves atomically and retains stale-edit conflict protection", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var f = new Fixture(); var start = coordinator.Read();
            var saved = coordinator.Update(start.Revision, d =>
            { d.Owners = f.Data.Owners; d.Accounts = f.Data.Accounts; d.Categories = f.Data.Categories;
                d.Transactions = f.Data.Transactions; d.InsightSettings = f.Data.InsightSettings; });
            Throws(() => coordinator.Update(saved.Revision, d => HouseholdEdits.SaveCategory(d, d.Categories.Single(c => c.Id == f.Root.Id) with { ParentId = f.Leaf.Id })));
            Equal(saved.Revision, coordinator.Read().Revision);
            var moved = coordinator.Update(saved.Revision, d => HouseholdEdits.SaveCategory(d, d.Categories.Single(c => c.Id == f.Child.Id) with { ParentId = f.Other.Id }));
            Throws(() => coordinator.Update(saved.Revision, d => HouseholdEdits.SaveCategory(d, f.Child with { ParentId = null })));
            Equal(moved.Revision, coordinator.Read().Revision);
            Equal("Overige › Elektronica › Verlichting", CategoryHierarchy.Path(coordinator.Read().Data, f.Leaf.Id));
            Equal(JsonSerializer.Serialize(f.Data.Transactions), JsonSerializer.Serialize(coordinator.Read().Data.Transactions));
        }));
    }

    private sealed class Fixture
    {
        public WebState State { get; } = new(null!) { Year = 2026, Month = 11 };
        public HouseholdData Data => State.Data;
        public Category Root { get; } = new() { Name = "Wonen en Hypotheek", MonthlyBudget = 100 };
        public Category Child { get; } = new() { Name = "Elektronica", MonthlyBudget = 30, IsEssential = true };
        public Category Leaf { get; } = new() { Name = "Verlichting", MonthlyBudget = 20 };
        public Category Other { get; } = new() { Name = "Overige" };
        public Fixture()
        {
            Data.Categories.Clear(); Child.ParentId = Root.Id; Leaf.ParentId = Child.Id;
            Data.Categories.AddRange([Leaf, Other, Child, Root]); Data.InsightSettings.SpendingForecastCategoryIds = [Child.Id];
            var owner = new Owner { Name = "Owner" }; Data.Owners.Add(owner);
            var account = new Account { Name = "Current", OwnerIds = [owner.Id], OpeningDate = new(2026, 1, 1), OpeningBalance = 1000 };
            Data.Accounts.Add(account);
            Data.Transactions.Add(new() { AccountId = account.Id, Date = new(2026, 11, 10), Amount = -20, Description = "Lamp shop", CategoryId = Child.Id });
            Data.RecurringPayments.Add(new() { Name = "Bill", AccountId = account.Id, CategoryId = Leaf.Id, Amount = -12, DayOfMonth = 20, StartDate = new(2026, 1, 1) });
            Data.Rules.Add(new() { Contains = "lamp", Name = "Lamp", CategoryId = Child.Id });
            Data.BudgetOverrides.Add(new() { CategoryId = Child.Id, Year = 2026, Month = 11, Amount = 45 });
            Data.SpendingHabits.Add(new() { AccountId = account.Id, CategoryId = Child.Id, WeeklyAmount = 5 });
            Data.PurchaseReceipts.Add(new() { Merchant = "Shop", DefaultCategoryId = Child.Id });
            Validation.Validate(Data);
        }
    }
    private static string FinancialSnapshot(HouseholdData data) => JsonSerializer.Serialize(new
    { data.Transactions, data.TransactionAllocations, data.RecurringPayments, data.Rules, data.BudgetOverrides,
        data.PurchaseReceipts, data.PurchaseReceiptItems, data.SpendingHabits, data.InsightSettings });
    private static void InDirectory(Action<string> run)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opencode", "category-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { run(directory); } finally { Directory.Delete(directory, recursive: true); }
    }
    private static void Sql(string path, string sql)
    { using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private static long Scalar(string path, string sql)
    { using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture); }
    private static void WithCultures(Action run)
    {
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try { foreach (var name in new[] { "nl-NL", "en-GB" }) { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name); run(); } }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Throws(Action run) { try { run(); } catch (InvalidOperationException) { return; } throw new Exception("Expected validation/conflict failure."); }
}
