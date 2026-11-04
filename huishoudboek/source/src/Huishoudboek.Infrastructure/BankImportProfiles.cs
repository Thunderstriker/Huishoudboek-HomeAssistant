using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

/// <summary>Templates for named, header-based EUR exports. Each selection is a fresh editable copy.</summary>
public static class BankImportProfiles
{
    public static IReadOnlyList<ImportProfile> All => [
        new() { Id = Guid.Parse("5d22e240-d030-49b9-9aad-737610bb0001"), Name = "ING (NL CSV)",
            DateColumn = "Datum", DateFormat = "yyyyMMdd", DescriptionColumn = "Naam / Omschrijving", ExtraDescriptionColumn = "Mededelingen",
            AmountColumn = "Bedrag (EUR)", DirectionColumn = "Af Bij", AccountReferenceColumn = "Rekening", BalanceColumn = "Saldo na mutatie" },
        new() { Id = Guid.Parse("5d22e240-d030-49b9-9aad-737610bb0002"), Name = "Rabobank (NL CSV)",
            DateColumn = "Datum", DescriptionColumn = "Naam tegenpartij", ExtraDescriptionColumn = "Omschrijving-1", FallbackDescriptionColumn = "Code",
            AmountColumn = "Bedrag", CurrencyColumn = "Munt", AccountReferenceColumn = "IBAN/BBAN", BalanceColumn = "Saldo na trn" },
        new() { Id = Guid.Parse("5d22e240-d030-49b9-9aad-737610bb0003"), Name = "KBC / CBC (BE CSV)",
            DateColumn = "Datum", DescriptionColumn = "Omschrijving", AmountColumn = "Bedrag", CurrencyColumn = "Munt",
            AccountReferenceColumn = "Rekeningnummer", BalanceColumn = "Saldo", CultureName = "nl-BE" }
    ];

    public static ImportProfile? Detect(IReadOnlyList<string> headers)
    {
        bool Has(string name) => headers.Contains(name, StringComparer.OrdinalIgnoreCase);
        var template = Has("Naam / Omschrijving") && Has("Af Bij") && Has("Bedrag (EUR)") ? All[0] :
            Has("IBAN/BBAN") && Has("Saldo na trn") && Has("Omschrijving-1") ? All[1] :
            Has("Rekeningnummer") && Has("Rubrieknaam") && Has("Omschrijving") && Has("Bedrag") ? All[2] : null;
        return template == null ? null : MatchHeaders(template, headers);
    }

    public static ImportProfile MatchHeaders(ImportProfile template, IReadOnlyList<string> headers)
    {
        var profile = template with { };
        foreach (var property in typeof(ImportProfile).GetProperties().Where(p => p.Name.EndsWith("Column", StringComparison.Ordinal)))
        {
            var value = (string)property.GetValue(profile)!;
            var actual = headers.FirstOrDefault(h => h.Equals(value, StringComparison.OrdinalIgnoreCase));
            // Required columns deliberately remain selected if missing: preview explains the mismatch.
            property.SetValue(profile, actual ?? (property.Name is nameof(ImportProfile.DateColumn) or nameof(ImportProfile.DescriptionColumn) or nameof(ImportProfile.AmountColumn) or nameof(ImportProfile.DirectionColumn) ? value : ""));
        }
        return profile;
    }
}
