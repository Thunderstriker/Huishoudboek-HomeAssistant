using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

public enum EnergyCsvProblem { Columns, Identity, Time, AmbiguousTime, Quantity, MissingQuantity, Register, Status, ConflictingRows }
public sealed record EnergyCsvIssue(int Row, EnergyCsvProblem Problem);
public sealed record EnergyCsvFile(IReadOnlyList<EnergyReading> Readings, int Rows, int DuplicateRows, int IssueCount, IReadOnlyList<EnergyCsvIssue> Issues)
{
    public IReadOnlyList<EnergyMonthlyPeak> Peaks { get; init; } = [];
}
public sealed record EnergyReadingChange(IReadOnlyList<EnergyReading> Before, IReadOnlyList<EnergyReading> After, bool NeedsReplacement);
public sealed record EnergyPeakChange(EnergyMonthlyPeak? Before, EnergyMonthlyPeak After);
public sealed record EnergyImportPreview(string Revision, EnergyCsvFile File, int Unchanged, IReadOnlyList<EnergyReadingChange> Changes)
{
    public IReadOnlyList<EnergyPeakChange> PeakChanges { get; init; } = [];
    public int ChangeCount => Changes.Count + PeakChanges.Count;
    public int Conflicts => Changes.Count(c => c.NeedsReplacement) + PeakChanges.Count(c => c.Before != null);
}

public static class EnergyCsv
{
    public const int MaximumBytes = 20 * 1024 * 1024;
    private static readonly CultureInfo Belgian = CultureInfo.GetCultureInfo("nl-BE");

