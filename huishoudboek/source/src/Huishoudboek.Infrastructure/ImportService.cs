using System.ComponentModel;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using CsvHelper;
using CsvHelper.Configuration;
using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

public sealed record ImportCell(string Text, decimal? Number = null, DateOnly? Date = null);
public sealed record ImportTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<ImportCell>> Rows,
    IReadOnlyList<int>? SourceRows = null);

public sealed record ImportCategoryChoice(Guid? Id, string Name);

public sealed class ImportRow : INotifyPropertyChanged
{
    private bool _include;
    private string _statusNote = "";
    public int RowNumber { get; init; }
    public DateOnly? SourceDate { get; init; }
    public Transaction? Transaction { get; init; }
    public bool IsValid => Transaction != null;
    public bool Include
    {
        get => _include;
        set { _include = value && IsValid; PropertyChanged?.Invoke(this, new(nameof(Include))); }
    }
    public DateOnly? Date => Transaction?.Date ?? SourceDate;
    public string Description => Transaction?.Description ?? "";
    public decimal? Amount => Transaction?.Amount;
    public string Note { get => string.Join(" ", new[] { _statusNote,
        Transaction?.Kind == TransactionKind.Mortgage ? "Hypotheek herkend; vul na import de werkelijke aflossing in." : "" }.Where(s => s.Length > 0)); init => _statusNote = value; }
    public IReadOnlyList<ImportCategoryChoice> CategoryChoices { get; internal set; } = [];
    public string Kind => Transaction?.Kind switch { TransactionKind.Transfer => "Interne overboeking", TransactionKind.Mortgage => "Hypotheek", _ => "Inkomst / uitgave" };
    public Guid? CategoryId
    {
        get => Transaction?.CategoryId;
        set
        {
            if (Transaction == null) return;
            Transaction.CategoryId = value;
            PropertyChanged?.Invoke(this, new(nameof(CategoryId)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class ImportService
{
    public const int MaximumRows = 100_000;
    public static IReadOnlyList<string> Worksheets(string path)
    {
        CheckFile(path);
        if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return [];
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var names = new List<string>();
        do { names.Add(reader.Name); } while (reader.NextResult());
        return names;
    }

    private static void CheckFile(string path)
    {
        if (new FileInfo(path).Length > 50 * 1024 * 1024)
            throw new InvalidDataException("Het bestand is groter dan 50 MB. Splits de export in kleinere perioden.");
        if (!new[] { ".csv", ".xls", ".xlsx" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
            throw new InvalidDataException("Kies een .csv-, .xls- of .xlsx-bestand.");
    }

    public static ImportTable Read(string path, string? worksheet = null, bool windows1252 = false)
    {
        CheckFile(path);
        if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return ReadExcel(path, worksheet);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var reader = new StreamReader(path, windows1252 ? Encoding.GetEncoding(1252) : new UTF8Encoding(false, true), true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true, Delimiter = ";", DetectDelimiterValues = [";", ",", "\t", "|"],
            TrimOptions = TrimOptions.Trim, IgnoreBlankLines = true
        });
        if (!csv.Read()) throw new InvalidDataException("Het bestand is leeg.");
        csv.ReadHeader();
        var headers = ValidateHeaders(csv.HeaderRecord ?? []);
        var rows = new List<IReadOnlyList<ImportCell>>();
        var sourceRows = new List<int>();
        while (csv.Read())
        {
            if (csv.Parser.Count != headers.Count) throw new InvalidDataException($"Regel {csv.Parser.Row} heeft niet hetzelfde aantal kolommen als de kopregel.");
            var cells = headers.Select((_, i) => new ImportCell(csv.GetField(i) ?? "")).ToList();
            if (cells.All(c => string.IsNullOrWhiteSpace(c.Text))) continue;
            rows.Add(cells); sourceRows.Add(csv.Parser.Row);
            if (rows.Count > MaximumRows) throw new InvalidDataException("Maximaal 100.000 regels per import.");
        }
        return new(headers, rows, sourceRows);
    }

    private static ImportTable ReadExcel(string path, string? worksheet)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        while (worksheet != null && reader.Name != worksheet)
            if (!reader.NextResult()) throw new InvalidDataException("Werkblad niet gevonden.");
        if (!reader.Read()) throw new InvalidDataException("Het werkblad is leeg.");
        var headers = ValidateHeaders(Enumerable.Range(0, reader.FieldCount)
            .Select(i => Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "").ToArray());
        var rows = new List<IReadOnlyList<ImportCell>>();
        var sourceRows = new List<int>();
        var sourceRow = 1;
        while (reader.Read())
        {
            sourceRow++;
            if (Enumerable.Range(0, reader.FieldCount).All(i => string.IsNullOrWhiteSpace(Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)))) continue;
            rows.Add(Enumerable.Range(0, headers.Count).Select(i =>
            {
                var cell = reader.GetValue(i);
                return cell switch
                {
                    DateTime date => new ImportCell(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Date: DateOnly.FromDateTime(date)),
                    double number => new ImportCell(number.ToString(CultureInfo.InvariantCulture), Convert.ToDecimal(number, CultureInfo.InvariantCulture)),
                    _ => new ImportCell(Convert.ToString(cell, CultureInfo.InvariantCulture) ?? "")
                };
            }).ToList());
            sourceRows.Add(sourceRow);
            if (rows.Count > MaximumRows) throw new InvalidDataException("Maximaal 100.000 regels per import.");
        }
        return new(headers, rows, sourceRows);
    }

    private static IReadOnlyList<string> ValidateHeaders(string[] headers)
    {
        var normalized = headers.Select(h => h.Trim()).ToArray();
        if (normalized.Length == 0 || normalized.Any(string.IsNullOrWhiteSpace) || normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            throw new InvalidDataException("De eerste rij moet unieke, niet-lege kolomnamen bevatten.");
        return normalized;
    }

    public static IReadOnlyList<ImportRow> Preview(ImportTable table, ImportProfile profile, Guid accountId,
        DateOnly from, DateOnly through, HouseholdData data)
    {
        if (from > through) throw new InvalidDataException("De begindatum ligt na de einddatum.");
        var culture = CultureInfo.GetCultureInfo(profile.CultureName);
        var indexes = table.Headers.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i);
        ImportCell Cell(IReadOnlyList<ImportCell> row, string column, bool optional = false)
        {
            if (string.IsNullOrWhiteSpace(column) && optional) return new("");
            return indexes.TryGetValue(column, out var i) ? row[i] : throw new InvalidDataException($"Kolom '{column}' niet gevonden.");
        }
        if (string.IsNullOrWhiteSpace(profile.DateColumn) || string.IsNullOrWhiteSpace(profile.DescriptionColumn))
            throw new InvalidDataException("Kies een datumkolom en omschrijvingskolom.");
        if (string.IsNullOrWhiteSpace(profile.AmountColumn) && (string.IsNullOrWhiteSpace(profile.DebitColumn) || string.IsNullOrWhiteSpace(profile.CreditColumn)))
            throw new InvalidDataException("Kies één bedragkolom, of zowel een af- als bij-kolom.");
        var counts = data.Transactions.Where(t => t.AccountId == accountId)
            .GroupBy(Signature).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>();
        var result = new List<ImportRow>();
        var account = data.Accounts.Single(a => a.Id == accountId);
        var categoryChoices = new[] { new ImportCategoryChoice(null, "Zonder categorie") }
            .Concat(data.Categories.Select(c => new ImportCategoryChoice(c.Id, c.Name))).ToList();
        for (var i = 0; i < table.Rows.Count; i++)
        {
            DateOnly? sourceDate = null;
            try
            {
                var row = table.Rows[i];
                var date = ParseDate(Cell(row, profile.DateColumn), profile.DateFormat, culture);
                if (date < from || date > through) continue;
                sourceDate = date;
                var currency = Cell(row, profile.CurrencyColumn, true).Text.Trim();
                if (currency.Length > 0 && !currency.Equals("EUR", StringComparison.OrdinalIgnoreCase) && currency != "€")
                    throw new InvalidDataException("Alleen EUR wordt ondersteund; deze regel gebruikt " + currency + ".");
                var reference = Cell(row, profile.AccountReferenceColumn, true).Text;
                if (!string.IsNullOrWhiteSpace(reference) && BankReferences.Normalize(reference) != BankReferences.Normalize(account.BankReference))
                    throw new InvalidDataException(string.IsNullOrWhiteSpace(account.BankReference)
                        ? "Vul eerst het rekeningnummer/IBAN in bij Rekeningen om de opdrachtgever te controleren."
                        : "Rekening opdrachtgever komt niet overeen met de gekozen rekening.");
                var description = (Cell(row, profile.DescriptionColumn).Text + " " + Cell(row, profile.ExtraDescriptionColumn, true).Text).Trim();
                if (description.Length == 0) description = Cell(row, profile.FallbackDescriptionColumn, true).Text.Trim();
                decimal amount;
                if (!string.IsNullOrWhiteSpace(profile.AmountColumn)) amount = ParseAmount(Cell(row, profile.AmountColumn), culture);
                else
                {
                    var debit = ParseAmount(Cell(row, profile.DebitColumn), culture, true);
                    var credit = ParseAmount(Cell(row, profile.CreditColumn), culture, true);
                    if (debit < 0 || credit < 0 || debit != 0 && credit != 0)
                        throw new InvalidDataException("Af/bij-kolommen moeten positief zijn; vul per regel slechts één van beide in.");
                    amount = credit - debit;
                }
                if (!string.IsNullOrWhiteSpace(profile.DirectionColumn))
                {
                    var direction = Cell(row, profile.DirectionColumn).Text.Trim().ToLowerInvariant();
                    amount = direction switch
                    {
                        "af" or "debit" or "debiet" or "d" or "db" => -Math.Abs(amount),
                        "bij" or "credit" or "krediet" or "c" or "cr" => Math.Abs(amount),
                        _ => throw new InvalidDataException($"Onbekende af/bij-code: '{direction}'.")
                    };
                }
                if (amount == 0 || string.IsNullOrWhiteSpace(description)) throw new InvalidDataException("Bedrag is nul of omschrijving ontbreekt.");
                if (date < account.OpeningDate) throw new InvalidDataException("Vóór rekeningopeningsdatum: pas de rekening en het beginsaldo aan.");
                var balanceCell = Cell(row, profile.BalanceColumn, true);
                var transaction = new Transaction { AccountId = accountId, Date = date, Description = description, Amount = amount,
                    StatementBalance = string.IsNullOrWhiteSpace(balanceCell.Text) ? null : ParseAmount(balanceCell, culture) };
                var signature = Signature(transaction);
                var occurrence = seen.GetValueOrDefault(signature) + 1;
                seen[signature] = occurrence;
                var exists = occurrence <= counts.GetValueOrDefault(signature);
                var repeated = occurrence > 1;
                // A statement row belongs only to the selected account, even when its counterparty is another own account.
                Finance.ApplySuggestions(data, transaction);
                var preview = new ImportRow
                {
                    RowNumber = table.SourceRows?[i] ?? i + 2, Transaction = transaction,
                    CategoryChoices = categoryChoices,
                    Note = exists ? "Al aanwezig; controleer vóór opnieuw importeren." :
                        repeated ? "Gelijke regel in dit bestand; kan een aparte betaling zijn." : "Gereed"
                };
                preview.Include = !exists && !repeated;
                result.Add(preview);
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException or OverflowException or ArgumentException)
            {
                result.Add(new ImportRow { RowNumber = table.SourceRows?[i] ?? i + 2, SourceDate = sourceDate, Note = ex.Message });
            }
        }
        return result;
    }

    public static string Signature(Transaction t) => $"{t.AccountId:N}|{t.Date:yyyy-MM-dd}|{t.Amount.ToString("F2", CultureInfo.InvariantCulture)}|{t.Description.Trim().ToUpperInvariant()}";

    public static ImportProfile SuggestProfile(IReadOnlyList<string> headers)
    {
        if (BankImportProfiles.Detect(headers) is { } bank) return bank;
        string Find(params string[] candidates) => candidates.Select(c => headers.FirstOrDefault(h => h.Equals(c, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(h => h != null) ?? "";
        var known = headers.Contains("Rekening opdrachtgever") && headers.Contains("Saldo na verrichting");
        // Argenta's "verrichtingen" export uses its own Dutch-language column names
        // without an opening/running balance per row.
        var argenta = headers.Contains("Boekingsdatum") && headers.Contains("Rekening tegenpartij") && !known;
        return new ImportProfile
        {
            Name = known ? "Bankexport (template.xls)" : argenta ? "Bankexport (Argenta)" : "Standaard import",
            DateColumn = Find("Datum", "Boekdatum", "Boekingsdatum", "Date", "Transactiedatum"),
            DescriptionColumn = Find("Tegenpartij", "Naam tegenpartij", "Omschrijving", "Description", "Mededeling"),
            ExtraDescriptionColumn = known ? Find("Mededeling") : argenta ? Find("Vrije mededeling", "Mededelingen", "Mededeling") : "",
            FallbackDescriptionColumn = Find("Type verrichting"),
            AmountColumn = Find("Bedrag", "Amount"), DebitColumn = Find("Af", "Debit"), CreditColumn = Find("Bij", "Credit"),
            CurrencyColumn = Find("Munt", "Valuta", "Currency"),
            AccountReferenceColumn = Find("Rekening opdrachtgever", argenta ? "Rekening" : ""), BalanceColumn = Find("Saldo na verrichting"),
            DirectionColumn = Find("Af Bij", "Af/Bij")
        };
    }

    public static DateOnly ParseDate(ImportCell cell, string format, CultureInfo culture)
    {
        if (cell.Date is { } date) return date;
        if (cell.Number is { } number && number is >= 1 and < 2958466)
            return DateOnly.FromDateTime(DateTime.FromOADate((double)number));
        if (!string.IsNullOrWhiteSpace(format)) return DateOnly.ParseExact(cell.Text.Trim(), format, culture);
        var formats = new[] { "yyyy-MM-dd", "yyyyMMdd", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy", "d.M.yyyy" };
        if (culture.Name.StartsWith("en-US", StringComparison.OrdinalIgnoreCase))
            formats = ["yyyy-MM-dd", "yyyyMMdd", "MM/dd/yyyy", "M/d/yyyy"];
        if (DateOnly.TryParseExact(cell.Text.Trim(), formats, culture, DateTimeStyles.AllowWhiteSpaces, out date)) return date;
        throw new FormatException($"Ongeldige datum '{cell.Text}'. Geef zo nodig een exact datumformaat op.");
    }

    public static decimal ParseAmount(ImportCell cell, CultureInfo culture, bool emptyIsZero = false)
    {
        if (cell.Number is { } value) return RoundAmount(value);
        var text = cell.Text.Replace("€", "", StringComparison.Ordinal).Replace("EUR", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\u00a0", "", StringComparison.Ordinal).Trim();
        if (emptyIsZero && text.Length == 0) return 0;
        if (text.EndsWith('-')) text = "-" + text[..^1];
        if (text.StartsWith('(') && text.EndsWith(')')) text = "-" + text[1..^1];
        var group = culture.NumberFormat.NumberGroupSeparator;
        var integerPart = text.TrimStart('-', '+').Split(culture.NumberFormat.NumberDecimalSeparator, StringSplitOptions.None)[0];
        if (group.Length > 0 && integerPart.Contains(group, StringComparison.Ordinal))
        {
            var groups = integerPart.Split(group, StringSplitOptions.None);
            if (groups[0].Length is < 1 or > 3 || groups.Skip(1).Any(g => g.Length != 3 || !g.All(char.IsDigit)))
                throw new FormatException($"Ongeldige duizendtallen in '{cell.Text}'; controleer de getalnotatie.");
        }
        return RoundAmount(decimal.Parse(text, NumberStyles.Number, culture));
    }

    private static decimal RoundAmount(decimal value)
    {
        if (decimal.Round(value, 2) != value) throw new FormatException("Bedrag heeft meer dan twee decimalen; controleer de getalnotatie.");
        return value;
    }

    public static void Export(string path, HouseholdData data)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.GetCultureInfo("nl-NL")) { Delimiter = ";" });
        foreach (var h in new[] { "Datum", "Rekening", "Omschrijving", "Bedrag", "Categorie", "Type", "Tegenrekening", "Aflossing", "Eigen omschrijving", "Transactie-ID", "Transferkoppeling", "Gekoppelde transactie-ID", "Vaste-post-ID", "Vaste post", "Geplande betaaldatum", "Splitsingen (JSON)", "Vergoedingsverwachtingen (JSON)" }) csv.WriteField(h);
        csv.NextRecord();
        var matches = data.TransferMatches.Where(m => m.IsConfirmed).SelectMany(m => new[] { (Id: m.DebitTransactionId, Match: m), (Id: m.CreditTransactionId, Match: m) })
            .ToDictionary(p => p.Id, p => p.Match);
        foreach (var t in data.Transactions.OrderBy(t => t.Date))
        {
            // Neutralize formula injection when opening an exported CSV in a spreadsheet.
            static string Safe(string s) => s.TrimStart().StartsWith('=') || s.TrimStart().StartsWith('+') ||
                s.TrimStart().StartsWith('-') || s.TrimStart().StartsWith('@') ? "'" + s : s;
            csv.WriteField(t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            csv.WriteField(Safe(data.Accounts.Single(a => a.Id == t.AccountId).Name));
            csv.WriteField(Safe(t.Description)); csv.WriteField(t.Amount);
            csv.WriteField(Safe(data.Categories.FirstOrDefault(c => c.Id == t.CategoryId)?.Name ?? ""));
            csv.WriteField(t.Kind.ToString());
            csv.WriteField(Safe(data.Accounts.FirstOrDefault(a => a.Id == t.CounterAccountId)?.Name ?? ""));
            csv.WriteField(t.PrincipalComponent); csv.WriteField(Safe(t.UserDescription));
            csv.WriteField(t.Id);
            csv.WriteField(matches.TryGetValue(t.Id, out var match) ? match.Id.ToString() : "");
            csv.WriteField(match == null ? "" : (match.DebitTransactionId == t.Id ? match.CreditTransactionId : match.DebitTransactionId).ToString());
            var recurring = data.RecurringPayments.FirstOrDefault(r => r.Id == t.RecurringPaymentId);
            csv.WriteField(recurring?.Id.ToString() ?? "");
            csv.WriteField(Safe(recurring?.Name ?? ""));
            csv.WriteField(recurring == null ? "" : RecurringSchedule.LinkedDate(recurring, t).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            csv.WriteField(System.Text.Json.JsonSerializer.Serialize(data.TransactionAllocations.Where(a => a.TransactionId == t.Id)));
            csv.WriteField(System.Text.Json.JsonSerializer.Serialize(data.ReimbursementClaims.Where(c => c.ExpenseTransactionId == t.Id)));
            csv.NextRecord();
        }
    }
}
