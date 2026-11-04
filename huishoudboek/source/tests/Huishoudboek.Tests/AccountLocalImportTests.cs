using System.Globalization;
using System.Text;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class AccountLocalImportTests
{
    private static readonly DateOnly Date = new(2026, 11, 3);
    private const decimal Amount = 27234.13m;
    private static readonly string[] Headers = ["Datum", "Omschrijving", "Bedrag", "Rekening opdrachtgever", "Rekening tegenpartij", "Saldo na verrichting"];

    private static HouseholdData CreateData()
    {
        var owner = new Owner { Name = "Eigenaar" };
        return new HouseholdData
        {
            Owners = [owner],
            Accounts = [
                new Account { Name = "Zichtrekening", Kind = AccountKind.Current, BankReference = "BE000000000001",
                    OpeningDate = Date.AddDays(-10), OpeningBalance = 100, OwnerIds = [owner.Id] },
                new Account { Name = "Spaarrekening", Kind = AccountKind.Savings, BankReference = "BE000000000002",
                    OpeningDate = Date.AddDays(-10), OpeningBalance = 50000, OwnerIds = [owner.Id] }
            ],
            Categories = [new Category { Name = "Notaris" }]
        };
    }

    private static ImportProfile Profile() => ImportService.SuggestProfile(Headers) with
    {
        // Old saved profiles must not reactivate automatic linking.
        CounterAccountReferenceColumn = "Rekening tegenpartij"
    };

    private static IReadOnlyList<ImportCell> Cells(Account account, string counterReference, decimal amount) =>
    [
        new("2026-11-03"), new("Notaris kosten"), new("", Number: amount), new(account.BankReference),
        new(counterReference), new("", Number: account.OpeningBalance + amount)
    ];

    private static ImportRow Preview(HouseholdData data, int accountIndex = 0, decimal amount = Amount, string? counterReference = null)
    {
        var account = data.Accounts[accountIndex];
        var table = new ImportTable(Headers, [Cells(account, counterReference ?? data.Accounts[1 - accountIndex].BankReference, amount)]);
        return Assert.Single(ImportService.Preview(table, Profile(), account.Id, Date, Date, data));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    [InlineData(1, -1)]
    public void OwnCounterpartyNeverChangesTheAccountOrAmount(int accountIndex, int sign)
    {
        var data = CreateData();
        var row = Preview(data, accountIndex, sign * Amount);
        var transaction = Assert.IsType<Transaction>(row.Transaction);

        Assert.True(row.IsValid);
        Assert.True(row.Include);
        Assert.Equal(data.Accounts[accountIndex].Id, transaction.AccountId);
        Assert.Equal(sign * Amount, transaction.Amount);
        Assert.Equal(transaction.Amount, row.Amount);
        Assert.Null(transaction.CounterAccountId);
        Assert.Equal(TransactionKind.Normal, transaction.Kind);
        Assert.Equal(0m, Finance.AccountEffect(transaction, data.Accounts[1 - accountIndex].Id));
        Assert.Single(data.Categories);
        Assert.Equal(2, row.CategoryChoices.Count);
        Assert.Empty(data.Transactions);
    }

    [Fact]
    public void CounterpartyOpeningDateAndAmbiguousReferencesAreIrrelevant()
    {
        var data = CreateData();
        data.Accounts[1].OpeningDate = Date.AddDays(1);
        data.Accounts.Add(data.Accounts[1] with { Id = Guid.NewGuid(), Name = "Andere spaarrekening" });

        Assert.True(Preview(data).Include);
        Assert.True(Preview(data, counterReference: data.Accounts[0].BankReference).Include);
    }

    [Fact]
    public void ACounterpartyColumnInAnOldProfileCanBeAbsent()
    {
        var data = CreateData();
        var table = new ImportTable(Headers, [Cells(data.Accounts[0], "", Amount)]);
        var profile = Profile() with { CounterAccountReferenceColumn = "Oude kolom die niet bestaat" };

        Assert.True(Assert.Single(ImportService.Preview(table, profile, data.Accounts[0].Id, Date, Date, data)).Include);
        Assert.Empty(ImportService.SuggestProfile(Headers).CounterAccountReferenceColumn);
    }

    [Fact]
    public void ExistingTransactionsNeverDeselectStatementRows()
    {
        var data = CreateData();
        var otherRow = Preview(data, 1).Transaction!;
        data.Transactions.Add(otherRow);
        Assert.True(Preview(data).Include);

        var ownRow = Preview(data).Transaction!;
        data.Transactions.Add(ownRow);
        var repeated = Preview(data);
        Assert.True(repeated.Include);
        Assert.Equal("Gereed", repeated.Note);
    }

    [Fact]
    public void ExistingLinkedTransferDoesNotSuppressAnotherAccountsStatement()
    {
        var data = CreateData();
        var transfer = new Transaction { AccountId = data.Accounts[1].Id, CounterAccountId = data.Accounts[0].Id,
            Kind = TransactionKind.Transfer, Date = Date, Amount = -Amount, Description = "Notaris kosten" };
        data.Transactions.Add(transfer);

        Assert.True(Preview(data).Include);
        Assert.Same(transfer, Assert.Single(data.Transactions));
    }

    [Fact]
    public void RepeatedRowsAreSelectedAndSourceAccountValidationStillWorks()
    {
        var data = CreateData();
        var cells = Cells(data.Accounts[0], data.Accounts[1].BankReference, Amount);
        var table = new ImportTable(Headers, [cells, cells]);
        var rows = ImportService.Preview(table, Profile(), data.Accounts[0].Id, Date, Date, data);
        Assert.True(rows[0].Include);
        Assert.True(rows[1].Include);
        Assert.True(rows[1].IsValid);
        Assert.Equal("Gereed", rows[1].Note);

        var wrongAccount = ImportService.Preview(table, Profile(), data.Accounts[1].Id, Date, Date, data);
        Assert.All(wrongAccount, row => { Assert.False(row.IsValid); Assert.False(row.Include); });
        data.Accounts[0].OpeningDate = Date.AddDays(1);
        Assert.False(Preview(data).IsValid);
    }

    [Fact]
    public void DateRangeInvalidRowsAndManualDeselectionArePreserved()
    {
        var data = CreateData();
        var table = new ImportTable(["Datum", "Omschrijving", "Bedrag"], [
            new ImportCell[] { new("2026-11-02"), new("Buiten periode"), new("-1,00") },
            new ImportCell[] { new("2026-11-03"), new("Betaling"), new("-1,00") },
            new ImportCell[] { new("2026-11-03"), new("Betaling"), new("-1,00") },
            new ImportCell[] { new("2026-11-03"), new("Ongeldig bedrag"), new("invalid") },
            new ImportCell[] { new("invalid"), new("Ongeldige datum"), new("-1,00") },
            new ImportCell[] { new("2026-11-04"), new("Buiten periode"), new("-1,00") }
        ]);
        var rows = ImportService.Preview(table, ImportService.SuggestProfile(table.Headers), data.Accounts[0].Id, Date, Date, data);

        Assert.Equal(new[] { 3, 4, 5, 6 }, rows.Select(r => r.RowNumber));
        Assert.All(rows.Take(2), row => Assert.True(row.Include));
        rows[1].Include = false;
        Assert.False(rows[1].Include);
        Assert.All(rows.Skip(2), row =>
        {
            Assert.False(row.IsValid);
            row.Include = true;
            Assert.False(row.Include);
        });
    }

    [Fact]
    public async Task ThousandRepeatedStatementRowsAreAllSelectedAndSavedEvenOnReimport()
    {
        using var fixture = new ImportFixture();
        var data = fixture.Coordinator.Read().Data;
        var current = data.Accounts[0];
        var savings = data.Accounts[1];
        const int rowCount = 1000;
        var culture = CultureInfo.GetCultureInfo("nl-NL");
        var csv = new StringBuilder().AppendLine(string.Join(';', Headers));
        for (var i = 1; i <= rowCount; i++)
            csv.AppendLine($"2026-11-03;Betaling;-1,00;{current.BankReference};{savings.BankReference};{(current.OpeningBalance - i).ToString(culture)}");

        for (var import = 1; import <= 2; import++)
        {
            var preview = await fixture.PreviewCsvAsync(current, csv.ToString());
            Assert.Equal(rowCount, preview.Rows.Count);
            Assert.Equal(rowCount, preview.Rows.Count(r => r.Include));
            Assert.All(preview.Rows, row =>
            {
                Assert.True(row.IsValid, row.Note);
                Assert.True(row.Include);
                Assert.Equal("Gereed", row.Note);
            });
            Assert.Equal((import - 1) * rowCount, fixture.Coordinator.Read().Data.Transactions.Count);
            var selections = preview.Rows.Where(r => r.Include)
                .Select(r => new ImportSelection(r.RowNumber, r.CategoryId, null)).ToArray();
            var result = fixture.Workspace.Commit(new(preview.PreviewId, selections), preview.Revision, fixture.Coordinator, Date);
            var saved = fixture.Coordinator.Read().Data;

            Assert.Equal(rowCount, result.Count);
            Assert.Equal(import * rowCount, saved.Transactions.Count);
            Assert.Equal(import * rowCount, saved.Transactions.Select(t => t.Id).Distinct().Count());
            Assert.Equal(rowCount, saved.ImportBatches.Single(b => b.Id == result.BatchId).RowCount);
            Assert.Equal(rowCount, saved.Transactions.Count(t => t.ImportBatchId == result.BatchId));
            Assert.All(saved.Transactions, t =>
            {
                Assert.Equal(current.Id, t.AccountId);
                Assert.Equal(Date, t.Date);
                Assert.Equal(-1m, t.Amount);
                Assert.Equal("Betaling", t.Description);
            });
            Assert.Equal(current.OpeningBalance - import * rowCount, Finance.Balance(saved, current, Date));
            Assert.Equal(savings.OpeningBalance, Finance.Balance(saved, savings, Date));
            Assert.Equal(StatementBalanceStatus.Available, result.Balance.Status);
            Assert.Equal(-(import - 1) * rowCount, result.Balance.Difference);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task CommitAndUndoOnlyChangeTheImportedAccount(int sign)
    {
        using var fixture = new ImportFixture();
        var data = fixture.Coordinator.Read().Data;
        var current = data.Accounts[0];
        var savings = data.Accounts[1];
        var preview = await fixture.PreviewAsync(current, savings, sign * Amount);
        var row = Assert.Single(preview.Rows);
        Assert.Equal(sign * Amount, row.Amount);
        Assert.Null(row.CounterAccountId);

        var result = fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, null, null)]),
            preview.Revision, fixture.Coordinator, Date);
        var saved = fixture.Coordinator.Read();
        var transaction = Assert.Single(saved.Data.Transactions);
        Assert.Equal(current.Id, transaction.AccountId);
        Assert.Equal(sign * Amount, transaction.Amount);
        Assert.Null(transaction.CounterAccountId);
        Assert.Null(transaction.CategoryId);
        Assert.Equal(result.BatchId, transaction.ImportBatchId);
        Assert.Equal(current.Id, Assert.Single(saved.Data.ImportBatches).AccountId);
        Assert.Equal(current.OpeningBalance + sign * Amount, Finance.Balance(saved.Data, current, Date));
        Assert.Equal(savings.OpeningBalance, Finance.Balance(saved.Data, savings, Date));
        Assert.Equal(StatementBalanceStatus.Available, result.Balance.Status);
        Assert.Equal(0m, result.Balance.Difference);

        var undone = fixture.Coordinator.Update(saved.Revision, d => HouseholdEdits.UndoImport(d, result.BatchId));
        Assert.Empty(undone.Data.Transactions);
        Assert.Equal(current.OpeningBalance, Finance.Balance(undone.Data, current, Date));
        Assert.Equal(savings.OpeningBalance, Finance.Balance(undone.Data, savings, Date));
    }

    [Fact]
    public async Task BothStatementsAreImportedIndependently()
    {
        using var fixture = new ImportFixture();
        var data = fixture.Coordinator.Read().Data;
        var current = data.Accounts[0];
        var savings = data.Accounts[1];
        foreach (var (account, counter, amount) in new[] { (current, savings, Amount), (savings, current, -Amount) })
        {
            var preview = await fixture.PreviewAsync(account, counter, amount);
            var row = Assert.Single(preview.Rows);
            Assert.True(row.Include);
            fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, null, null)]), preview.Revision, fixture.Coordinator, Date);
        }

        var saved = fixture.Coordinator.Read().Data;
        Assert.Equal(2, saved.Transactions.Count);
        Assert.All(saved.Transactions, t => Assert.Null(t.CounterAccountId));
        Assert.Equal(current.OpeningBalance + Amount, Finance.Balance(saved, current, Date));
        Assert.Equal(savings.OpeningBalance - Amount, Finance.Balance(saved, savings, Date));
    }

    [Fact]
    public async Task ConfirmedStatementPairsPreserveAccountLocalImportsAndAllowReimport()
    {
        using var fixture = new ImportFixture();
        var data = fixture.Coordinator.Read().Data;
        var current = data.Accounts[0];
        var savings = data.Accounts[1];
        foreach (var (account, counter, amount) in new[] { (current, savings, Amount), (savings, current, -Amount) })
        {
            var preview = await fixture.PreviewAsync(account, counter, amount);
            var row = Assert.Single(preview.Rows);
            fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, null, null)]), preview.Revision, fixture.Coordinator, Date);
        }
        var imported = fixture.Coordinator.Read();
        var debit = imported.Data.Transactions.Single(t => t.Amount < 0);
        var credit = imported.Data.Transactions.Single(t => t.Amount > 0);
        var matched = fixture.Coordinator.Update(imported.Revision, d => TransferMatching.Confirm(d, debit.Id, credit.Id, Date));
        Assert.Equal(imported.Data.Transactions, matched.Data.Transactions);
        Assert.Equal(current.OpeningBalance + Amount, Finance.Balance(matched.Data, current, Date));
        Assert.Equal(savings.OpeningBalance - Amount, Finance.Balance(matched.Data, savings, Date));
        var report = Finance.Report(matched.Data, Date.Year, Date.Month, Date);
        Assert.Equal((0m, 0m, -Amount), (report.Income, report.Outflow, report.Savings));
        foreach (var (account, counter, amount) in new[] { (current, savings, Amount), (savings, current, -Amount) })
        {
            var repeated = await fixture.PreviewAsync(account, counter, amount);
            Assert.True(Assert.Single(repeated.Rows).Include);
        }
        var undone = fixture.Coordinator.Update(matched.Revision, d => HouseholdEdits.UndoImport(d, credit.ImportBatchId!.Value));
        Assert.Empty(undone.Data.TransferMatches);
        Assert.Equal(debit, Assert.Single(undone.Data.Transactions));
        Assert.Equal(current.OpeningBalance, Finance.Balance(undone.Data, current, Date));
        Assert.Equal(savings.OpeningBalance - Amount, Finance.Balance(undone.Data, savings, Date));
    }

    [Fact]
    public async Task LegacyClientCannotLinkAnAccountThroughAdjustOrCommit()
    {
        using var fixture = new ImportFixture();
        var before = fixture.Coordinator.Read();
        var preview = await fixture.PreviewAsync(before.Data.Accounts[0], before.Data.Accounts[1], Amount);
        var row = Assert.Single(preview.Rows);
        var counterId = before.Data.Accounts[1].Id;

        Assert.Throws<InvalidOperationException>(() => fixture.Workspace.Adjust(new(preview.PreviewId, row.RowNumber, counterId, null, true)));
        Assert.Throws<InvalidOperationException>(() => fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, null, counterId)]),
            preview.Revision, fixture.Coordinator, Date));
        Assert.Equal(before.Revision, fixture.Coordinator.Read().Revision);
        Assert.Empty(fixture.Coordinator.Read().Data.Transactions);

        var adjusted = fixture.Workspace.Adjust(new(preview.PreviewId, row.RowNumber, null, before.Data.Categories[0].Id, true));
        Assert.Null(adjusted.CounterAccountId);
        fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, adjusted.CategoryId, null)]), preview.Revision, fixture.Coordinator, Date);
        Assert.Equal(adjusted.CategoryId, Assert.Single(fixture.Coordinator.Read().Data.Transactions).CategoryId);
    }

    [Fact]
    public async Task ADeliberateNullCategorySurvivesCommitSuggestions()
    {
        using var fixture = new ImportFixture();
        var before = fixture.Coordinator.Read();
        fixture.Coordinator.Update(before.Revision, data => data.Rules.Add(new CategorizationRule
        {
            Contains = "Notaris", CategoryId = data.Categories[0].Id
        }));
        var preview = await fixture.PreviewAsync(before.Data.Accounts[0], before.Data.Accounts[1], Amount);
        var row = Assert.Single(preview.Rows);
        Assert.NotNull(row.CategoryId);
        fixture.Workspace.Adjust(new(preview.PreviewId, row.RowNumber, null, null, true));
        fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, null, null)]), preview.Revision, fixture.Coordinator, Date);
        Assert.Null(Assert.Single(fixture.Coordinator.Read().Data.Transactions).CategoryId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecurringAndMortgageSuggestionsRemainAccountLocal(bool mortgage)
    {
        using var fixture = new ImportFixture();
        var before = fixture.Coordinator.Read();
        var seeded = fixture.Coordinator.Update(before.Revision, data =>
        {
            if (mortgage) data.Mortgage = new Mortgage { ReferencePrincipal = 100000, ReferenceDate = Date.AddDays(-1),
                MonthlyPayment = Amount, RemainingMonths = 12, OwnerIds = [data.Owners[0].Id] };
            foreach (var account in data.Accounts)
                data.RecurringPayments.Add(new RecurringPayment { AccountId = account.Id, Name = "Notaris",
                    Amount = -Amount, DayOfMonth = Date.Day, StartDate = Date.AddDays(-10), MatchText = "Notaris",
                    CategoryId = data.Categories[0].Id, IsMortgage = mortgage });
        });
        var preview = await fixture.PreviewAsync(seeded.Data.Accounts[0], seeded.Data.Accounts[1], -Amount);
        var row = Assert.Single(preview.Rows);
        fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, row.CategoryId, null)]), preview.Revision, fixture.Coordinator, Date);
        var saved = fixture.Coordinator.Read().Data;
        var transaction = Assert.Single(saved.Transactions);
        Assert.Equal(mortgage ? TransactionKind.Mortgage : TransactionKind.Normal, transaction.Kind);
        Assert.Equal(seeded.Data.RecurringPayments[0].Id, transaction.RecurringPaymentId);
        Assert.Equal(seeded.Data.Categories[0].Id, transaction.CategoryId);
        Assert.Equal(seeded.Data.Accounts[1].OpeningBalance, Finance.Balance(saved, saved.Accounts[1], Date));
    }

    [Fact]
    public async Task ImportDoesNotRewritePreviouslySavedLinkedTransfers()
    {
        using var fixture = new ImportFixture();
        var before = fixture.Coordinator.Read();
        var transfer = new Transaction { AccountId = before.Data.Accounts[1].Id, CounterAccountId = before.Data.Accounts[0].Id,
            Kind = TransactionKind.Transfer, Date = Date, Amount = -Amount, Description = "Notaris kosten" };
        fixture.Coordinator.Update(before.Revision, data => data.Transactions.Add(transfer));
        var preview = await fixture.PreviewAsync(before.Data.Accounts[0], before.Data.Accounts[1], Amount);
        var row = Assert.Single(preview.Rows);
        Assert.True(row.Include);
        fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, null, null)]), preview.Revision, fixture.Coordinator, Date);
        var saved = fixture.Coordinator.Read().Data;
        Assert.Equal(transfer, saved.Transactions.Single(t => t.Id == transfer.Id));
        Assert.Equal(before.Data.Accounts[1].OpeningBalance - Amount, Finance.Balance(saved, saved.Accounts[1], Date));
    }

    private sealed class ImportFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "opencode", "huishoudboek-tests", Guid.NewGuid().ToString("N"));
        public AdministrationCoordinator Coordinator { get; }
        public ImportWorkspace Workspace { get; }

        public ImportFixture()
        {
            Coordinator = new AdministrationCoordinator(Path.Combine(_directory, "administration"), initialize: true);
            var snapshot = Coordinator.Read();
            Coordinator.Update(snapshot.Revision, data =>
            {
                var seed = CreateData();
                data.Owners = seed.Owners; data.Accounts = seed.Accounts; data.Categories = seed.Categories;
            });
            Workspace = new ImportWorkspace(_directory);
        }

        public Task<PreviewResponse> PreviewAsync(Account account, Account counter, decimal amount)
        {
            var culture = CultureInfo.GetCultureInfo("nl-NL");
            var csv = string.Join(';', Headers) + "\n" + $"2026-11-03;Notaris kosten;{amount.ToString(culture)};{account.BankReference};{counter.BankReference};{(account.OpeningBalance + amount).ToString(culture)}\n";
            return PreviewCsvAsync(account, csv);
        }

        public async Task<PreviewResponse> PreviewCsvAsync(Account account, string csv)
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var upload = await Workspace.UploadAsync(source, "statement.csv", CancellationToken.None);
            return Workspace.Preview(new(upload.UploadId, account.Id, Date, Date, Profile()), Coordinator.Read());
        }

        public void Dispose()
        {
            Workspace.Dispose();
            Coordinator.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
