using System.Globalization;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Microsoft.Data.Sqlite;

var tests = new (string Name, Action Run)[]
{
    ("Receipt bank search keeps initial choices bounded but searches all older matches", () =>
    {
        var entries = Enumerable.Range(0, 100).Select(i => new Transaction
        {
            Date = new DateOnly(2026, 1, 1).AddDays(i), Description = "Shop", Amount = -12.34m
        }).ToList();
        foreach (var query in new[] { "", "  \t " })
        {
            var initial = ReceiptBankSearch.Find(entries, query, null).ToList();
            Equal(80, initial.Count); Equal(entries[^1].Id, initial[0].Id);
            True(initial.All(t => t.Id != entries[0].Id));
        }
        var matches = ReceiptBankSearch.Find(entries, "shop", null).ToList();
        Equal(100, matches.Count); Equal(entries[0].Id, matches[^1].Id);
        Equal(entries[0].Id, ReceiptBankSearch.Find(entries, "01-01-2026", null).Single().Id);
        Equal(80, ReceiptBankSearch.Find(entries, "", null).Count());
    }),
    ("Receipt bank search combines text, month, year and culture-independent amounts", () =>
    {
        var january = new Transaction { Date = new(2026, 1, 15), Description = "My Shop", Amount = -12.34m };
        var entries = new[] { january, january with { Id = Guid.NewGuid(), Date = new(2025, 1, 15) },
            january with { Id = Guid.NewGuid(), Date = new(2026, 2, 15) } };
        foreach (var query in new[] { "  SHOP januari 2026 12,34 ", "January shop 2026 12.34", "15-01-2026", "2026-01-15" })
            Equal(january.Id, ReceiptBankSearch.Find(entries, query, null).Single().Id);
        Equal(0, ReceiptBankSearch.Find(entries, "unknown", null).Count());
    }),
    ("Receipt bank search preserves the selected older entry without widening eligibility", () =>
    {
        var selected = new Transaction { Date = new(2026, 1, 1), Description = "Old shop", Amount = -10 };
        var recent = Enumerable.Range(0, 90).Select(i => new Transaction
        { Date = new DateOnly(2026, 6, 1).AddDays(i), Description = "Recent", Amount = -20 }).ToList();
        var eligible = recent.Append(selected).ToList();
        var initial = ReceiptBankSearch.Find(eligible, "", selected.Id).ToList();
        Equal(80, initial.Count); Equal(selected.Id, initial[0].Id);
        var matches = ReceiptBankSearch.Find(eligible, "Recent", selected.Id).ToList();
        Equal(91, matches.Count); Equal(selected.Id, matches[0].Id);
        True(!ReceiptBankSearch.Find(recent, "", selected.Id).Any(t => t.Id == selected.Id));
    }),
    ("Receipt decimals preserve each keystroke and update totals immediately", () =>
    {
        var input = new ReceiptNumberDraft(null, CultureInfo.GetCultureInfo("nl-NL"));
        var item = new PurchaseReceiptItem { Name = "Product" };
        foreach (var (text, expected) in new (string, decimal)[] { ("1", 1), ("12", 12), ("12,", 12), ("12,3", 12.3m), ("12,34", 12.34m) })
        {
            input.Text = text; item.Amount = input.Value;
            True(input.IsValid); Equal(text, input.Text); Equal(expected, PurchaseReceipts.ItemTotal([item]));
        }
        foreach (var text in new[] { "-", "-,", "not a number", "12,345" })
        {
            input.Text = text; item.Amount = input.Value;
            True(!input.IsValid); Equal(text, input.Text); Equal<decimal?>(null, item.Amount);
        }
        input.Text = "-1,25"; item.Amount = input.Value;
        True(input.IsValid); Equal(-1.25m, PurchaseReceipts.ItemTotal([item]));
    }),
    ("Receipt decimals accept comma or dot independently of the display language", () =>
    {
        foreach (var language in new[] { "nl-NL", "en-GB" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            var input = new ReceiptNumberDraft(12.34m, culture);
            Equal(12.34m.ToString(culture), input.Text);
            foreach (var (text, expected) in new (string, decimal)[] { ("12,34", 12.34m), ("12.34", 12.34m), (" 12,34 ", 12.34m), (".5", .5m), (",5", .5m) })
            {
                input.Text = text; True(input.IsValid); Equal(expected, input.Value!.Value);
            }
            foreach (var text in new[] { "1.234,56", "1,234.56", "1 234,56", "1e2", "EUR 12", "1,2,3", "1000000000.01", "999999999999999999999999999999" })
            { input.Text = text; True(!input.IsValid); Equal<decimal?>(null, input.Value); }
        }
    }),
    ("Receipt number ranges and precision match monetary and weight fields", () =>
    {
        var culture = CultureInfo.InvariantCulture;
        var quantity = new ReceiptNumberDraft(null, culture, decimalPlaces: 4, minimum: .0001m, maximum: 1_000_000m);
        var price = new ReceiptNumberDraft(null, culture, decimalPlaces: 4, minimum: 0);
        var total = new ReceiptNumberDraft(null, culture, minimum: 0);
        foreach (var text in new[] { "0.0001", "1,2345", "1000000" }) { quantity.Text = text; True(quantity.IsValid); }
        foreach (var text in new[] { "0", "-1", "0.00001", "1000000.0001" }) { quantity.Text = text; True(!quantity.IsValid); }
        foreach (var text in new[] { "0", "1.2345", "1000000000" }) { price.Text = text; True(price.IsValid); }
        foreach (var text in new[] { "-0.01", "1.23456", "1000000000.0001" }) { price.Text = text; True(!price.IsValid); }
        foreach (var text in new[] { "0", "1.23", "1000000000" }) { total.Text = text; True(total.IsValid); }
        foreach (var text in new[] { "-0.01", "1.234", "1000000000.01" }) { total.Text = text; True(!total.IsValid); }
    }),
    ("Cleared receipt numbers remain optional and cleared voucher payment means zero", () =>
    {
        var amount = new ReceiptNumberDraft(25, CultureInfo.InvariantCulture) { Text = "" };
        var vouchers = new ReceiptNumberDraft(10, CultureInfo.InvariantCulture, minimum: 0, emptyAsZero: true) { Text = "" };
        True(amount.IsValid); Equal<decimal?>(null, amount.Value);
        True(vouchers.IsValid); Equal<decimal?>(0m, vouchers.Value);
        vouchers.Text = "-"; True(!vouchers.IsValid); Equal<decimal?>(null, vouchers.Value);
        vouchers.Text = "5,25"; True(vouchers.IsValid); Equal<decimal?>(5.25m, vouchers.Value);
        var receipt = new PurchaseReceipt { Total = 25, MealVoucherAmount = vouchers.Value!.Value };
        Equal<decimal?>(19.75m, PurchaseReceipts.AmountDue(receipt));
        amount.Text = "26.50"; receipt.Total = amount.Value;
        Equal<decimal?>(21.25m, PurchaseReceipts.AmountDue(receipt));
    }),
    ("Receipt metadata does not change financial totals", () =>
    {
        var f = new Fixture(); f.Save(false);
        Equal(40m, f.Report().Outflow); Equal(0m, MealVouchers.Balance(f.Data, Fixture.Today));
    }),
    ("Mixed receipt counts full categories, not twice", () =>
    {
        var f = new Fixture(); f.Credit(); var original = JsonSerializer.Serialize(f.Bank); f.Save();
        Equal(160m, f.Report().Income); Equal(100m, f.Report().Outflow);
        Equal(90m, f.Spent(f.Food)); Equal(10m, f.Spent(f.House));
        Equal(100m, MealVouchers.Balance(f.Data, Fixture.Today));
        Equal(960m, Finance.Balance(f.Data, f.Account, Fixture.Today)); Equal(original, JsonSerializer.Serialize(f.Bank));
    }),
    ("Different voucher quantities and values share one balance", () =>
    {
        var f = new Fixture(); f.Credit(); f.Credit(18, 10); f.Save();
        Equal(340m, f.Report().Income); Equal(280m, MealVouchers.Balance(f.Data, Fixture.Today));
    }),
    ("Opening balance is not income", () =>
    {
        var f = new Fixture(); f.Credit(1, 200, opening: true); f.Save();
        Equal(0m, f.Report().Income); Equal(140m, MealVouchers.Balance(f.Data, Fixture.Today));
        f.Credit(1, 1, opening: true); Throws(() => Validation.Validate(f.Data));
    }),
    ("Fully voucher-paid receipts need no bank or account", () =>
    {
        var f = new Fixture(100); f.Data.Accounts.Clear(); f.Receipt.AccountId = null; f.Credit(); f.Save();
        Equal(100m, f.Report().Outflow); Equal(60m, MealVouchers.Balance(f.Data, Fixture.Today));
        Equal(0, f.Data.Transactions.Count); Equal(0, f.Data.TransactionAllocations.Count);
    }),
    ("Bank-only receipt uses categories without extra expense", () =>
    {
        var f = new Fixture(0); f.Save(); Equal(100m, f.Report().Outflow); Equal(90m, f.Spent(f.Food));
        Equal(0, MealVouchers.Lines(f.Data).Count());
    }),
    ("Repeated application preserves amounts and allocation IDs", () =>
    {
        var f = new Fixture(); f.Credit(); f.Save();
        var ids = f.Data.TransactionAllocations.Select(a => a.Id).ToArray();
        f.Save(); f.Save(); Equal(100m, f.Report().Outflow); Equal(100m, MealVouchers.Balance(f.Data, Fixture.Today));
        True(ids.SequenceEqual(f.Data.TransactionAllocations.Select(a => a.Id)));
    }),
    ("Unapplying and deleting restore original bank categorisation", () =>
    {
        var f = new Fixture(); f.Credit(); f.Save(); f.Save(false);
        Equal(40m, f.Report().Outflow); Equal(160m, MealVouchers.Balance(f.Data, Fixture.Today));
        Equal(0, f.Data.TransactionAllocations.Count);
        f.Save(); PurchaseReceipts.Delete(f.Data, f.Receipt.Id); Validation.Validate(f.Data);
        Equal(40m, f.Report().Outflow); Equal(160m, MealVouchers.Balance(f.Data, Fixture.Today));
    }),
    ("Bank deletion detaches receipt and releases voucher payment", () =>
    {
        var f = new Fixture(); f.Credit(); f.Save(); HouseholdEdits.DeleteTransaction(f.Data, f.Bank.Id);
        Validation.Validate(f.Data); True(!f.Data.PurchaseReceipts.Single().IsApplied);
        Equal(160m, MealVouchers.Balance(f.Data, Fixture.Today)); Equal(0m, f.Report().Outflow);
    }),
    ("Unreviewed and mismatching receipts cannot be applied", () =>
    {
        var f = new Fixture(); f.Receipt.IsApplied = true; f.Receipt.IsReviewed = false;
        Throws(() => PurchaseReceipts.Save(f.Data, f.Receipt, f.Items));
        f = new Fixture(); f.Bank.Amount = -41; Throws(() => f.Save());
        f = new Fixture(100); f.Receipt.TransactionId = Guid.NewGuid(); Throws(() => f.Save());
    }),
    ("Existing manual splits and duplicate applied receipts are protected", () =>
    {
        var f = new Fixture(); f.Data.TransactionAllocations.Add(new() { TransactionId = f.Bank.Id, Amount = -40, CategoryId = f.Food.Id });
        Throws(() => f.Save());
        f = new Fixture(); f.Save();
        var duplicate = f.Receipt with { Id = Guid.NewGuid() };
        Throws(() => PurchaseReceipts.Save(f.Data, duplicate, f.Items.Select(i => i with { Id = Guid.NewGuid() }).ToList()));
    }),
    ("Negative category totals cannot generate fictitious funding", () =>
    {
        var f = new Fixture(); f.Items.Clear();
        f.Items.Add(new() { Name = "Food", Amount = 110, CategoryId = f.Food.Id });
        f.Items.Add(new() { Name = "Discount", Kind = ReceiptLineKind.Discount, Amount = -10, CategoryId = f.House.Id });
        Throws(() => f.Save());
    }),
    ("Product discounts follow product categories", () =>
    {
        var f = new Fixture(); f.Receipt.Total = 90; f.Bank.Amount = -30;
        f.Items.Add(new() { Name = "Discount", Amount = -10, Kind = ReceiptLineKind.Discount, RelatedItemId = f.Items[0].Id });
        f.Save(); Equal(80m, f.Spent(f.Food)); Equal(10m, f.Spent(f.House));
    }),
    ("Rounding reconciles all payment and category totals", () =>
    {
        var random = new Random(237);
        for (var n = 0; n < 150; n++)
        {
            var food = random.Next(1, 10000) / 100m; var house = random.Next(1, 10000) / 100m;
            var total = food + house; var vouchers = random.Next(0, (int)(total * 100) + 1) / 100m;
            var f = new Fixture(vouchers); f.Receipt.Total = total;
            f.Items.Clear(); f.Items.Add(new() { Name = "Food", Amount = food, CategoryId = f.Food.Id });
            f.Items.Add(new() { Name = "House", Amount = house, CategoryId = f.House.Id });
            f.Data.Transactions.Clear(); f.Receipt.TransactionId = null;
            if (total > vouchers) { f.Bank.Amount = -(total - vouchers); f.Data.Transactions.Add(f.Bank); f.Receipt.TransactionId = f.Bank.Id; }
            f.Save(); Equal(total, f.Report().Outflow); Equal(food, f.Spent(f.Food)); Equal(house, f.Spent(f.House));
            Equal(vouchers, MealVouchers.Lines(f.Data).Sum(l => l.Spending));
            Equal(total - vouchers, -f.Data.TransactionAllocations.Sum(a => a.Amount));
        }
    }),
    ("Owner filters exclude shared credits and voucher-funded costs", () =>
    {
        var f = new Fixture(); f.Credit(); f.Save(); var owner = f.Data.Owners.Single().Id;
        Equal(100m, DetailedReporting.Dashboard(f.Data, 2026, 11, Fixture.Today).Report.Outflow);
        var scoped = DetailedReporting.Dashboard(f.Data, 2026, 11, Fixture.Today, owner).Report;
        Equal(40m, scoped.Outflow); Equal(0m, scoped.Income);
        Equal(100m, HomeOverview.Create(f.Data, 2026, 11, Fixture.Today).Month.Outflow);
        Equal(40m, HomeOverview.Create(f.Data, 2026, 11, Fixture.Today, owner).Month.Outflow);
    }),
    ("Monthly, annual, budget and category-history totals agree", () =>
    {
        var f = new Fixture(); f.Credit(); f.Save();
        Equal(100m, Reporting.Month(f.Data, 2026, 11, Fixture.Today).Outflow);
        Equal(100m, DetailedReporting.Year(f.Data, 2026, Fixture.Today).Outflow);
        Equal(90m, DetailedReporting.Budgets(f.Data, 2026, 11, Fixture.Today).Single(c => c.Id == f.Food.Id).Spent);
        Equal(90m, CategoryHistory.Create(f.Data, f.Food.Id, Fixture.Today, Fixture.Today, Fixture.Today).Total);
    }),
    ("Reporting follows bank date while voucher balance follows purchase date", () =>
    {
        var f = new Fixture(); f.Receipt.PurchaseDate = new(2026, 10, 31); f.Save();
        Equal(0m, Finance.Report(f.Data, 2026, 10, Fixture.Today).Outflow); Equal(100m, f.Report().Outflow);
        Equal(-60m, MealVouchers.Balance(f.Data, new(2026, 10, 31)));
    }),
    ("Financial insights include complete receipt costs but exclude vouchers from reserves", () =>
    {
        var f = new Fixture(); f.Bank.Date = new(2026, 10, 5); f.Receipt.PurchaseDate = f.Bank.Date;
        f.Credit(date: new DateOnly(2026, 10, 1)); f.Save();
        var insights = FinancialInsights.Create(f.Data, Fixture.Today);
        var october = insights.Months.Single(m => m.Month == new DateOnly(2026, 10, 1));
        Equal(100m, october.Spending); Equal(160m, october.Income); Equal(960m, insights.Freedom.LiquidBalance);
        var owner = FinancialInsights.Create(f.Data, Fixture.Today, f.Data.Owners.Single().Id);
        Equal(40m, owner.Months.Single(m => m.Month == october.Month).Spending);
    }),
    ("Future credits and receipts are not actual income or spending", () =>
    {
        var f = new Fixture(100); f.Receipt.PurchaseDate = Fixture.Today.AddDays(2);
        f.Credit(date: Fixture.Today.AddDays(2)); f.Save();
        Equal(0m, f.Report().Income); Equal(0m, f.Report().Outflow);
        Equal(160m, f.Report().Forecast.Income); Equal(100m, f.Report().Forecast.Outflow);
        Equal(0m, MealVouchers.Balance(f.Data, Fixture.Today));
    }),
    ("Negative balances do not invent income or bank cash", () =>
    {
        var f = new Fixture(); var before = CashFlow.Project(f.Data, Fixture.Today).Accounts.Single(); f.Save();
        var after = CashFlow.Project(f.Data, Fixture.Today).Accounts.Single();
        Equal(-60m, MealVouchers.Balance(f.Data, Fixture.Today)); Equal(0m, f.Report().Income);
        Equal(before.CurrentBalance, after.CurrentBalance); Equal(before.DailyVariableEstimate, after.DailyVariableEstimate);
        Equal(before.EstimatedSpendable, after.EstimatedSpendable);
    }),
    ("Receipt-funded bank splits may be adjusted within receipt category amounts", () =>
    {
        var f = new Fixture(); f.Save();
        TransactionAccounting.SaveAllocations(f.Data, f.Bank.Id, f.Data.TransactionAllocations.Select(a => a with { Amount = a.CategoryId == f.Food.Id ? -30 : -10 }).ToList());
        Equal(90m, f.Spent(f.Food)); Equal(10m, f.Spent(f.House));
        Equal(0m, MealVouchers.Lines(f.Data).Where(l => l.CategoryId == f.House.Id).Sum(l => l.Spending));
        Throws(() => TransactionAccounting.SaveAllocations(f.Data, f.Bank.Id, f.Data.TransactionAllocations.Select(a => a with { Amount = a.CategoryId == f.Food.Id ? -20 : -20 }).ToList()));
        Equal(100m, f.Report().Outflow);
    }),
    ("Refunds preserve full expense accounting and protect dependent splits", () =>
    {
        var f = new Fixture(); f.Save(); var split = f.Data.TransactionAllocations.Single(a => a.CategoryId == f.Food.Id);
        var refund = new Transaction { AccountId = f.Account.Id, Date = Fixture.Today, Description = "Refund", Amount = 10 };
        f.Data.Transactions.Add(refund);
        TransactionAccounting.SaveAllocations(f.Data, refund.Id, [new() { Amount = 10, Treatment = AllocationTreatment.Refund, OriginalTransactionId = f.Bank.Id, OriginalAllocationId = split.Id }]);
        Equal(90m, f.Report().Outflow); Equal(0m, f.Report().Income);
        Throws(() => PurchaseReceipts.Delete(f.Data, f.Receipt.Id));
        f.Save(); Equal(split.Id, f.Data.TransactionAllocations.Single(a => a.TransactionId == f.Bank.Id && a.CategoryId == f.Food.Id).Id);
    }),
    ("Deleting receipt categories preserves full bill and funding IDs", () =>
    {
        var f = new Fixture(); f.Save(); var ids = f.Data.TransactionAllocations.Select(a => a.Id).ToArray();
        Categorization.DeleteCategory(f.Data, f.House.Id); Validation.Validate(f.Data);
        Equal(100m, f.Spent(f.Food)); True(ids.SequenceEqual(f.Data.TransactionAllocations.Select(a => a.Id)));
        Categorization.DeleteCategory(f.Data, f.Food.Id); Validation.Validate(f.Data);
        Equal(100m, f.Report().Categories.Single(c => c.Id == Guid.Empty).Spent);
    }),
    ("Undoing an import detaches applied receipts", () =>
    {
        var f = new Fixture(); var batch = new ImportBatch { AccountId = f.Account.Id, RowCount = 1 };
        f.Data.ImportBatches.Add(batch); f.Bank.ImportBatchId = batch.Id; f.Save();
        HouseholdEdits.UndoImport(f.Data, batch.Id); Validation.Validate(f.Data);
        Equal(0m, f.Report().Outflow); Equal(0m, MealVouchers.Balance(f.Data, Fixture.Today));
        Equal(1, f.Data.PurchaseReceipts.Count); True(!f.Data.PurchaseReceipts.Single().IsApplied);
    }),
    ("Voucher input rejects invalid amounts and counts", () =>
    {
        foreach (var credit in new[] { new MealVoucherCredit { Quantity = 0, UnitValue = 8 }, new() { Quantity = 1, UnitValue = -1 }, new() { Quantity = 1, UnitValue = 8.001m } })
        { var f = new Fixture(); f.Data.MealVoucherCredits.Add(credit); Throws(() => Validation.Validate(f.Data)); }
    }),
    ("SQLite backup and restore preserve vouchers, splits and attachments", () => WithDirectory(directory =>
    {
        var f = new Fixture(); f.Credit(); f.Save();
        var receipt = f.Data.PurchaseReceipts.Single(); receipt.DocumentName = "receipt.pdf"; receipt.DocumentContentType = "application/pdf";
        var document = ReceiptDocuments.Create(receipt.Id, receipt.DocumentName, "%PDF-1.4 test fixture"u8.ToArray());
        var store = new SqliteHouseholdStore(Path.Combine(directory, "source.db")); store.Save(f.Data, document);
        var backup = Path.Combine(directory, "backup.db"); store.Backup(backup);
        var restored = new SqliteHouseholdStore(Path.Combine(directory, "restored.db")); restored.Restore(backup);
        var loaded = restored.Load(); Equal(100m, Finance.Report(loaded, 2026, 11, Fixture.Today).Outflow);
        Equal(100m, MealVouchers.Balance(loaded, Fixture.Today)); Equal(1, loaded.MealVoucherCredits.Count);
        True(restored.ReadReceiptDocument(receipt.Id)!.Content.SequenceEqual(document.Content));
    })),
    ("Schemas 11, 12 and 13 migrate with safe defaults and safety backups", () =>
    {
        foreach (var version in new[] { 11, 12, 13 }) WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "legacy.db"); var f = new Fixture(); f.Save(false);
            var storedReceipt = f.Data.PurchaseReceipts.Single(); storedReceipt.DocumentName = "legacy.pdf"; storedReceipt.DocumentContentType = "application/pdf";
            var document = ReceiptDocuments.Create(storedReceipt.Id, storedReceipt.DocumentName, "%PDF-1.4 legacy fixture"u8.ToArray());
            var store = new SqliteHouseholdStore(path); store.Save(f.Data, document);
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open(); using var command = connection.CreateCommand();
                command.CommandText = RecognitionTests.RemoveSchema16Columns + "DROP TABLE InvestmentTrade; DROP TABLE InvestmentInstrument; DROP TABLE MealVoucherCredit; ALTER TABLE PurchaseReceipt DROP COLUMN IsApplied;";
                if (version < 13) command.CommandText += "ALTER TABLE PurchaseReceipt DROP COLUMN MealVoucherAmount; ALTER TABLE PurchaseReceipt DROP COLUMN DefaultCategoryId;";
                if (version < 12) command.CommandText += "DROP TABLE PurchaseReceipt; DROP TABLE PurchaseReceiptItem; DROP TABLE receipt_documents;";
                command.CommandText += $"PRAGMA user_version={version};"; command.ExecuteNonQuery();
            }
            var previous = SqliteHouseholdStore.ReadBackup(path); Equal(0, previous.MealVoucherCredits.Count);
            True(previous.PurchaseReceipts.All(r => !r.IsApplied));
            var upgraded = new SqliteHouseholdStore(path, allowCreate: false); var loaded = upgraded.Load();
            Equal(40m, Finance.Report(loaded, 2026, 11, Fixture.Today).Outflow);
            Equal(version >= 12 ? 1 : 0, loaded.PurchaseReceipts.Count);
            if (version >= 12) True(upgraded.ReadReceiptDocument(storedReceipt.Id)!.Content.SequenceEqual(document.Content));
            Equal(1, Directory.GetFiles(directory, "*.voor-versie-16-*.db").Length);
            var restored = new SqliteHouseholdStore(Path.Combine(directory, "restored.db"));
            restored.Restore(Directory.GetFiles(directory, "*.voor-versie-16-*.db").Single());
            Equal(40m, Finance.Report(restored.Load(), 2026, 11, Fixture.Today).Outflow);
        });
    }),
    ("CSV includes non-cash income and voucher expense exactly once", () => WithDirectory(directory =>
    {
        var f = new Fixture(); f.Credit(); f.Save(); var path = Path.Combine(directory, "export.csv"); ImportService.Export(path, f.Data);
        using var reader = new StreamReader(path); using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.GetCultureInfo("nl-NL")) { Delimiter = ";" });
        csv.Read(); csv.ReadHeader(); decimal income = 0, spending = 0; var count = 0;
        while (csv.Read()) { var amount = csv.GetField<decimal>("Bedrag"); income += Math.Max(0, amount); spending += Math.Max(0, -amount); count++; }
        Equal(3, count); Equal(160m, income); Equal(100m, spending);
    })),
    ("Historical voucher credit can be saved before an unfinished purchase", () => WithDirectory(directory =>
    {
        using var coordinator = new AdministrationCoordinator(directory, initialize: true);
        var f = new Fixture(); f.Receipt.PurchaseDate = new(2026, 1, 15); f.Bank.Date = f.Receipt.PurchaseDate.Value;
        var start = coordinator.Read();
        var purchaseRevision = coordinator.Update(start.Revision, d =>
        { d.Owners = f.Data.Owners; d.Accounts = f.Data.Accounts; d.Categories = f.Data.Categories; d.Transactions = f.Data.Transactions; }).Revision;
        var credit = new MealVoucherCredit { Date = new(2026, 1, 1), Quantity = 20, UnitValue = 8 };
        var credited = coordinator.Update(purchaseRevision, d => MealVouchers.SaveCredit(d, credit));
        Equal(0, credited.Data.PurchaseReceipts.Count);
        Equal(160m, MealVouchers.Balance(credited.Data, f.Receipt.PurchaseDate.Value));
        Throws(() => coordinator.SaveReceipt(purchaseRevision, f.Receipt, f.Items, false, null));
        purchaseRevision = credited.Revision;
        f.Receipt.IsApplied = true;
        var saved = coordinator.SaveReceipt(purchaseRevision, f.Receipt, f.Items, false, null);
        Equal(100m, MealVouchers.Balance(saved.Data, f.Receipt.PurchaseDate.Value));
        Equal(credit.Id, saved.Data.MealVoucherCredits.Single().Id);
    })),
    ("Inline voucher saves preserve conflict protection and remain separate from purchases", () => WithDirectory(directory =>
    {
        using var coordinator = new AdministrationCoordinator(directory, initialize: true);
        var f = new Fixture(); var start = coordinator.Read();
        var snapshot = coordinator.Update(start.Revision, d =>
        { d.Owners = f.Data.Owners; d.Accounts = f.Data.Accounts; d.Categories = f.Data.Categories; d.Transactions = f.Data.Transactions; });
        var credit = new MealVoucherCredit { Date = new(2026, 1, 1), Quantity = 20, UnitValue = 8, IsOpeningBalance = true };
        var credited = coordinator.Update(snapshot.Revision, d => MealVouchers.SaveCredit(d, credit));
        Throws(() => coordinator.Update(snapshot.Revision, d => MealVouchers.SaveCredit(d, credit with { Id = Guid.NewGuid(), IsOpeningBalance = false })));
        Equal(credited.Revision, coordinator.Read().Revision);
        Equal(1, coordinator.Read().Data.MealVoucherCredits.Count); Equal(0, coordinator.Read().Data.PurchaseReceipts.Count);
        Throws(() => coordinator.Update(credited.Revision, d => MealVouchers.SaveCredit(d, credit with { Id = Guid.NewGuid() })));
        Equal(credited.Revision, coordinator.Read().Revision);
        coordinator.Update(credited.Revision, d => MealVouchers.SaveCredit(d, new() { Date = new(2026, 12, 1), UnitValue = 8 }));
        Throws(() => coordinator.SaveReceipt(credited.Revision, f.Receipt, f.Items, false, null));
        var current = coordinator.Read();
        Equal(0, current.Data.PurchaseReceipts.Count);
        Equal(160m, MealVouchers.Balance(current.Data, f.Receipt.PurchaseDate!.Value));
        Equal(0m, Finance.Report(current.Data, 2026, 1, Fixture.Today).Income);
    })),
    ("Coordinator rejects stale and invalid receipt changes atomically", () => WithDirectory(directory =>
    {
        using var coordinator = new AdministrationCoordinator(directory, initialize: true);
        var f = new Fixture(); f.Credit(); var start = coordinator.Read();
        var snapshot = coordinator.Update(start.Revision, d => { d.Owners = f.Data.Owners; d.Accounts = f.Data.Accounts; d.Categories = f.Data.Categories; d.Transactions = f.Data.Transactions; d.MealVoucherCredits = f.Data.MealVoucherCredits; });
        f.Receipt.IsApplied = true;
        var saved = coordinator.SaveReceipt(snapshot.Revision, f.Receipt, f.Items, false, null);
        Throws(() => coordinator.SaveReceipt(snapshot.Revision, f.Receipt, f.Items, false, null));
        var bad = f.Receipt with { MealVoucherAmount = 59 };
        Throws(() => coordinator.SaveReceipt(saved.Revision, bad, f.Items, false, null));
        Equal(saved.Revision, coordinator.Read().Revision);
        Equal(100m, Finance.Report(coordinator.Read().Data, 2026, 11, Fixture.Today).Outflow);
    }))
}.Concat(InvestmentTests.All()).Concat(MortgageTests.All()).Concat(RecognitionTests.All()).ToArray();

