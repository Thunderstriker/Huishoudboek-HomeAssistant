using System.Text;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class RecurringImportMatchingTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly DateOnly Due = new(2026, 10, 26);
    private const string BankText = "DOBCO MEDICAL SYSTEMS NV /A/WRK-006508.Salary.2026.010";

    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Joel" };
        var account = new Account { Name = "Zichtrekening AXA", OwnerIds = [owner.Id],
            OpeningDate = new(2026, 1, 1), OpeningBalance = 1000, MinimumCashBuffer = 100 };
        var category = new Category { Name = "Salaris" };
        return new HouseholdData { Owners = [owner], Accounts = [account], Categories = [category],
            RecurringPayments = [new RecurringPayment { Name = "Salaris Joel", AccountId = account.Id,
                CategoryId = category.Id, Amount = 3260, DayOfMonth = 26, StartDate = account.OpeningDate,
                MatchText = "DOBCO MEDICAL SYSTEMS NV", MatchTolerance = 100, MatchWindowDays = 5 }] };
    }

    private static Transaction Entry(HouseholdData data, decimal amount = 3263.18m, int offset = 0) => new()
    {
        AccountId = data.Accounts[0].Id, Amount = amount, Date = Due.AddDays(offset), Description = BankText
    };

    [Fact]
    public async Task SalaryImportAndReimportAutomaticallyLinkWithinTheConfiguredHundredEuroTolerance()
    {
        using var fixture = new ImportFixture();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var preview = await fixture.PreviewAsync($"2026-10-26;{BankText};3263,18;4263,18");
            var row = Assert.Single(preview.Rows);
            Assert.Equal(3263.18m, row.Amount);
            Assert.NotNull(row.CategoryId);
            Assert.Empty(fixture.Coordinator.Read().Data.Transactions);

            var result = fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, row.CategoryId, null)]),
                preview.Revision, fixture.Coordinator, Today);
            var saved = fixture.Coordinator.Read();
            var transaction = Assert.Single(saved.Data.Transactions);
            var plan = Assert.Single(saved.Data.RecurringPayments);
            Assert.Equal(plan.Id, transaction.RecurringPaymentId);
            Assert.Equal(Due, transaction.RecurringOccurrenceDate);
            Assert.Equal((3263.18m, Due, BankText, 4263.18m),
                (transaction.Amount, transaction.Date, transaction.Description, transaction.StatementBalance));
            Assert.Equal(row.CategoryId, transaction.CategoryId);
            Assert.Equal(result.BatchId, transaction.ImportBatchId);
            Assert.Equal(3260m, plan.Amount);
            Assert.Empty(RecurringSchedule.Candidates(saved.Data, Today));
            var forecast = Assert.Single(CashFlow.Project(saved.Data, Today).Accounts);
            Assert.Equal(4263.18m, forecast.CurrentBalance);
            Assert.Equal(Due, forecast.StatementThrough);
            Assert.Equal(new DateOnly(2026, 11, 26), forecast.NextIncomeDate);
            Assert.Equal(3260m, Assert.Single(forecast.Events).Amount);
            Assert.DoesNotContain(forecast.Warnings, w => w.Contains(plan.Name));
            Validation.Validate(saved.Data);

            if (attempt == 0)
            {
                var undone = fixture.Coordinator.Update(saved.Revision, d => HouseholdEdits.UndoImport(d, result.BatchId));
                Assert.Empty(undone.Data.Transactions);
                Assert.Equal(1000m, Finance.Balance(undone.Data, undone.Data.Accounts[0], Today));
                Assert.Single(undone.Data.RecurringPayments);
            }
        }
    }

    [Fact]
    public async Task ExpenseImportUsesTheConfiguredAmountAndDateMarginsAcrossMonthBoundaries()
    {
        using var fixture = new ImportFixture();
        fixture.Coordinator.Update(fixture.Coordinator.Read().Revision, data =>
        {
            var plan = data.RecurringPayments[0];
            plan.Name = "Beheer rekening";
            plan.Amount = -6;
            plan.DayOfMonth = 3;
            plan.MatchText = "AXA";
            plan.MatchTolerance = 1;
            plan.MatchWindowDays = 10;
        });
        var preview = await fixture.PreviewAsync("2026-10-27;AXA rekeningkosten;-6,50;993,50");
        var row = Assert.Single(preview.Rows);
        fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, row.CategoryId, null)]),
            preview.Revision, fixture.Coordinator, Today);
        var data = fixture.Coordinator.Read().Data;
        var transaction = Assert.Single(data.Transactions);
        Assert.Equal(data.RecurringPayments[0].Id, transaction.RecurringPaymentId);
        Assert.Equal(new DateOnly(2026, 11, 3), transaction.RecurringOccurrenceDate);
        Assert.Equal(new DateOnly(2026, 10, 27), transaction.Date);
        Assert.Equal(-6.50m, transaction.Amount);
        Assert.Equal(-6m, data.RecurringPayments[0].Amount);
        var forecast = Assert.Single(CashFlow.Project(data, Today).Accounts);
        Assert.Equal(993.50m, forecast.CurrentBalance);
        Assert.DoesNotContain(forecast.Events, e => e.IsOverdue);
        Assert.DoesNotContain(forecast.Warnings, w => w.Contains("Beheer rekening"));
        Validation.Validate(data);
    }

    [Theory]
    [InlineData(3160, true)]
    [InlineData(3360, true)]
    [InlineData(3159.99, false)]
    [InlineData(3360.01, false)]
    public void AutomaticRecognitionRespectsTheConfiguredAmountBoundary(double amount, bool expectedLink)
    {
        var data = Data();
        var transaction = Entry(data, (decimal)amount);
        Finance.ApplySuggestions(data, transaction);
        Assert.Equal(expectedLink, transaction.RecurringPaymentId == data.RecurringPayments[0].Id);
        Assert.Equal((decimal)amount, transaction.Amount);
    }

    [Theory]
    [InlineData(-11, false)]
    [InlineData(-10, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void AutomaticRecognitionRespectsConfiguredDateWindowsBeyondFiveDays(int offset, bool expectedLink)
    {
        var data = Data();
        data.RecurringPayments[0].MatchWindowDays = 10;
        data.RecurringPayments[0].Amount = -3260;
        var transaction = Entry(data, -3263.18m, offset);
        Finance.ApplySuggestions(data, transaction);
        Assert.Equal(expectedLink, transaction.RecurringPaymentId == data.RecurringPayments[0].Id);
        Assert.Equal(expectedLink ? Due : (DateOnly?)null, transaction.RecurringOccurrenceDate);
        Assert.Equal(Due.AddDays(offset), transaction.Date);
        Assert.Equal(-3263.18m, transaction.Amount);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("direction")]
    [InlineData("text")]
    [InlineData("blank text")]
    [InlineData("multiple plans")]
    [InlineData("multiple occurrences")]
    [InlineData("already linked")]
    public void ConfiguredToleranceDoesNotBypassOtherRecognitionAndAmbiguityChecks(string reason)
    {
        var data = Data();
        var transaction = Entry(data);
        var plan = data.RecurringPayments[0];
        switch (reason)
        {
            case "account":
                var other = data.Accounts[0] with { Id = Guid.NewGuid(), Name = "Andere rekening" };
                data.Accounts.Add(other);
                transaction.AccountId = other.Id;
                break;
            case "direction": transaction.Amount = -transaction.Amount; break;
            case "text": transaction.Description = "Andere werkgever"; break;
            case "blank text": plan.MatchText = ""; break;
            case "multiple plans": data.RecurringPayments.Add(plan with { Id = Guid.NewGuid(), Name = "Andere vaste post" }); break;
            case "multiple occurrences": plan.MatchWindowDays = 31; break;
            case "already linked":
                var previous = Entry(data);
                data.Transactions.Add(previous);
                RecurringSchedule.Link(data, previous.Id, plan.Id, Due);
                break;
        }
        Finance.ApplySuggestions(data, transaction);
        Assert.Null(transaction.RecurringPaymentId);
        Assert.Null(transaction.RecurringOccurrenceDate);
        Validation.Validate(data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportDoesNotChooseBetweenCompetingRowsRegardlessOfProcessingOrder(bool reverse)
    {
        using var fixture = new ImportFixture();
        var preview = await fixture.PreviewAsync($"2026-10-26;{BankText};3263,18;\n" +
            $"2026-10-27;{BankText};3264,20;\n2026-09-26;{BankText};3263,18;");
        var rows = reverse ? preview.Rows.Reverse() : preview.Rows;
        var selections = rows.Select(r => new ImportSelection(r.RowNumber, r.CategoryId, null)).ToArray();
        fixture.Workspace.Commit(new(preview.PreviewId, selections), preview.Revision, fixture.Coordinator, Today);
        var data = fixture.Coordinator.Read().Data;
        Assert.All(data.Transactions.Where(t => t.Date.Month == 10), t => Assert.Null(t.RecurringPaymentId));
        Assert.Equal(data.RecurringPayments[0].Id, data.Transactions.Single(t => t.Date.Month == 9).RecurringPaymentId);
        Assert.Equal(2, RecurringSchedule.Candidates(data, Today).Count);
        Validation.Validate(data);
    }

    [Fact]
    public async Task DeselectedImportRowsDoNotPreventAnUnambiguousAutomaticLinkOrOverwriteTheSelectedCategory()
    {
        using var fixture = new ImportFixture();
        var preview = await fixture.PreviewAsync($"2026-10-26;{BankText};3263,18;\n2026-10-27;{BankText};3264,20;");
        var row = preview.Rows[1];
        fixture.Workspace.Commit(new(preview.PreviewId, [new(row.RowNumber, null, null)]),
            preview.Revision, fixture.Coordinator, Today);
        var data = fixture.Coordinator.Read().Data;
        var transaction = Assert.Single(data.Transactions);
        Assert.Equal(data.RecurringPayments[0].Id, transaction.RecurringPaymentId);
        Assert.Equal(Due, transaction.RecurringOccurrenceDate);
        Assert.Equal(3264.20m, transaction.Amount);
        Assert.Null(transaction.CategoryId);
        Validation.Validate(data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingEntriesAreNotReconciledButStillPreventAmbiguousAutomaticLinks(bool competing)
    {
        var data = Data();
        var previous = Entry(data);
        if (!competing) previous.Date = Due.AddMonths(-1);
        data.Transactions.Add(previous);
        var original = previous with { };
        var transaction = Entry(data, offset: 1);
        Finance.ApplySuggestions(data, transaction);
        Assert.Equal(original, previous);
        Assert.Equal(!competing, transaction.RecurringPaymentId == data.RecurringPayments[0].Id);
    }

    private sealed class ImportFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "opencode", "huishoudboek-tests", Guid.NewGuid().ToString("N"));
        public AdministrationCoordinator Coordinator { get; }
        public ImportWorkspace Workspace { get; }

        public ImportFixture()
        {
            Coordinator = new AdministrationCoordinator(Path.Combine(_directory, "administration"), initialize: true);
            Coordinator.Replace(Coordinator.Read().Revision, Data());
            Workspace = new ImportWorkspace(_directory);
        }

        public async Task<PreviewResponse> PreviewAsync(string rows)
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes("Datum;Omschrijving;Bedrag;Saldo na verrichting\n" + rows));
            var upload = await Workspace.UploadAsync(source, "AXA.csv", CancellationToken.None);
            var snapshot = Coordinator.Read();
            return Workspace.Preview(new(upload.UploadId, snapshot.Data.Accounts[0].Id,
                new(2026, 9, 1), Today), snapshot);
        }

        public void Dispose()
        {
            Workspace.Dispose();
            Coordinator.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
