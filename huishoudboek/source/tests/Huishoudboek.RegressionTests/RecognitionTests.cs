using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
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

internal static class RecognitionTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);
    private const string September = "AXA 0105183224401042026-09-1020260900000010518322440";
    private const string November = "AXA 0105183224401042026-11-1020261100000010518322440";
    internal const string RemoveSchema18Columns = "ALTER TABLE Category DROP COLUMN ParentId;";
    internal const string RemoveSchema17Tables = RemoveSchema18Columns + "DROP TABLE SpendingAdjustment; DROP TABLE PurchaseDateDecision; DROP TABLE SpendingHabit;";
    internal const string RemoveSchema16Columns = RemoveSchema17Tables + """
        ALTER TABLE [Transaction] DROP COLUMN RecognizedName;
        ALTER TABLE [Transaction] DROP COLUMN RecognitionRuleId;
        ALTER TABLE CategorizationRule DROP COLUMN Name;
        ALTER TABLE CategorizationRule DROP COLUMN AccountId;
        ALTER TABLE CategorizationRule DROP COLUMN Direction;
        ALTER TABLE CategorizationRule DROP COLUMN RecurringPaymentId;
        ALTER TABLE CategorizationRule DROP COLUMN NormalizeText;
        """;

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("AXA stable reference recognizes changing monthly descriptions independently of schedule margins", () =>
        {
            var f = new Fixture(); var plan = f.Plan();
            var september = f.Entry(September, new(2026, 9, 10));
            var changedAmount = f.Entry(November, new(2026, 11, 10), -123.45m);
            var changedDate = f.Entry(November, new(2026, 10, 25));
            var original = september.Description;
            Finance.ApplySuggestions(f.Data, [september, changedAmount, changedDate]);
            foreach (var t in new[] { september, changedAmount, changedDate })
            { Equal("AXA – Car insurance", t.RecognizedName); Equal<Guid?>(f.Insurance.Id, t.CategoryId); }
            Equal<Guid?>(plan.Id, september.RecurringPaymentId);
            Equal<Guid?>(null, changedAmount.RecurringPaymentId); Equal<Guid?>(null, changedDate.RecurringPaymentId);
            Equal(original, september.Description); Validation.Validate(f.Data);
        });
        yield return ("Recognition supports category-only and name-only setups without creating plans", () =>
        {
            var f = new Fixture(); f.Data.Rules.Add(new() { Contains = "AXA", CategoryId = f.OtherCategory.Id });
            f.Data.Rules.Add(new() { Contains = "01051832244", CategoryId = f.Insurance.Id, Name = "Insurance X" });
            var t = f.Entry(); Finance.ApplySuggestions(f.Data, t);
            Equal("Insurance X", t.RecognizedName); Equal<Guid?>(f.OtherCategory.Id, t.CategoryId);
            f.Data.Rules[0].CategoryId = null; f.Data.Rules[0].Name = "Insurance X"; f.Data.Rules[1].CategoryId = null;
            var nameOnly = f.Entry(September, new(2026, 9, 10)); Finance.ApplySuggestions(f.Data, nameOnly);
            Equal("Insurance X", nameOnly.RecognizedName); Equal<Guid?>(null, nameOnly.CategoryId);
            Equal(0, f.Data.RecurringPayments.Count); Validation.Validate(f.Data);
        });
        yield return ("Account and direction restrictions apply equally to names and categories", () =>
        {
            var f = new Fixture(); f.Rule();
            var outgoing = f.Entry(); var incoming = f.Entry(amount: 100); var otherAccount = f.Entry(); otherAccount.AccountId = f.OtherAccount.Id;
            Finance.ApplySuggestions(f.Data, [outgoing, incoming, otherAccount]);
            Equal("Insurance X", outgoing.RecognizedName);
            foreach (var t in new[] { incoming, otherAccount }) { Equal("", t.RecognizedName); Equal<Guid?>(null, t.CategoryId); }
        });
        yield return ("Ambiguous identities and competing planned payments are never arbitrarily selected", () =>
        {
            var f = new Fixture(); f.Plan(); f.Plan("AXA – Home insurance"); var t = f.Entry();
            Finance.ApplySuggestions(f.Data, t);
            Equal("", t.RecognizedName); Equal<Guid?>(null, t.RecurringPaymentId); True(new RecognitionMatcher(f.Data).Ambiguous(t));
            // Even equal plan labels must not choose arbitrary provenance between different plans.
            f.Data.Rules[1].Name = f.Data.Rules[0].Name;
            Finance.ApplySuggestions(f.Data, t); Equal("", t.RecognizedName); Equal<Guid?>(null, t.RecognitionRuleId);
        });
        yield return ("A confirmed recurring link is authoritative when other text rules overlap", () =>
        {
            var f = new Fixture(); var plan = f.Plan(); var t = f.Entry(); RecurringSchedule.Link(f.Data, t.Id, plan.Id, new(2026, 11, 10));
            t = f.Data.Transactions.Single();
            f.Data.Rules.Add(new() { Name = "Other identity", Contains = "AXA" });
            Finance.ApplySuggestions(f.Data, t);
            Equal(plan.Name, t.RecognizedName); Equal<Guid?>(plan.Id, t.RecurringPaymentId); True(!new RecognitionMatcher(f.Data).Ambiguous(t));
        });
        yield return ("Personal descriptions take precedence and recognition never changes bank data or coverage", () =>
        {
            var f = new Fixture(); var rule = f.Rule(); var t = f.Entry(); t.UserDescription = "My personal note";
            BankImportCoverage.ConfirmAccountPeriod(f.Data, f.Account.Id, new(2026, 11, 1), Today, Today);
            var fingerprint = f.Data.AccountStatementPeriods[0].BankDataFingerprint;
            var before = (t.Description, t.Amount, t.Date, t.Id, t.AccountId);
            Finance.ApplySuggestions(f.Data, t);
            Equal("My personal note", TransactionRecognition.DisplayName(f.Data, t)); Equal("Insurance X", t.RecognizedName);
            Equal(before, (t.Description, t.Amount, t.Date, t.Id, t.AccountId));
            True(BankImportCoverage.IsCurrent(f.Data, f.Data.AccountStatementPeriods[0]));
            Equal(fingerprint, f.Data.AccountStatementPeriods[0].BankDataFingerprint);
            TransactionRecognition.DeleteRule(f.Data, rule.Id); Equal("Insurance X", t.RecognizedName); Equal<Guid?>(null, t.RecognitionRuleId);
            Validation.Validate(f.Data);
        });
        yield return ("Corrected bank text and manual planning links cannot retain a stale automatic identity", () =>
        {
            var f = new Fixture(); var rule = f.Rule(); var t = f.Entry(); Finance.ApplySuggestions(f.Data, t);
            HouseholdEdits.SaveTransaction(f.Data, t with { Description = "Unrelated supplier" });
            Equal("", f.Data.Transactions.Single().RecognizedName); Equal<Guid?>(null, f.Data.Transactions.Single().RecognitionRuleId);
            var plan = f.Plan(); t = f.Data.Transactions.Single();
            t.RecognizedName = "Old automatic label"; t.RecognitionRuleId = rule.Id; t.UserDescription = "Personal note";
            RecurringSchedule.Link(f.Data, t.Id, plan.Id, new(2026, 11, 10));
            t = f.Data.Transactions.Single(); Equal(plan.Name, t.RecognizedName); Equal("Personal note", TransactionRecognition.DisplayName(f.Data, t));
            Validation.Validate(f.Data);
        });
        yield return ("History preview is read-only and updates only explicitly selected entries", () =>
        {
            var f = new Fixture(); f.Rule(); var first = f.Entry(September, new(2026, 9, 10)); var second = f.Entry();
            first.CategoryId = f.OtherCategory.Id; first.UserDescription = "Keep this";
            var original = JsonSerializer.Serialize(f.Data);
            var preview = TransactionRecognition.PreviewHistory(f.Data, Today);
            Equal(2, preview.Count); Equal(original, JsonSerializer.Serialize(f.Data));
            TransactionRecognition.ApplyHistory(f.Data, new HashSet<Guid> { first.Id }, Today);
            Equal("Insurance X", f.Data.Transactions.Single(t => t.Id == first.Id).RecognizedName);
            Equal<Guid?>(f.OtherCategory.Id, f.Data.Transactions.Single(t => t.Id == first.Id).CategoryId);
            Equal("Keep this", f.Data.Transactions.Single(t => t.Id == first.Id).UserDescription);
            Equal(second, f.Data.Transactions.Single(t => t.Id == second.Id)); Validation.Validate(f.Data);
            var saved = JsonSerializer.Serialize(f.Data);
            Throws(() => TransactionRecognition.ApplyHistory(f.Data, new HashSet<Guid> { Guid.NewGuid() }, Today));
            Equal(saved, JsonSerializer.Serialize(f.Data));
        });
        yield return ("Deselecting duplicate history entries cannot manufacture an automatic planning match", () =>
        {
            var f = new Fixture(); f.Plan(); var first = f.Entry(); var duplicate = f.Entry();
            var changes = TransactionRecognition.PreviewHistory(f.Data, Today);
            Equal(2, changes.Count); True(changes.All(c => c.After.RecurringPaymentId == null));
            TransactionRecognition.ApplyHistory(f.Data, new HashSet<Guid> { first.Id }, Today);
            True(f.Data.Transactions.All(t => t.RecurringPaymentId == null)); Equal("", duplicate.RecognizedName);
        });
        yield return ("Historical recognition preserves original whitespace and bank coverage even when adding a planning link", () =>
        {
            var f = new Fixture(); f.Plan(); var t = f.Entry("  " + November + "  "); t.UserDescription = "  Personal note  ";
            BankImportCoverage.ConfirmAccountPeriod(f.Data, f.Account.Id, new(2026, 11, 1), Today, Today);
            var changes = TransactionRecognition.PreviewHistory(f.Data, Today); Equal(1, changes.Count);
            TransactionRecognition.ApplyHistory(f.Data, changes.Select(c => c.Before.Id).ToHashSet(), Today);
            var saved = f.Data.Transactions.Single(); Equal(t.Description, saved.Description); Equal(t.UserDescription, saved.UserDescription);
            True(saved.RecurringPaymentId != null); True(BankImportCoverage.IsCurrent(f.Data, f.Data.AccountStatementPeriods.Single()));
        });
        yield return ("Splits and reimbursements keep their financial handling while receiving readable names", () =>
        {
            var f = new Fixture(); f.Plan(); var split = f.Entry();
            f.Data.TransactionAllocations.Add(new() { TransactionId = split.Id, Amount = -40, CategoryId = f.OtherCategory.Id });
            f.Data.TransactionAllocations.Add(new() { TransactionId = split.Id, Amount = -60, CategoryId = f.Insurance.Id });
            var allocations = JsonSerializer.Serialize(f.Data.TransactionAllocations);
            Finance.ApplySuggestions(f.Data, split);
            Equal("AXA – Car insurance", split.RecognizedName); Equal<Guid?>(null, split.CategoryId); Equal<Guid?>(null, split.RecurringPaymentId);
            Equal(allocations, JsonSerializer.Serialize(f.Data.TransactionAllocations)); Validation.Validate(f.Data);
            var expense = f.Entry(September, new(2026, 9, 10));
            f.Data.ReimbursementClaims.Add(new() { ExpenseTransactionId = expense.Id, Counterparty = "Partner", ExpectedAmount = 20 });
            var claims = JsonSerializer.Serialize(f.Data.ReimbursementClaims);
            Finance.ApplySuggestions(f.Data, expense); Equal<Guid?>(null, expense.RecurringPaymentId);
            Equal(claims, JsonSerializer.Serialize(f.Data.ReimbursementClaims)); Validation.Validate(f.Data);
        });
        yield return ("Legacy transfers are excluded from recognition and history application", () =>
        {
            var f = new Fixture(); f.Rule(); var t = f.Entry(); t.Kind = TransactionKind.Transfer; t.CounterAccountId = f.OtherAccount.Id;
            Finance.ApplySuggestions(f.Data, t); Equal("", t.RecognizedName); Equal(0, TransactionRecognition.PreviewHistory(f.Data, Today).Count);
            Validation.Validate(f.Data);
        });
        yield return ("Planning edits and recognition edits share one record and saving settings does not touch history", () =>
        {
            var f = new Fixture(); var plan = f.Plan(); var t = f.Entry(); var ruleId = f.Data.Rules.Single().Id;
            var state = State(f.Data); var draft = EditorDraft.Recurring(state, plan);
            Field(draft, "name", "Updated insurance"); Field(draft, "text", "01051832244");
            var before = JsonSerializer.Serialize(f.Data); var bankBefore = JsonSerializer.Serialize(t);
            var proposal = draft.PrepareSave(f.Data, Today);
            Equal(before, JsonSerializer.Serialize(f.Data)); True(proposal.ReviewRecognition); Equal(1, proposal.Changes.Count);
            Equal(0, proposal.InitialSelection.Count); proposal.SaveOnly(f.Data);
            Equal(bankBefore, JsonSerializer.Serialize(f.Data.Transactions.Single())); Equal(1, f.Data.Rules.Count);
            Equal(ruleId, f.Data.Rules[0].Id); Equal(plan.Id, f.Data.RecurringPayments[0].Id);
            Equal("Updated insurance", f.Data.Rules[0].Name); Equal("Updated insurance", f.Data.RecurringPayments[0].Name);
            var fromTransaction = EditorDraft.RecognitionFromTransaction(state, t);
            Equal<Guid?>(ruleId, fromTransaction.RecognitionRuleId);
            var apply = fromTransaction.PrepareSave(f.Data, Today); Equal(1, apply.InitialSelection.Count);
            apply.SaveSelected!(f.Data, apply.InitialSelection);
            Equal("Updated insurance", f.Data.Transactions[0].RecognizedName); Equal<Guid?>(plan.Id, f.Data.Transactions[0].RecurringPaymentId);
            Equal(1, f.Data.Rules.Count); Validation.Validate(f.Data);
        });
        yield return ("A recognition setup can add optional planning without duplicating its matching rule", () =>
        {
            WithCultures(() =>
            {
                var f = new Fixture(); var t = f.Entry(); var draft = EditorDraft.Rule(State(f.Data), seed: t);
                Equal("", draft.Text("text")); // Require an explicit stable fragment, not the full dated bank text.
                Field(draft, "text", "01051832244"); Field(draft, "name", "Insurance X");
                var amount = draft.Fields.Single(x => x.Key == "amount"); True(!draft.IsFieldVisible(amount));
                Field(draft, "planning", "new"); True(draft.IsFieldVisible(amount));
                var proposal = draft.PrepareSave(f.Data, Today); proposal.SaveSelected!(f.Data, proposal.InitialSelection);
                Equal(1, f.Data.Rules.Count); Equal(1, f.Data.RecurringPayments.Count);
                Equal<Guid?>(f.Data.RecurringPayments[0].Id, f.Data.Rules[0].RecurringPaymentId);
                Equal<Guid?>(f.Data.RecurringPayments[0].Id, f.Data.Transactions[0].RecurringPaymentId); Validation.Validate(f.Data);
            });
        });
        yield return ("Removing optional planning keeps recognition and historical readable names", () =>
        {
            var f = new Fixture(); var plan = f.Plan(); var t = f.Entry(); RecurringSchedule.Link(f.Data, t.Id, plan.Id, new(2026, 11, 10));
            t = f.Data.Transactions.Single();
            HouseholdEdits.DeleteRecurring(f.Data, plan.Id);
            Equal(0, f.Data.RecurringPayments.Count); Equal(1, f.Data.Rules.Count); Equal<Guid?>(null, f.Data.Rules[0].RecurringPaymentId);
            Equal(plan.Name, t.RecognizedName); Equal<Guid?>(null, t.RecurringPaymentId);
            var future = f.Entry(September, new(2026, 9, 10)); Finance.ApplySuggestions(f.Data, future); Equal(plan.Name, future.RecognizedName);
            Categorization.DeleteCategory(f.Data, f.Insurance.Id); Equal(1, f.Data.Rules.Count); Equal<Guid?>(null, f.Data.Rules[0].CategoryId);
            Validation.Validate(f.Data);
        });
        yield return ("Legacy normalization and exact linked examples survive without learning changing digits", () =>
        {
            var f = new Fixture(); var plan = f.Plan(); plan.MatchText = "AXA NV 01051832244";
            HouseholdEdits.SaveRecurring(f.Data, plan);
            var punctuated = f.Entry("AXA-NV / 01051832244", new(2026, 9, 10)); Finance.ApplySuggestions(f.Data, punctuated);
            Equal(plan.Name, punctuated.RecognizedName);
            var example = f.Entry("Completely different text 202609", new(2026, 8, 10));
            RecurringSchedule.Link(f.Data, example.Id, plan.Id, new(2026, 8, 10));
            var exact = f.Entry(example.Description, new(2026, 10, 10)); var changed = f.Entry("Completely different text 202611");
            Finance.ApplySuggestions(f.Data, [exact, changed]); Equal(plan.Name, exact.RecognizedName); Equal("", changed.RecognizedName);
        });
        yield return ("Schema 15 automatically migrates 283 rules, dozens of plans and two years of payments without rewriting history", () => InDirectory(directory =>
        {
            var f = LargeLegacy(); var path = Path.Combine(directory, "huishoudboek.db"); new SqliteHouseholdStore(path).Save(f.Data);
            var transactionJson = JsonSerializer.Serialize(f.Data.Transactions); var rules = f.Data.Rules.Select(r => r with { }).ToArray();
            var plans = JsonSerializer.Serialize(f.Data.RecurringPayments); var balance = Finance.Balance(f.Data, f.Account, Today);
            var financialDetails = JsonSerializer.Serialize(new { f.Data.TransactionAllocations, f.Data.ReimbursementClaims, f.Data.ImportBatches, f.Data.AccountStatementPeriods });
            Sql(path, RemoveSchema16Columns + "PRAGMA user_version=15;");
            using (var coordinator = new AdministrationCoordinator(directory, claim: true, allowUpgrade: true))
            {
                var loaded = coordinator.Read().Data;
                Equal(transactionJson, JsonSerializer.Serialize(loaded.Transactions)); Equal(plans, JsonSerializer.Serialize(loaded.RecurringPayments));
                Equal(283 + 32, loaded.Rules.Count); True(rules.SequenceEqual(loaded.Rules.Take(283)));
                Equal(balance, Finance.Balance(loaded, loaded.Accounts[0], Today)); Equal(32, loaded.Transactions.Count(t => t.RecurringPaymentId != null));
                True(loaded.Transactions.All(t => t.RecognizedName == "" && t.RecognitionRuleId == null));
                Equal(financialDetails, JsonSerializer.Serialize(new { loaded.TransactionAllocations, loaded.ReimbursementClaims, loaded.ImportBatches, loaded.AccountStatementPeriods }));
                True(BankImportCoverage.IsCurrent(loaded, loaded.AccountStatementPeriods.Single()));
                Equal(32, TransactionRecognition.PreviewHistory(loaded, Today).Count);
                Equal(transactionJson, JsonSerializer.Serialize(loaded.Transactions));
                Equal(f.OtherCategory.Id, new RecognitionMatcher(loaded).Category(new Transaction { AccountId = f.Account.Id, Description = November, Amount = -100 })!.CategoryId!.Value);
            }
            var backup = Directory.GetFiles(directory, "*.voor-versie-18-*.db").Single();
            Equal(15L, Scalar(backup, "PRAGMA user_version")); Equal(283L, Scalar(backup, "SELECT count(*) FROM CategorizationRule"));
            var first = new SqliteHouseholdStore(path, allowCreate: false).Load(); var second = new SqliteHouseholdStore(path, allowCreate: false).Load();
            True(first.Rules.Select(r => r.Id).SequenceEqual(second.Rules.Select(r => r.Id)));
            Equal(1, Directory.GetFiles(directory, "*.voor-versie-18-*.db").Length);
            Equal(transactionJson, JsonSerializer.Serialize(second.Transactions));
            var restored = new SqliteHouseholdStore(Path.Combine(directory, "restored.db")); restored.Restore(backup);
            Equal(transactionJson, JsonSerializer.Serialize(restored.Load().Transactions)); Equal(315, restored.Load().Rules.Count);
            Equal(283L, Scalar(backup, "SELECT count(*) FROM CategorizationRule")); // Restore never modifies its source.
        }));
        yield return ("Failed schema migration rolls back changes and retains its pre-upgrade backup", () => InDirectory(directory =>
        {
            var f = new Fixture(); f.Rule(); var path = Path.Combine(directory, "old.db"); new SqliteHouseholdStore(path).Save(f.Data);
            Sql(path, RemoveSchema16Columns.Replace("ALTER TABLE CategorizationRule DROP COLUMN Name;", "", StringComparison.Ordinal) + "PRAGMA user_version=15;");
            try { _ = new SqliteHouseholdStore(path, allowCreate: false); throw new Exception("Expected migration failure."); }
            catch (SqliteException) { }
            Equal(15L, Scalar(path, "PRAGMA user_version"));
            Equal(0L, Scalar(path, "SELECT count(*) FROM pragma_table_info('Transaction') WHERE name='RecognizedName'"));
            Equal(1, Directory.GetFiles(directory, "*.voor-versie-18-*.db").Length);
        }));
        yield return ("Recognition names, rule scopes and provenance survive SQLite backup, restore and CSV export", () => InDirectory(directory =>
        {
            var f = new Fixture(); var rule = f.Rule(); var t = f.Entry(); Finance.ApplySuggestions(f.Data, t);
            var store = new SqliteHouseholdStore(Path.Combine(directory, "source.db")); store.Save(f.Data);
            var backup = Path.Combine(directory, "backup.db"); store.Backup(backup);
            var restored = new SqliteHouseholdStore(Path.Combine(directory, "restored.db")); restored.Restore(backup);
            Equal(rule, restored.Load().Rules.Single()); Equal(t, restored.Load().Transactions.Single());
            t.RecognizedName = "=unsafe()";
            var csvPath = Path.Combine(directory, "export.csv"); ImportService.Export(csvPath, f.Data);
            using var reader = new StreamReader(csvPath); using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.GetCultureInfo("nl-NL")) { Delimiter = ";" });
            csv.Read(); csv.ReadHeader(); csv.Read(); Equal(November, csv.GetField("Omschrijving")); Equal("'=unsafe()", csv.GetField("Herkende naam"));
            Equal(rule.Id.ToString(), csv.GetField("Herkenningsregel-ID"));
        }));
        yield return ("Import preview and commit carry recognized names while honoring an explicitly empty category", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var f = new Fixture(); var plan = f.Plan(); var initial = coordinator.Read();
            var snapshot = coordinator.Replace(initial.Revision, f.Data);
            using var imports = new ImportWorkspace(directory);
            using var source = new MemoryStream(Encoding.UTF8.GetBytes($"Date;Description;Amount\n2026-09-10;{September};-100.00\n2026-11-10;{November};-123.45\n"));
            var upload = imports.UploadAsync(source, "bank.csv", CancellationToken.None).GetAwaiter().GetResult();
            var profile = new ImportProfile { Name = "Test", DateColumn = "Date", DescriptionColumn = "Description", AmountColumn = "Amount", CultureName = "en-US", DateFormat = "yyyy-MM-dd" };
            var preview = imports.Preview(new(upload.UploadId, f.Account.Id, new(2026, 9, 1), Today, profile), snapshot);
            True(preview.Rows.All(r => r.RecognizedName == plan.Name)); Equal<Guid?>(plan.Id, preview.Rows[0].RecurringPaymentId);
            Equal<Guid?>(null, preview.Rows[1].RecurringPaymentId); True(preview.Rows[1].HasPlannedIdentity);
            var selected = preview.Rows.Select(r => new ImportSelection(r.RowNumber, null, null)).ToArray();
            imports.Commit(new(preview.PreviewId, selected), preview.Revision, coordinator, Today);
            var saved = coordinator.Read().Data;
            Equal(2, saved.Transactions.Count); True(saved.Transactions.All(t => t.RecognizedName == plan.Name && t.CategoryId == null));
            Equal(1, saved.Transactions.Count(t => t.RecurringPaymentId != null)); Equal(September, saved.Transactions[0].Description);
        }));
        yield return ("Recognition history updates retain coordinator revision protection", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true); var f = new Fixture(); f.Rule(); f.Entry();
            var initial = coordinator.Read(); var preview = coordinator.Replace(initial.Revision, f.Data);
            var changes = TransactionRecognition.PreviewHistory(preview.Data, Today); Equal(1, changes.Count);
            coordinator.Update(preview.Revision, d => d.Accounts[0].Name = "Changed elsewhere");
            Throws(() => coordinator.Update(preview.Revision, d => TransactionRecognition.ApplyHistory(d, changes.Select(c => c.Before.Id).ToHashSet(), Today)));
            Equal("", coordinator.Read().Data.Transactions[0].RecognizedName);
        }));
        yield return ("Recognition management and desktop/mobile transactions render readable names and original bank text in both languages", () =>
        {
            WithCultures(() =>
            {
                var f = new Fixture(); f.Rule(); var t = f.Entry(); Finance.ApplySuggestions(f.Data, t);
                var state = State(f.Data); state.Year = 2026; state.Month = 11;
                var html = Render<Transactions>(state); True(html.Contains("Insurance X")); True(html.Contains(November));
                True(html.Contains(CultureInfo.CurrentUICulture.Name == "nl-NL" ? "Herkennen als…" : "Recognize as…"));
                var management = Render<Recognition>(state); True(management.Contains("01051832244")); True(!management.Contains("RecognitionCompatibilityHelp"));
                var editor = Render<Editor>(state, new Dictionary<string, object?> { [nameof(Editor.Draft)] = EditorDraft.RecognitionFromTransaction(state, t) });
                True(editor.Contains(November)); True(!editor.Contains("RecognitionEditorNote")); True(!editor.Contains("OptionalPlanning"));
            });
        });
        yield return ("Recognition localization keys are complete and equal in Dutch and English", () =>
        {
            var root = FindRoot();
            var nl = XDocument.Load(Path.Combine(root, "huishoudboek/source/src/Huishoudboek.Web/UiResources.resx"));
            var en = XDocument.Load(Path.Combine(root, "huishoudboek/source/src/Huishoudboek.Web/UiResources.en.resx"));
            var keys = nl.Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToList();
            Equal(keys.Count, keys.Distinct().Count()); True(keys.Order().SequenceEqual(en.Root!.Elements("data").Select(e => (string)e.Attribute("name")!).Order()));
        });
    }

    private sealed class Fixture
    {
        public HouseholdData Data { get; } = HouseholdData.CreateEmpty();
        public Account Account { get; }
        public Account OtherAccount { get; }
        public Category Insurance => Data.Categories.Single(c => c.Name == "Verzekeringen");
        public Category OtherCategory => Data.Categories.Single(c => c.Name == "Overig");
        public Fixture()
        {
            var owner = new Owner { Name = "Owner" }; Data.Owners.Add(owner);
            Account = new() { Name = "Current", OwnerIds = [owner.Id], OpeningDate = new(2024, 1, 1), OpeningBalance = 10000 };
            OtherAccount = Account with { Id = Guid.NewGuid(), Name = "Other" }; Data.Accounts.AddRange([Account, OtherAccount]);
        }
        public CategorizationRule Rule()
        {
            var rule = new CategorizationRule { Name = "Insurance X", Contains = "01051832244", CategoryId = Insurance.Id,
                AccountId = Account.Id, Direction = PaymentDirection.Outgoing };
            Data.Rules.Add(rule); return rule;
        }
        public RecurringPayment Plan(string name = "AXA – Car insurance")
        {
            var plan = new RecurringPayment { Name = name, AccountId = Account.Id, CategoryId = Insurance.Id, Amount = -100,
                DayOfMonth = 10, StartDate = new(2024, 1, 1), MatchText = "01051832244", MatchTolerance = 1, MatchWindowDays = 3 };
            HouseholdEdits.SaveRecurring(Data, plan); return Data.RecurringPayments.Single(p => p.Id == plan.Id);
        }
        public Transaction Entry(string description = November, DateOnly? date = null, decimal amount = -100)
        {
            var t = new Transaction { AccountId = Account.Id, Description = description, Date = date ?? new(2026, 11, 10), Amount = amount };
            Data.Transactions.Add(t); return t;
        }
    }

    private static Fixture LargeLegacy()
    {
        var f = new Fixture();
        f.Data.Rules.Add(new() { Contains = "AXA", CategoryId = f.OtherCategory.Id });
        f.Data.Rules.Add(new() { Contains = "01051832244", CategoryId = f.Insurance.Id });
        for (var i = 2; i < 283; i++) f.Data.Rules.Add(new() { Contains = $"Legacy supplier {i}", CategoryId = f.Insurance.Id });
        for (var i = 0; i < 730; i++) f.Entry($"Original bank text {i}", new DateOnly(2024, 11, 1).AddDays(i), -(i % 90 + 1));
        for (var i = 0; i < 32; i++)
        {
            var plan = new RecurringPayment { Name = $"Plan {i}", MatchText = $"Policy {i}", AccountId = f.Account.Id,
                Amount = -100, CategoryId = f.Insurance.Id, StartDate = new(2024, 1, 1), DayOfMonth = 10 };
            f.Data.RecurringPayments.Add(plan);
            var linked = f.Entry($"Policy {i} 2026-10", new(2026, 10, 10)); linked.RecurringPaymentId = plan.Id; linked.RecurringOccurrenceDate = linked.Date;
        }
        f.Data.TransactionAllocations.Add(new() { TransactionId = f.Data.Transactions[0].Id, Amount = -.40m, CategoryId = f.Insurance.Id });
        f.Data.TransactionAllocations.Add(new() { TransactionId = f.Data.Transactions[0].Id, Amount = -.60m, CategoryId = f.OtherCategory.Id });
        f.Data.ReimbursementClaims.Add(new() { ExpenseTransactionId = f.Data.Transactions[1].Id, Counterparty = "Partner", ExpectedAmount = 1 });
        var batch = new ImportBatch { AccountId = f.Account.Id, FileName = "Two years.xlsx", RowCount = f.Data.Transactions.Count,
            CoverageFrom = new(2024, 11, 1), CoverageThrough = Today };
        f.Data.ImportBatches.Add(batch); foreach (var t in f.Data.Transactions) t.ImportBatchId = batch.Id;
        BankImportCoverage.ConfirmAccountPeriod(f.Data, f.Account.Id, new(2024, 11, 1), Today, Today);
        Validation.Validate(f.Data); return f;
    }

    private static WebState State(HouseholdData data)
    {
        var state = new WebState(null!); state.Data.Owners = data.Owners; state.Data.Accounts = data.Accounts; state.Data.Categories = data.Categories;
        state.Data.InsightSettings = data.InsightSettings;
        state.Data.Transactions = data.Transactions; state.Data.Rules = data.Rules; state.Data.RecurringPayments = data.RecurringPayments;
        state.Data.TransactionAllocations = data.TransactionAllocations; state.Data.ReimbursementClaims = data.ReimbursementClaims;
        return state;
    }
    private static void Field(EditorDraft draft, string key, string value) => draft.Fields.Single(f => f.Key == key).Value = value;
    private static string Render<T>(WebState state, IDictionary<string, object?>? parameters = null) where T : IComponent
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization(); services.AddSingleton(state); services.AddSingleton<IJSRuntime>(new NoJs());
        using var provider = services.BuildServiceProvider();
        return Run().GetAwaiter().GetResult();
        async Task<string> Run()
        {
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()))).ToHtmlString()));
        }
    }
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "huishoudboek/config.yaml"))) directory = directory.Parent;
        return directory?.FullName ?? throw new Exception("Repository root not found.");
    }
    private static void Sql(string path, string sql)
    { using var c = new SqliteConnection($"Data Source={path};Pooling=False"); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    private static long Scalar(string path, string sql)
    { using var c = new SqliteConnection($"Data Source={path};Pooling=False"); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); }
    private static void WithCultures(Action run)
    {
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try { foreach (var name in new[] { "nl-NL", "en-GB" }) { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name); run(); } }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
    }
    private static void InDirectory(Action<string> run)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "recognition-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path); try { run(path); } finally { Directory.Delete(path, recursive: true); }
    }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Throws(Action run)
    { try { run(); } catch (InvalidOperationException) { return; } throw new Exception("Expected rejection."); }
}