var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} regression tests passed.");
return failed == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}
static void True(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
static void Throws(Action action)
{
    try { action(); } catch (InvalidOperationException) { return; }
    throw new Exception("Expected validation/conflict failure.");
}
static void WithDirectory(Action<string> test)
{
    var directory = Path.Combine(Path.GetTempPath(), "opencode", "huishoudboek-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { test(directory); } finally { Directory.Delete(directory, recursive: true); }
}

sealed class Fixture
{
    public static readonly DateOnly Today = new(2026, 11, 16);
    public HouseholdData Data { get; } = HouseholdData.CreateEmpty();
    public Account Account { get; }
    public Transaction Bank { get; }
    public Category Food { get; }
    public Category House { get; }
    public PurchaseReceipt Receipt { get; }
    public List<PurchaseReceiptItem> Items { get; } = [];

    public Fixture(decimal vouchers = 60)
    {
        var owner = new Owner { Name = "Test household" }; Data.Owners.Add(owner);
        Account = new() { Name = "Test bank", OwnerIds = [owner.Id], OpeningDate = new(2026, 1, 1), OpeningBalance = 1000 };
        Data.Accounts.Add(Account); Food = Data.Categories.Single(c => c.Name == "Voeding"); House = Data.Categories.Single(c => c.Name == "Huishouden");
        Bank = new() { AccountId = Account.Id, Date = new(2026, 11, 5), Description = "Test supermarket", Amount = -(100 - vouchers), CategoryId = Food.Id };
        if (vouchers < 100) Data.Transactions.Add(Bank);
        Receipt = new() { Merchant = "Test supermarket", PurchaseDate = new(2026, 11, 5), Total = 100, MealVoucherAmount = vouchers,
            DefaultCategoryId = Food.Id, TransactionId = vouchers < 100 ? Bank.Id : null, IsReviewed = true };
        for (var i = 0; i < 20; i++) Items.Add(new() { Name = "Food " + i, Amount = 4.50m });
        Items.Add(new() { Name = "Housekeeping", Amount = 10, CategoryId = House.Id });
    }
    public void Credit(int quantity = 20, decimal value = 8, bool opening = false, DateOnly? date = null) =>
        MealVouchers.SaveCredit(Data, new() { Date = date ?? new DateOnly(2026, 11, 1), Quantity = quantity, UnitValue = value, IsOpeningBalance = opening });
    public void Save(bool apply = true) { Receipt.IsApplied = apply; PurchaseReceipts.Save(Data, Receipt, Items); Validation.Validate(Data); }
    public MonthlyReport Report() => Finance.Report(Data, 2026, 11, Today);
    public decimal Spent(Category category) => Report().Categories.FirstOrDefault(c => c.Id == category.Id)?.Spent ?? 0;
}
