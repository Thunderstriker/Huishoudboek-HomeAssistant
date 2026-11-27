using System.Text;
using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;

internal static class ImportDuplicateTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);
    private static readonly ImportProfile Profile = new()
    {
        Name = "Duplicate regression", DateColumn = "Date", DescriptionColumn = "Description", AmountColumn = "Amount",
        BalanceColumn = "Balance", DateFormat = "yyyy-MM-dd", CultureName = "en-US"
    };

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("Unrecognized statement headers can be mapped without creating a committable preview", () => InDirectory(directory =>
        {
            using var f = new ServiceFixture(directory);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Posted;Shop name;Total\n2026-10-25;Market;-10\n2026-11-05;Shop;-20\n"));
            var upload = f.Imports.UploadAsync(stream, "synthetic.csv", CancellationToken.None).GetAwaiter().GetResult();
            var before = f.Coordinator.Read();
            var request = new PreviewRequest(upload.UploadId, f.AccountId, DateOnly.MinValue, DateOnly.MaxValue, AllowIncompleteMapping: true);
            var incomplete = f.Imports.Preview(request, before);
            True(incomplete.RequiresMapping); Equal(Guid.Empty, incomplete.PreviewId);
            Equal("Posted", incomplete.Headers[0]); Equal(0, incomplete.Rows.Count);
            Throws(() => f.Imports.Commit(new(incomplete.PreviewId, [], true), before.Revision, f.Coordinator, Today));
            var mapped = f.Imports.Preview(request with { Profile = Profile with { DateColumn = "Posted", DescriptionColumn = "Shop name", AmountColumn = "Total", BalanceColumn = "" } }, before);
            True(!mapped.RequiresMapping);
            Equal<DateOnly?>(new(2026, 10, 25), mapped.Rows[0].Date);
            Equal<DateOnly?>(new(2026, 11, 5), mapped.Rows[1].Date);
            Equal(before.Revision, f.Coordinator.Read().Revision);
            Equal(2, f.Commit(mapped).Count);
            True(f.Coordinator.Read().Data.ImportBatches.Single().CoverageFrom == null);
        }));
        yield return ("Reimporting a statement without running balances adds no transactions or empty history", () => InDirectory(directory =>
        {
            using var f = new ServiceFixture(directory);
            var csv = Csv("2026-11-05;Shop;-100;", "2026-11-07;Salary;1000;");
            var first = f.Preview(csv); Equal(2, first.Rows.Count(r => r.Include)); f.Commit(first);
            var before = f.Coordinator.Read(); var transactions = JsonSerializer.Serialize(before.Data.Transactions);
            var again = f.Preview(csv);
            True(again.Rows.All(r => !r.Include && r.DuplicateStatus == ImportDuplicateStatus.AlreadyImported));
            var result = f.Commit(again); Equal(0, result.Count); Equal(Guid.Empty, result.BatchId);
            var saved = f.Coordinator.Read().Data;
            Equal(transactions, JsonSerializer.Serialize(saved.Transactions)); Equal(1, saved.ImportBatches.Count);
            Equal(1900m, Finance.Balance(saved, saved.Accounts.Single(), Today));
            Equal(100m, Reporting.Month(saved, 2026, 11, Today).Outflow);
        }));
        yield return ("Overlapping exports preserve additional identical payments by occurrence", () => InDirectory(directory =>
        {
            using var f = new ServiceFixture(directory);
            var row = "2026-11-05;Shop;-10;";
            var first = f.Preview(Csv(row, row)); True(first.Rows.All(r => r.Include)); f.Commit(first);
            var overlap = f.Preview(Csv(row, row, row, "2026-11-10;Other shop;-20;"));
            Equal(2, overlap.Rows.Count(r => r.DuplicateStatus == ImportDuplicateStatus.AlreadyImported));
            Equal(2, overlap.Rows.Count(r => r.Include));
            Equal(2, overlap.Rows.Where(r => r.MatchedTransactionId != null).Select(r => r.MatchedTransactionId).Distinct().Count());
            f.Commit(overlap);
            var saved = f.Coordinator.Read().Data; Equal(4, saved.Transactions.Count);
            Equal(3, saved.Transactions.Count(t => t.Description == "Shop"));
            var again = f.Preview(Csv(row, row, row, "2026-11-10;Other shop;-20;"));
            True(again.Rows.All(r => r.DuplicateStatus == ImportDuplicateStatus.AlreadyImported && !r.Include));
        }));
        yield return ("Running balances distinguish identical payments and reversed exports", () =>
        {
            var f = new DataFixture(); var one = f.Existing(balance: 990); var two = f.Existing(balance: 980);
            var rows = f.Preview(("Shop", -10, 980m), ("Shop", -10, 990m), ("Shop", -10, 970m));
            Equal<Guid?>(two.Id, rows[0].MatchedTransactionId); Equal<Guid?>(one.Id, rows[1].MatchedTransactionId);
            Equal(ImportDuplicateStatus.New, rows[2].DuplicateStatus); True(rows[2].Include);
        });
        yield return ("Specific balance matches are reserved before matching rows with missing balances", () =>
        {
            var f = new DataFixture(); var specific = f.Existing(balance: 990); var other = f.Existing(balance: 980);
            var rows = f.Preview(("Shop", -10, null), ("Shop", -10, 990m));
            Equal<Guid?>(other.Id, rows[0].MatchedTransactionId); Equal<Guid?>(specific.Id, rows[1].MatchedTransactionId);
            True(rows.All(r => !r.Include));
        });
        yield return ("Missing balances can match earlier imports but conflicting known balances cannot", () =>
        {
            var f = new DataFixture(); f.Existing();
            Equal(ImportDuplicateStatus.AlreadyImported, f.Preview(("Shop", -10, 990m)).Single().DuplicateStatus);
            f = new DataFixture(); f.Existing(balance: 990);
            Equal(ImportDuplicateStatus.AlreadyImported, f.Preview(("Shop", -10, null)).Single().DuplicateStatus);
            Equal(ImportDuplicateStatus.New, f.Preview(("Shop", -10, 980m)).Single().DuplicateStatus);
        });
        yield return ("Duplicate matching is scoped to the source account, signed amount and bank date", () =>
        {
            var f = new DataFixture(); var other = new Account { Name = "Other", OwnerIds = f.Account.OwnerIds, OpeningDate = f.Account.OpeningDate };
            f.Data.Accounts.Add(other); f.Existing().AccountId = other.Id;
            True(f.Preview(("Shop", -10, null)).Single().Include);
            f = new DataFixture(); f.Existing().Amount = 10;
            True(f.Preview(("Shop", -10, null)).Single().Include);
            f = new DataFixture(); f.Existing().Date = Today.AddDays(-1);
            True(f.Preview(("Shop", -10, null)).Single().Include);
        });
        yield return ("Manual entries and changed bank text require review without automatic selection", () =>
        {
            var f = new DataFixture(); var manual = f.Existing(); manual.ImportBatchId = null;
            var row = f.Preview(("Shop", -10, null)).Single();
            Equal(ImportDuplicateStatus.NeedsReview, row.DuplicateStatus); True(!row.Include); Equal<Guid?>(manual.Id, row.MatchedTransactionId);
            f = new DataFixture(); f.Existing("Shop  branch");
            row = f.Preview(("SHOP branch", -10, null)).Single(); Equal(ImportDuplicateStatus.NeedsReview, row.DuplicateStatus); True(!row.Include);
            f = new DataFixture(); f.Existing("Shop", balance: 990);
            row = f.Preview(("Different bank text", -10, 990m)).Single(); Equal(ImportDuplicateStatus.NeedsReview, row.DuplicateStatus); True(!row.Include);
            f = new DataFixture(); f.Existing("Invoice 20261101");
            True(f.Preview(("Invoice 20261102", -10, null)).Single().Include);
        });
        yield return ("Undone imports and user labels are not used as bank identity", () =>
        {
            var f = new DataFixture(); var entry = f.Existing("Actual bank text"); entry.UserDescription = "Shop"; entry.RecognizedName = "Shop";
            True(f.Preview(("Shop", -10, null)).Single().Include);
            f = new DataFixture(); f.Existing(); f.Data.ImportBatches.Single().Undone = true;
            True(f.Preview(("Shop", -10, null)).Single().Include);
        });
        yield return ("Duplicate previews and category overrides never modify existing entries or splits", () => InDirectory(directory =>
        {
            using var f = new ServiceFixture(directory);
            f.Commit(f.Preview(Csv("2026-11-05;Shop;-100;900")));
            f.Coordinator.Update(f.Coordinator.Read().Revision, data =>
            {
                var entry = data.Transactions.Single(); entry.UserDescription = "Keep my note"; entry.CategoryId = data.Categories[0].Id;
                data.TransactionAllocations.Add(new() { TransactionId = entry.Id, Amount = -100, CategoryId = data.Categories[1].Id });
            });
            var before = f.Coordinator.Read(); var snapshot = JsonSerializer.Serialize(before.Data);
            var preview = f.Preview(Csv("2026-11-05;Shop;-100;900"));
            Equal(snapshot, JsonSerializer.Serialize(f.Coordinator.Read().Data));
            var adjusted = f.Imports.Adjust(new(preview.PreviewId, preview.Rows[0].RowNumber, null, before.Data.Categories[2].Id, true));
            Equal(ImportDuplicateStatus.AlreadyImported, adjusted.DuplicateStatus); True(!adjusted.Include);
            Equal(preview.Rows[0].MatchedTransactionId, adjusted.MatchedTransactionId);
            f.Commit(preview);
            var saved = f.Coordinator.Read().Data;
            Equal(JsonSerializer.Serialize(before.Data.Transactions), JsonSerializer.Serialize(saved.Transactions));
            Equal(JsonSerializer.Serialize(before.Data.TransactionAllocations), JsonSerializer.Serialize(saved.TransactionAllocations));
        }));
        yield return ("API refuses selected existing rows without an explicit duplicate override and keeps failed imports atomic", () => InDirectory(directory =>
        {
            using var f = new ServiceFixture(directory); var csv = Csv("2026-11-05;Shop;-10;"); f.Commit(f.Preview(csv));
            var preview = f.Preview(csv); var before = f.Coordinator.Read();
            var selection = new ImportSelection(preview.Rows[0].RowNumber, null, null);
            Throws(() => f.Imports.Commit(new(preview.PreviewId, [selection]), preview.Revision, f.Coordinator, Today));
            Equal(before.Revision, f.Coordinator.Read().Revision);
            Equal(JsonSerializer.Serialize(before.Data), JsonSerializer.Serialize(f.Coordinator.Read().Data));
            var result = f.Imports.Commit(new(preview.PreviewId, [selection with { AllowDuplicate = true }]), preview.Revision, f.Coordinator, Today);
            Equal(1, result.Count); Equal(2, f.Coordinator.Read().Data.Transactions.Count);
        }));
        yield return ("Reimported and overlapping statements can confirm coverage without selecting old rows", () => InDirectory(directory =>
        {
            using var f = new ServiceFixture(directory); var csv = Csv("2026-11-05;Shop;-10;"); f.Commit(f.Preview(csv));
            var again = f.Preview(csv); var result = f.Commit(again, coverage: true);
            Equal(0, result.Count); True(result.BatchId != Guid.Empty);
            var saved = f.Coordinator.Read().Data; Equal(1, saved.Transactions.Count);
            True(BankImportCoverage.Covers(saved, f.AccountId, new(2026, 11, 1), Today));
            var overlap = f.Preview(Csv("2026-11-05;Shop;-10;", "2026-11-15;New shop;-20;"));
            Equal(1, f.Commit(overlap, coverage: true).Count);
            saved = f.Coordinator.Read().Data; Equal(2, saved.Transactions.Count);
            True(BankImportCoverage.Covers(saved, f.AccountId, new(2026, 11, 1), Today));
        }));
        yield return ("Coverage rejects skipped new, uncertain and invalid rows even when matching rows exist", () => InDirectory(directory =>
        {
            using var f = new ServiceFixture(directory); f.Commit(f.Preview(Csv("2026-11-05;Shop;-10;")));
            foreach (var extra in new[] { "2026-11-15;New;-20;", "2026-11-05;SHOP;-10;", "invalid;Invalid;-10;" })
            {
                var preview = f.Preview(Csv("2026-11-05;Shop;-10;", extra)); var before = f.Coordinator.Read();
                Throws(() => f.Imports.Commit(new(preview.PreviewId, [], true), preview.Revision, f.Coordinator, Today));
                Equal(before.Revision, f.Coordinator.Read().Revision);
                True(!BankImportCoverage.Covers(f.Coordinator.Read().Data, f.AccountId, new(2026, 11, 1), Today));
            }
        }));
        yield return ("Deleting or undoing original rows reopens coverage confirmed by a later duplicate-only import", () =>
        {
            foreach (var undo in new[] { false, true }) InDirectory(directory =>
            {
                using var f = new ServiceFixture(directory); var csv = Csv("2026-11-05;Shop;-10;");
                var original = f.Commit(f.Preview(csv)); f.Commit(f.Preview(csv), coverage: true);
                True(BankImportCoverage.Covers(f.Coordinator.Read().Data, f.AccountId, new(2026, 11, 1), Today));
                f.Coordinator.Update(f.Coordinator.Read().Revision, data =>
                {
                    if (undo) HouseholdEdits.UndoImport(data, original.BatchId);
                    else HouseholdEdits.DeleteTransaction(data, data.Transactions.Single().Id);
                });
                var saved = f.Coordinator.Read().Data; Equal(0, saved.Transactions.Count);
                True(!BankImportCoverage.Covers(saved, f.AccountId, new(2026, 11, 1), Today));
            });
        });
        yield return ("Empty statements still confirm quiet coverage but skipped new rows cannot finish as a no-op", () => InDirectory(directory =>
        {
            using var f = new ServiceFixture(directory);
            var empty = f.Preview(Csv()); Equal(0, f.Commit(empty, coverage: true).Count);
            True(BankImportCoverage.Covers(f.Coordinator.Read().Data, f.AccountId, new(2026, 11, 1), Today));
            var preview = f.Preview(Csv("2026-11-05;Shop;-10;"));
            Throws(() => f.Imports.Commit(new(preview.PreviewId, []), preview.Revision, f.Coordinator, Today));
        }));
        yield return ("An import undone after saving can be imported again and stale previews remain protected", () => InDirectory(directory =>
        {
            using var f = new ServiceFixture(directory); var csv = Csv("2026-11-05;Shop;-10;"); var result = f.Commit(f.Preview(csv));
            var stale = f.Preview(csv);
            f.Coordinator.Update(f.Coordinator.Read().Revision, data => HouseholdEdits.UndoImport(data, result.BatchId));
            Throws(() => f.Commit(stale));
            var fresh = f.Preview(csv); True(fresh.Rows.Single().Include); f.Commit(fresh);
            Equal(1, f.Coordinator.Read().Data.Transactions.Count);
        }));
        yield return ("Import review keeps known skipped duplicates out of exceptions and permits coverage", () =>
        {
            var source = new PreviewRow(1, Today, "Shop", -10, null, true, false, "Ready", null, null, "Normal",
                DuplicateStatus: ImportDuplicateStatus.AlreadyImported);
            var review = new ImportReviewRow(source); True(!review.NeedsReview(Today)); True(review.AccountsForCoverage);
            review.Include = true; True(review.NeedsReview(Today));
            review = new(source with { DuplicateStatus = ImportDuplicateStatus.NeedsReview });
            True(review.NeedsReview(Today)); True(!review.AccountsForCoverage);
            review.Include = true; True(review.AccountsForCoverage); True(review.NeedsReview(Today));
            review = new(source with { IsValid = false }); True(!review.AccountsForCoverage);
        });
        yield return ("Large duplicate statements use occurrence counts without collapsing identical payments", () =>
        {
            var f = new DataFixture(); const int count = 10_000;
            for (var i = 0; i < count; i++) f.Existing();
            var rows = f.Preview(Enumerable.Repeat(("Shop", -10m, (decimal?)null), count + 1).ToArray());
            Equal(count, rows.Count(r => r.DuplicateStatus == ImportDuplicateStatus.AlreadyImported));
            Equal(1, rows.Count(r => r.Include));
        });
    }

    private static string Csv(params string[] rows) => "Date;Description;Amount;Balance\n" + string.Join("\n", rows) + "\n";
    private sealed class DataFixture
    {
        public HouseholdData Data { get; } = HouseholdData.CreateEmpty();
        public Account Account { get; }
        private readonly ImportBatch batch;
        public DataFixture()
        {
            var owner = new Owner { Name = "Synthetic household" }; Data.Owners.Add(owner);
            Account = new() { Name = "Synthetic bank", OwnerIds = [owner.Id], OpeningDate = new(2026, 1, 1), OpeningBalance = 1000 };
            Data.Accounts.Add(Account); batch = new() { AccountId = Account.Id, FileName = "synthetic.csv" }; Data.ImportBatches.Add(batch);
        }
        public Transaction Existing(string description = "Shop", decimal? balance = null)
        {
            var entry = new Transaction { AccountId = Account.Id, Date = Today, Description = description, Amount = -10,
                StatementBalance = balance, ImportBatchId = batch.Id };
            Data.Transactions.Add(entry); batch.RowCount++; return entry;
        }
        public IReadOnlyList<ImportRow> Preview(params (string Description, decimal Amount, decimal? Balance)[] entries)
        {
            var table = new ImportTable(["Date", "Description", "Amount", "Balance"], entries.Select(e => (IReadOnlyList<ImportCell>)new ImportCell[]
            {
                new("2026-11-23", Date: Today), new(e.Description), new("", Number: e.Amount),
                e.Balance is { } balance ? new(balance.ToString(System.Globalization.CultureInfo.InvariantCulture), Number: balance) : new("")
            }).ToList());
            return ImportService.Preview(table, Profile, Account.Id, new(2026, 11, 1), Today, Data);
        }
    }
    private sealed class ServiceFixture : IDisposable
    {
        public AdministrationCoordinator Coordinator { get; }
        public ImportWorkspace Imports { get; }
        public Guid AccountId { get; }
        public ServiceFixture(string directory)
        {
            Coordinator = new(directory, initialize: true); Imports = new(directory);
            var data = new DataFixture().Data; data.ImportBatches.Clear(); AccountId = data.Accounts.Single().Id;
            Coordinator.Replace(Coordinator.Read().Revision, data);
        }
        public PreviewResponse Preview(string csv)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var upload = Imports.UploadAsync(stream, "synthetic.csv", CancellationToken.None).GetAwaiter().GetResult();
            return Imports.Preview(new(upload.UploadId, AccountId, new(2026, 11, 1), Today, Profile), Coordinator.Read());
        }
        public CommitImportResponse Commit(PreviewResponse preview, bool coverage = false) => Imports.Commit(
            new(preview.PreviewId, preview.Rows.Where(r => r.Include).Select(r => new ImportSelection(r.RowNumber, r.CategoryId, null)).ToArray(), coverage),
            preview.Revision, Coordinator, Today);
        public void Dispose() { Imports.Dispose(); Coordinator.Dispose(); }
    }
    private static void InDirectory(Action<string> run)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", "import-duplicate-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { run(directory); } finally { Directory.Delete(directory, recursive: true); }
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Throws(Action run) { try { run(); } catch (InvalidOperationException) { return; } throw new Exception("Expected validation/conflict failure."); }
}
