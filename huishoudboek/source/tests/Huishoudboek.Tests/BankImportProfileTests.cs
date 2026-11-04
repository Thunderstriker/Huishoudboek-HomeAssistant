using System.Text;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class BankImportProfileTests
{
    private static readonly DateOnly Date = new(2026, 11, 4);
    public static TheoryData<string, string, decimal, decimal?> Exports => new()
    {
        { "ING (NL CSV)", "Datum;Naam / Omschrijving;Rekening;Tegenrekening;Code;Af Bij;Bedrag (EUR);Mutatiesoort;Mededelingen;Saldo na mutatie\n20261104;Test shop;NL00TEST0000000001;NL00TEST0000000002;BA;Af;12,34;Betaling;Coffee;87,66\n", -12.34m, 87.66m },
        { "ING (NL CSV)", "Datum;Naam / Omschrijving;Rekening;Af Bij;Bedrag (EUR)\n20261104;Test salary;NL00TEST0000000001;Bij;12,34\n", 12.34m, null },
        { "Rabobank (NL CSV)", "IBAN/BBAN;Munt;Datum;Bedrag;Saldo na trn;Naam tegenpartij;Omschrijving-1;Code\nNL00TEST0000000001;EUR;2026-11-04;-12,34;87,66;Test shop;Coffee;BA\n", -12.34m, 87.66m },
        { "KBC / CBC (BE CSV)", "Rekeningnummer;Rubrieknaam;Naam;Munt;Afschriftnummer;Datum;Omschrijving;Bedrag;Saldo\nNL00TEST0000000001;Zichtrekening;Test owner;EUR;1;04/11/2026;Test shop;-12,34;87,66\n", -12.34m, 87.66m }
    };

    [Theory]
    [MemberData(nameof(Exports))]
    public void KnownCsvExportsDetectAndParseDatesSignsBalancesAndReferences(string name, string csv, decimal expected, decimal? balance)
    {
        using var files = new TestFiles(); File.WriteAllText(files.Path("statement.csv"), csv, new UTF8Encoding(false));
        var table = ImportService.Read(files.Path("statement.csv")); var profile = ImportService.SuggestProfile(table.Headers);
        Assert.Equal(name, profile.Name); var data = Data();
        var row = Assert.Single(ImportService.Preview(table, profile, data.Accounts[0].Id, Date, Date, data));
        Assert.True(row.IsValid, row.Note); Assert.True(row.Include); Assert.Equal(expected, row.Amount); Assert.Equal(Date, row.Date); Assert.Equal(balance, row.Transaction!.StatementBalance);
        Assert.Equal(TransactionKind.Normal, row.Transaction.Kind); Assert.Null(row.Transaction.CounterAccountId); Assert.Empty(data.Transactions);
        data.Transactions.Add(row.Transaction);
        Assert.False(Assert.Single(ImportService.Preview(table, profile, data.Accounts[0].Id, Date, Date, data)).Include);
        data.Accounts[0].BankReference = "NL00TEST0000000099";
        var wrongAccount = Assert.Single(ImportService.Preview(table, profile, data.Accounts[0].Id, Date, Date, data));
        Assert.False(wrongAccount.IsValid); Assert.Contains("Rekening opdrachtgever", wrongAccount.Note);
    }

    [Fact]
    public void DetectionUsesDistinctiveHeadersAndPreservesCaseWhileOptionalColumnsMayBeAbsent()
    {
        Assert.Null(BankImportProfiles.Detect(["Datum", "Omschrijving", "Bedrag"]));
        Assert.Equal("Standaard import", ImportService.SuggestProfile(["Datum", "Omschrijving", "Bedrag"]).Name);
        var profile = BankImportProfiles.Detect(["datum", "naam / omschrijving", "af bij", "bedrag (eur)"])!;
        Assert.Equal("datum", profile.DateColumn); Assert.Equal("", profile.BalanceColumn); Assert.Equal("", profile.ExtraDescriptionColumn);
        var template = BankImportProfiles.All[0]; template.Name = "Edited locally";
        Assert.Equal("ING (NL CSV)", BankImportProfiles.All[0].Name);
    }

    [Fact]
    public void CurrencyAndDirectionSafeguardsRemainInForceForReadyMadeProfiles()
    {
        var data = Data(); var headers = new[] { "Rekeningnummer", "Rubrieknaam", "Omschrijving", "Datum", "Bedrag", "Munt" };
        var profile = BankImportProfiles.Detect(headers)!;
        var table = new ImportTable(headers, [new ImportCell[] { new("NL00TEST0000000001"), new("Account"), new("Purchase"), new("04/11/2026"), new("-12,34"), new("USD") }]);
        var row = Assert.Single(ImportService.Preview(table, profile, data.Accounts[0].Id, Date, Date, data));
        Assert.False(row.IsValid); Assert.Contains("Alleen EUR", row.Note);
        var ingHeaders = new[] { "Datum", "Naam / Omschrijving", "Af Bij", "Bedrag (EUR)" };
        var ing = new ImportTable(ingHeaders, [new ImportCell[] { new("20261104"), new("Purchase"), new("unknown"), new("12,34") }]);
        var invalid = Assert.Single(ImportService.Preview(ing, BankImportProfiles.Detect(ingHeaders)!, data.Accounts[0].Id, Date, Date, data));
        Assert.False(invalid.IsValid); Assert.Contains("Onbekende af/bij-code", invalid.Note);
    }

    [Fact]
    public async Task SelectingBuiltInProfileBeforePreviewHandlesMissingOptionalColumnsAndStillRequiresCommit()
    {
        using var files = new TestFiles(); using var coordinator = new AdministrationCoordinator(files.Path("data"), initialize: true);
        coordinator.Replace(coordinator.Read().Revision, Data()); using var workspace = new ImportWorkspace(files.Path("uploads"));
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Datum;Naam / Omschrijving;Rekening;Af Bij;Bedrag (EUR)\n20261104;Coffee;NL00TEST0000000001;Af;12,34\n"));
        var upload = await workspace.UploadAsync(stream, "ing.csv", CancellationToken.None); var snapshot = coordinator.Read();
        var preview = workspace.Preview(new(upload.UploadId, snapshot.Data.Accounts[0].Id, Date, Date, BankImportProfiles.All[0], null, false), snapshot);
        Assert.True(Assert.Single(preview.Rows).IsValid); Assert.Empty(coordinator.Read().Data.Transactions);
        var result = workspace.Commit(new(preview.PreviewId, [new(preview.Rows[0].RowNumber, null, null)]), snapshot.Revision, coordinator, Date);
        Assert.Equal(1, result.Count); Assert.Equal(-12.34m, Assert.Single(coordinator.Read().Data.Transactions).Amount);
        Assert.Equal("ING (NL CSV)", Assert.Single(coordinator.Read().Data.ImportProfiles).Name);
    }

    private static HouseholdData Data()
    {
        var owner = new Owner { Name = "Test owner" };
        return new() { Owners = [owner], Accounts = [new() { Name = "Test account", BankReference = "NL00TEST0000000001", OwnerIds = [owner.Id], OpeningDate = Date.AddDays(-1), OpeningBalance = 100 }] };
    }
    private sealed class TestFiles : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "huishoudboek-bank-profiles-" + Guid.NewGuid().ToString("N"));
        public TestFiles() => System.IO.Directory.CreateDirectory(Directory);
        public string Path(string name) => System.IO.Path.Combine(Directory, name);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