    public static EnergyCsvFile Read(Stream source, CancellationToken token = default)
    {
        using var text = new StreamReader(source, new UTF8Encoding(false, true), true, leaveOpen: true);
        var malformedField = false;
        using var csv = new CsvReader(text, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";", TrimOptions = TrimOptions.Trim, MaxFieldSize = 4096,
            BadDataFound = field => { if (!IsSpreadsheetEan(field.Field.Trim())) malformedField = true; },
            ExceptionMessagesContainRawData = false
        });
        try
        {
            if (!csv.Read()) throw new InvalidDataException("Het meterbestand is leeg.");
            csv.ReadHeader();
            var headers = csv.HeaderRecord!.Select(Normalize).ToArray();
            int Find(params string[] names)
            {
                var matching = headers.Select((value, i) => (value, i)).Where(p => names.Contains(p.value)).Select(p => p.i).ToArray();
                if (matching.Length > 1) throw new InvalidDataException("De meterexport heeft dubbelzinnige kolomnamen.");
                return matching.Length == 0 ? -1 : matching[0];
            }
            var date = Find("vandatum", "datum", "startdatum", "begindatum", "datumvan", "datumstart", "fromdate", "date");
            var dateEnd = Find("totdatum", "einddatum", "stopdatum", "datumtot", "datumeinde", "todate");
            var start = Find("vantijdstip", "starttijd", "startuur", "beginuur", "begintijd", "tijdstipvan", "tijdvan", "van", "start", "starttime", "uurvan");
            var end = Find("tottijdstip", "eindtijd", "einduur", "stoptijd", "stopuur", "tijdstiptot", "tijdtot", "tot", "einde", "endtime", "uurtot");
            var ean = Find("ean", "eancode", "eannummer", "aansluiting");
            var meter = Find("meter", "meternummer", "meterid", "meternr", "meternumber", "meteridentificatie");
            var volume = Find("volume", "waarde", "hoeveelheid", "verbruik", "meetwaarde", "volumekwh", "waardekwh", "kwh", "quantity");
            var unit = Find("eenheid", "meeteenheid", "unit");
            var direction = Find("richting", "meettype", "direction", "stroomrichting");
            var register = Find("register", "telwerk", "tariefregister", "telsysteem", "tijdvak", "tarief");
            var status = Find("validatiestatus", "validatie", "status", "kwaliteit", "waardetype", "validity");
            var description = Find("omschrijving", "beschrijving", "description");
            if (malformedField || new[] { date, start, end, ean, meter, volume, status }.Any(i => i < 0) ||
                (register < 0 && description < 0) || (unit < 0 && !headers[volume].EndsWith("kwh", StringComparison.Ordinal)))
                throw new InvalidDataException("De vereiste kolommen voor de Fluvius-export ontbreken. Controleer het CSV-formaat.");

            var rows = 0;
            var duplicates = 0;
            var issues = new List<EnergyCsvIssue>();
            var issueCount = 0;
            var parsed = new List<(EnergyReading Reading, int Row)>();
            var parsedPeaks = new List<(EnergyMonthlyPeak Peak, int Row)>();
            void Issue(int row, EnergyCsvProblem problem) { issueCount++; if (issues.Count < 150) issues.Add(new(row, problem)); }
            while (csv.Read())
            {
                token.ThrowIfCancellationRequested();
                if (++rows > ImportService.MaximumRows) throw new InvalidDataException("Maximaal 100.000 regels per meterimport.");
                var row = csv.Parser.Row;
                malformedField = false;
                if (csv.Parser.Count != headers.Length) { Issue(row, EnergyCsvProblem.Columns); continue; }
                var cells = Enumerable.Range(0, headers.Length).Select(i => csv.GetField(i)?.Trim() ?? "").ToArray();
                if (malformedField) { Issue(row, EnergyCsvProblem.Columns); continue; }
                string Cell(int i) => i < 0 ? "" : cells[i];
                var point = Cell(ean);
                if (IsSpreadsheetEan(point)) point = point[2..^1];
                if (!EnergyTerms.ValidEan(point) || Cell(meter).Length is 0 or > 100) { Issue(row, EnergyCsvProblem.Identity); continue; }
                if (!DateOnly.TryParseExact(Cell(date), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) || !BelgianEnergyTime.Supported(day))
                { Issue(row, EnergyCsvProblem.Time); continue; }
                if (Normalize(Cell(register)) == "piekvermogen")
                {
                    if (day.Day != 1 || !DateOnly.TryParseExact(Cell(dateEnd), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var until)
                        || until != day.AddMonths(1) || !Clock(Cell(start), day, false, out var a) || a.TimeOfDay != TimeSpan.Zero
                        || !Clock(Cell(end), until, false, out var b) || b.TimeOfDay != TimeSpan.Zero)
                    { Issue(row, EnergyCsvProblem.Time); continue; }
                    if (Cell(volume).Length == 0) { Issue(row, EnergyCsvProblem.MissingQuantity); continue; }
                    if (!Cell(unit).Equals("kW", StringComparison.OrdinalIgnoreCase) || !Quantity(Cell(volume), out var kw))
                    { Issue(row, EnergyCsvProblem.Quantity); continue; }
                    var peakReading = new EnergyMonthlyPeak { Ean = point, Meter = Cell(meter), Month = day, Kw = kw, Status = Cell(status) };
                    if (!EnergyTerms.ValidMonthlyPeak(peakReading)) { Issue(row, EnergyCsvProblem.Status); continue; }
                    parsedPeaks.Add((peakReading, row));
                    continue;
                }
                var lastDay = day;
                if (dateEnd >= 0 && (!DateOnly.TryParseExact(Cell(dateEnd), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out lastDay) ||
                    lastDay < day || lastDay > day.AddDays(1))) { Issue(row, EnergyCsvProblem.Time); continue; }
                if (!Clock(Cell(start), day, false, out var begin) || !Clock(Cell(end), lastDay, true, out var finish)) { Issue(row, EnergyCsvProblem.Time); continue; }
                var daily = IsDailyTotal(day, begin, finish);
                DateTime from, to;
                if (daily)
                {
                    from = BelgianEnergyTime.MidnightUtc(day);
                    to = BelgianEnergyTime.MidnightUtc(day.AddDays(1));
                    if (Cell(volume).Length == 0) { Issue(row, EnergyCsvProblem.MissingQuantity); continue; }
                }
                else
                {
                    if (finish.TimeOfDay == TimeSpan.Zero && finish.Date == begin.Date) finish = finish.AddDays(1);
                    var candidates = Bounds(begin, finish);
                    if (candidates.Length != 1) { Issue(row, candidates.Length > 1 ? EnergyCsvProblem.AmbiguousTime : EnergyCsvProblem.Time); continue; }
                    (from, to) = candidates[0];
                }
                if (!Quantity(Cell(volume), out var kwh) ||
                    (unit >= 0 && !Cell(unit).Equals("kWh", StringComparison.OrdinalIgnoreCase))) { Issue(row, EnergyCsvProblem.Quantity); continue; }
                var detail = Cell(description);
                var registerWords = Words(Cell(register));
                var directionText = direction >= 0 ? Words(Cell(direction)) : registerWords.Overlaps(["AFNAME", "CONSUMPTION", "INJECTIE", "INJECTION"]) ? registerWords : Words(detail);
                var days = new[] { "DAG", "DAGREGISTER", "DAGTARIEF", "PIEKUREN", "DAY" };
                var nights = new[] { "NACHT", "NACHTREGISTER", "NACHTTARIEF", "DALUREN", "NIGHT" };
                var registerText = registerWords.Overlaps(days) || registerWords.Overlaps(nights) ? registerWords : Words(detail);
                var consumption = directionText.Overlaps(["AFNAME", "CONSUMPTION"]);
                var injection = directionText.Overlaps(["INJECTIE", "INJECTION"]);
                var peak = registerText.Overlaps(days);
                var offpeak = registerText.Overlaps(nights);
                if (consumption == injection || peak == offpeak || detail.Length > 500) { Issue(row, EnergyCsvProblem.Register); continue; }
                if (EnergyTerms.ReadingQuality(Cell(status)) == 0 || (Cell(status).Equals("Geen verbruik", StringComparison.OrdinalIgnoreCase) && kwh != 0))
                { Issue(row, EnergyCsvProblem.Status); continue; }
                var reading = new EnergyReading { Ean = point, Meter = Cell(meter), StartUtc = from, EndUtc = to, Quantity = kwh,
                    Resolution = daily ? EnergyReadingResolution.DailyTotal : EnergyReadingResolution.QuarterHour,
                    Direction = consumption ? EnergyDirection.Consumption : EnergyDirection.Injection, Register = peak ? EnergyRegister.Day : EnergyRegister.Night,
                    Status = Cell(status), Description = detail };
                if (!EnergyTerms.ValidReading(reading)) { Issue(row, EnergyCsvProblem.Time); continue; }
                parsed.Add((reading, row));
            }

            var accepted = new List<EnergyReading>();
            foreach (var group in parsed.GroupBy(p => Slot(p.Reading)))
            {
                var keys = group.GroupBy(p => Key(p.Reading)).ToList();
                if (group.Select(p => p.Reading.Meter).Distinct().Skip(1).Any() || keys.Any(g => g.Skip(1).Any(p => !Same(g.First().Reading, p.Reading))))
                { foreach (var item in group) Issue(item.Row, EnergyCsvProblem.ConflictingRows); continue; }
                foreach (var key in keys) { accepted.Add(key.First().Reading); duplicates += key.Count() - 1; }
            }
            var acceptedPeaks = new List<EnergyMonthlyPeak>();
            foreach (var group in parsedPeaks.GroupBy(p => PeakSlot(p.Peak)))
            {
                var first = group.First().Peak;
                if (group.Skip(1).Any(p => !Same(first, p.Peak)))
                { foreach (var item in group) Issue(item.Row, EnergyCsvProblem.ConflictingRows); continue; }
                acceptedPeaks.Add(first);
                duplicates += group.Count() - 1;
            }
            return new(accepted, rows, duplicates, issueCount, issues.OrderBy(i => i.Row).ToList()) { Peaks = acceptedPeaks };
        }
        catch (Exception ex) when (ex is CsvHelperException or DecoderFallbackException)
        { throw new InvalidDataException("Ongeldig CSV-formaat of tekencodering. Gebruik de oorspronkelijke UTF-8-export van Fluvius."); }
    }

    public static EnergyImportPreview Compare(EnergyCsvFile file, IEnumerable<EnergyReading> existing, string revision, IEnumerable<EnergyMonthlyPeak>? peaks = null)
    {
        var previous = existing.ToLookup(Slot);
        var changes = new List<EnergyReadingChange>();
        var unchanged = 0;
        foreach (var group in file.Readings.GroupBy(Slot))
        {
            var before = previous[group.Key].ToList();
            var incoming = group.ToDictionary(Key);
            var meterChanged = before.Any(r => r.Meter != group.First().Meter);
            // A quarter in a different register could be a correction. For daily totals both registers are independent.
            var moved = group.Key.Resolution == EnergyReadingResolution.QuarterHour
                ? before.Where(r => r.Quantity != 0 && !incoming.ContainsKey(Key(r)) && group.Any(a => a.Direction == r.Direction && a.Quantity != 0)).ToList() : [];
            var conflict = meterChanged || moved.Count != 0;
            var after = meterChanged ? [] : before.Except(moved).ToDictionary(Key);
            var changed = false;
            foreach (var reading in group)
            {
                if (after.TryGetValue(Key(reading), out var old))
                {
                    if (Same(old, reading)) { unchanged++; continue; }
                    conflict = true;
                }
                after[Key(reading)] = reading;
                changed = true;
            }
            if (changed) changes.Add(new(before, after.Values.ToList(), conflict));
        }
        var oldPeaks = (peaks ?? []).ToDictionary(PeakSlot);
        var peakChanges = new List<EnergyPeakChange>();
        foreach (var peak in file.Peaks)
        {
            var before = oldPeaks.GetValueOrDefault(PeakSlot(peak));
            if (before != null && Same(before, peak)) unchanged++;
            else peakChanges.Add(new(before, peak));
        }
        return new(revision, file, unchanged, changes) { PeakChanges = peakChanges };
    }

    private static bool Quantity(string text, out decimal quantity) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Belgian, out quantity) && quantity is >= 0 and <= 1_000_000;
    private static (string Ean, DateOnly Month) PeakSlot(EnergyMonthlyPeak peak) => (peak.Ean, peak.Month);
    private static bool Same(EnergyMonthlyPeak a, EnergyMonthlyPeak b) => a.Meter == b.Meter && a.Kw == b.Kw && a.Status.Equals(b.Status, StringComparison.OrdinalIgnoreCase);
    private static (string Ean, DateTime StartUtc, EnergyReadingResolution Resolution) Slot(EnergyReading r) => (r.Ean, r.StartUtc, r.Resolution);
    private static (EnergyDirection, EnergyRegister) Key(EnergyReading r) => (r.Direction, r.Register);
    private static bool Same(EnergyReading a, EnergyReading b) => a.Meter == b.Meter && a.Quantity == b.Quantity && a.Unit == b.Unit &&
        a.Status.Equals(b.Status, StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string value) => string.Concat(value.ToLowerInvariant().Where(char.IsLetterOrDigit));
    private static HashSet<string> Words(string value) => string.Concat(value.ToUpperInvariant().Select(c => char.IsLetter(c) ? c : ' '))
        .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    private static bool IsSpreadsheetEan(string field) => field is { Length: 21 } && field.StartsWith("=\"", StringComparison.Ordinal) && field[^1] == '"' && EnergyTerms.ValidEan(field[2..^1]);
    private static bool Clock(string value, DateOnly day, bool end, out DateTime result)
    {
        if (end && value is "24:00" or "24:00:00") { result = day.AddDays(1).ToDateTime(TimeOnly.MinValue); return true; }
        if (TimeOnly.TryParseExact(value, ["HH:mm", "HH:mm:ss", "H:mm", "H:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) && time.Second == 0 && time.Minute % 15 == 0)
        { result = day.ToDateTime(time); return true; }
        result = default; return false;
    }
    private static bool IsDailyTotal(DateOnly day, DateTime begin, DateTime finish)
    {
        if (begin.TimeOfDay != TimeSpan.Zero) return false;
        var nextMidnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        if (finish == nextMidnight) return true;
        // Fluvius can write a 24-hour elapsed end on clock-change days (23:00 on the fall-back day).
        return BelgianEnergyTime.Quarters(day) switch
        {
            100 => finish == nextMidnight.AddHours(-1),
            92 => finish == nextMidnight.AddHours(1),
            _ => false
        };
    }
    private static (DateTime, DateTime)[] Bounds(DateTime begin, DateTime finish)
    {
        var zone = BelgianEnergyTime.Zone;
        IEnumerable<DateTime> Utc(DateTime time) => zone.IsInvalidTime(time) ? [] : zone.IsAmbiguousTime(time)
            ? zone.GetAmbiguousTimeOffsets(time).Select(o => new DateTimeOffset(time, o).UtcDateTime)
            : [TimeZoneInfo.ConvertTimeToUtc(time, zone)];
        return (from start in Utc(begin) from end in Utc(finish) where end - start == TimeSpan.FromMinutes(15) select (start, end)).ToArray();
    }
}
