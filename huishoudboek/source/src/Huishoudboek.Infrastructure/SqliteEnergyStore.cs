using System.Globalization;
using Huishoudboek.Core;
using Microsoft.Data.Sqlite;

namespace Huishoudboek.Infrastructure;

public sealed partial class SqliteHouseholdStore
{
    private static void CreateEnergyTables(SqliteConnection c, SqliteTransaction tx)
    {
        foreach (var type in new[] { typeof(EnergyContract), typeof(EnergyTariff), typeof(EnergyIndexValue), typeof(EnergyReading), typeof(EnergyMarketPrice) })
            CreateTable(c, tx, type, requireValues: true);
        Execute(c, tx, "CREATE UNIQUE INDEX ix_energy_reading_slot ON EnergyReading(Ean, StartUtc, Direction, Register)");
        Execute(c, tx, "CREATE UNIQUE INDEX ix_energy_market_slot ON EnergyMarketPrice(Series, StartUtc, EndUtc)");
        Execute(c, tx, "CREATE TABLE energy_tariff_documents (tariff_id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, content BLOB NOT NULL)");
    }

    public EnergySettings ReadEnergySettings()
    {
        using var c = Open(_path, true);
        using var tx = c.BeginTransaction(deferred: true);
        var result = ReadEnergySettings(c, tx);
        EnergyTerms.Validate(result);
        return result;
    }

    private static EnergySettings ReadEnergySettings(SqliteConnection c, SqliteTransaction tx) => new(
        Read<EnergyContract>(c, tx), Read<EnergyTariff>(c, tx), Read<EnergyIndexValue>(c, tx));

    public void SaveEnergySettings(EnergySettings settings, EnergyTariffDocument? newDocument = null)
    {
        EnergyTerms.Validate(settings);
        using var c = Open(_path);
        using var tx = c.BeginTransaction();
        CheckEnergyLinks(c, tx, settings);
        Write(c, tx, settings.Contracts); Write(c, tx, settings.Tariffs); Write(c, tx, settings.Indexes);
        Execute(c, tx, "DELETE FROM energy_tariff_documents WHERE tariff_id NOT IN (SELECT Id FROM EnergyTariff WHERE DocumentName <> '')");
        if (newDocument != null) WriteEnergyDocument(c, tx, newDocument);
        CheckEnergyDocuments(c, tx, settings);
        tx.Commit();
    }

    private static void CheckEnergyLinks(SqliteConnection c, SqliteTransaction tx, EnergySettings settings)
    {
        using var command = c.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT Id FROM SubscriptionContract";
        using var reader = command.ExecuteReader();
        var ids = new HashSet<Guid>();
        while (reader.Read()) ids.Add(Guid.Parse(reader.GetString(0)));
        if (settings.Contracts.Any(c => c.SubscriptionId is { } id && !ids.Contains(id)))
            throw new InvalidDataException("Het gekoppelde abonnement bestaat niet meer.");
    }

    public List<EnergyReading> ReadEnergyReadings(string ean, DateOnly from, DateOnly until)
    {
        using var c = Open(_path, true);
        using var tx = c.BeginTransaction(deferred: true);
        return EnergyReadings(c, tx, ean, BelgianEnergyTime.MidnightUtc(from), BelgianEnergyTime.MidnightUtc(until)).ToList();
    }

    private static IEnumerable<EnergyReading> EnergyReadings(SqliteConnection c, SqliteTransaction tx, string ean, DateTime from, DateTime until) =>
        ReadRows<EnergyReading>(c, tx, filterAndOrder: "WHERE Ean=$ean AND StartUtc >= $from AND StartUtc < $until ORDER BY StartUtc, Direction, Register",
            parameters: [("$ean", ean), ("$from", Iso(from)), ("$until", Iso(until))]);

    public EnergyImportPreview PreviewEnergyImport(EnergyCsvFile file, string revision)
    {
        using var c = Open(_path, true);
        using var tx = c.BeginTransaction(deferred: true);
        return CompareEnergyImport(c, tx, file, revision);
    }

    private static EnergyImportPreview CompareEnergyImport(SqliteConnection c, SqliteTransaction tx, EnergyCsvFile file, string revision)
    {
        if (file.Readings.Count > ImportService.MaximumRows || file.Readings.Any(r => !EnergyTerms.ValidReading(r)))
            throw new InvalidDataException("De import bevat ongeldige meetgegevens.");
        var groups = file.Readings.GroupBy(r => (r.Ean, r.StartUtc.Year, r.StartUtc.Month)).ToList();
        if (groups.Select(g => g.Key.Ean).Distinct().Count() > 25 || file.Readings.Select(r => (r.Ean, r.Meter)).Distinct().Count() > 50)
            throw new InvalidDataException("Importeer maximaal 25 aansluitingen en 50 meters per meterbestand.");
        var existing = groups.SelectMany(g =>
        {
            var dates = g.Select(r => r.StartUtc).ToHashSet();
            return EnergyReadings(c, tx, g.Key.Ean, dates.Min(), dates.Max().AddMinutes(15)).Where(r => dates.Contains(r.StartUtc));
        });
        return EnergyCsv.Compare(file, existing, revision);
    }

    public bool CommitEnergyImport(EnergyCsvFile file, bool replace)
    {
        using var c = Open(_path);
        using var tx = c.BeginTransaction();
        var preview = CompareEnergyImport(c, tx, file, "");
        if (preview.Conflicts > 0 && !replace) throw new InvalidOperationException("Bevestig de getoonde vervangingen om deze batch op te slaan.");
        if (preview.Changes.Count == 0) return false;
        using var command = c.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "DELETE FROM EnergyReading WHERE Ean=$ean AND StartUtc=$start";
        var ean = command.Parameters.AddWithValue("$ean", "");
        var start = command.Parameters.AddWithValue("$start", "");
        command.Prepare();
        foreach (var change in preview.Changes.Where(change => change.Before.Count > 0))
        {
            var head = change.Before[0];
            ean.Value = head.Ean; start.Value = Iso(head.StartUtc);
            command.ExecuteNonQuery();
        }
        Insert(c, tx, preview.Changes.SelectMany(change => change.After));
        tx.Commit();
        return true;
    }

    public List<EnergyMarketPrice> ReadEnergyPrices(DateOnly from, DateOnly until)
    {
        using var c = Open(_path, true);
        using var tx = c.BeginTransaction(deferred: true);
        return ReadRows<EnergyMarketPrice>(c, tx,
            filterAndOrder: "WHERE Series=$series AND StartUtc >= $from AND StartUtc < $until ORDER BY StartUtc, EndUtc",
            parameters: [("$series", (int)EnergyMarketSeries.BelgianDayAhead), ("$from", Iso(BelgianEnergyTime.MidnightUtc(from))), ("$until", Iso(BelgianEnergyTime.MidnightUtc(until)))]).ToList();
    }

    public void SaveEnergyPrices(IReadOnlyList<EnergyMarketPrice> prices)
    {
        if (prices.Any(p => !EnergyTerms.ValidPrice(p))) throw new InvalidDataException("De prijsbron stuurde ongeldige intervallen.");
        if (prices.Count == 0) return;
        using var c = Open(_path);
        using var tx = c.BeginTransaction();
        using var command = c.CreateCommand();
        command.Transaction = tx;
        // Remove intersecting cached slots when a source corrects the resolution or value. A missing row does not erase history.
        command.CommandText = "DELETE FROM EnergyMarketPrice WHERE Series=$series AND StartUtc >= $near AND StartUtc < $end AND EndUtc > $start";
        var series = command.Parameters.AddWithValue("$series", 0);
        var near = command.Parameters.AddWithValue("$near", "");
        var start = command.Parameters.AddWithValue("$start", "");
        var end = command.Parameters.AddWithValue("$end", "");
        command.Prepare();
        foreach (var price in prices)
        {
            series.Value = (int)price.Series; near.Value = Iso(price.StartUtc.AddHours(-1)); start.Value = Iso(price.StartUtc); end.Value = Iso(price.EndUtc);
            command.ExecuteNonQuery();
        }
        Insert(c, tx, prices);
        tx.Commit();
    }

    public EnergyTariffDocument? ReadEnergyDocument(Guid tariffId)
    {
        using var c = Open(_path, true);
        using var command = c.CreateCommand();
        command.CommandText = "SELECT name, content FROM energy_tariff_documents WHERE tariff_id=$id";
        command.Parameters.AddWithValue("$id", tariffId.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(tariffId, reader.GetString(0), (byte[])reader[1]) : null;
    }

    private static void WriteEnergyDocument(SqliteConnection c, SqliteTransaction tx, EnergyTariffDocument document)
    {
        if (EnergyPdf.Create(document.TariffId, document.Name, document.Content).Name != document.Name)
            throw new InvalidDataException("Ongeldig bestand bij de energietariefkaart.");
        using var command = c.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "INSERT OR REPLACE INTO energy_tariff_documents(tariff_id,name,content) VALUES($id,$name,$data)";
        command.Parameters.AddWithValue("$id", document.TariffId.ToString());
        command.Parameters.AddWithValue("$name", document.Name);
        command.Parameters.AddWithValue("$data", document.Content);
        command.ExecuteNonQuery();
    }

    private static void CheckEnergyDocuments(SqliteConnection c, SqliteTransaction tx, EnergySettings settings)
    {
        var expected = settings.Tariffs.Where(t => t.DocumentName.Length > 0).ToDictionary(t => t.Id, t => t.DocumentName);
        using var command = c.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT tariff_id, name, length(content), substr(content,1,8) FROM energy_tariff_documents";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (!Guid.TryParse(reader.GetString(0), out var id) || !expected.Remove(id, out var name) || name != reader.GetString(1) ||
                reader.GetInt64(2) is < 5 or > PurchaseReceipts.MaximumDocumentBytes || !EnergyPdf.HasSignature((byte[])reader[3]))
                throw new InvalidDataException("Een PDF-tariefkaart hoort niet bij de opgeslagen tariefversie.");
        if (expected.Count != 0) throw new InvalidDataException("Een bewaarde PDF-tariefkaart ontbreekt in de database.");
    }

    private static void ValidateEnergyBackup(SqliteConnection c, HouseholdData household)
    {
        using var tx = c.BeginTransaction(deferred: true);
        var settings = ReadEnergySettings(c, tx);
        EnergyTerms.Validate(settings);
        var subscriptionIds = household.Contracts.Select(c => c.Id).ToHashSet();
        if (settings.Contracts.Any(c => c.SubscriptionId is { } id && !subscriptionIds.Contains(id))) throw new InvalidDataException("Het gekoppelde abonnement ontbreekt in de back-up.");
        CheckEnergyDocuments(c, tx, settings);
        EnergyReading? previous = null;
        foreach (var r in ReadRows<EnergyReading>(c, tx, filterAndOrder: "ORDER BY Ean, StartUtc, Direction, Register"))
        {
            if (!EnergyTerms.ValidReading(r) || (previous is { } p && p.Ean == r.Ean && p.StartUtc == r.StartUtc &&
                (r.Meter != p.Meter || (p.Direction == r.Direction && p.Register == r.Register)))) throw new InvalidDataException("Ongeldige of overlappende energiemetingen in de back-up.");
            previous = r;
        }
        EnergyMarketPrice? priorPrice = null;
        foreach (var p in ReadRows<EnergyMarketPrice>(c, tx, filterAndOrder: "ORDER BY Series, StartUtc, EndUtc"))
        {
            if (!EnergyTerms.ValidPrice(p) || (priorPrice is { } q && q.Series == p.Series && q.EndUtc > p.StartUtc)) throw new InvalidDataException("Ongeldige of overlappende marktprijzen in de back-up.");
            priorPrice = p;
        }
    }

    private static void RestoreEnergy(SqliteConnection c, SqliteTransaction tx, SqliteConnection source)
    {
        Execute(c, tx, "DELETE FROM energy_tariff_documents");
        if (Version(source) < 20)
        {
            foreach (var table in new[] { "EnergyContract", "EnergyTariff", "EnergyIndexValue", "EnergyReading", "EnergyMarketPrice" }) Execute(c, tx, $"DELETE FROM [{table}]");
            return;
        }
        using var sourceTx = source.BeginTransaction(deferred: true);
        var settings = ReadEnergySettings(source, sourceTx);
        Write(c, tx, settings.Contracts); Write(c, tx, settings.Tariffs); Write(c, tx, settings.Indexes);
        Write(c, tx, ReadRows<EnergyReading>(source, sourceTx));
        Write(c, tx, ReadRows<EnergyMarketPrice>(source, sourceTx));
        using var command = source.CreateCommand();
        command.Transaction = sourceTx;
        command.CommandText = "SELECT tariff_id, name, content FROM energy_tariff_documents";
        using var reader = command.ExecuteReader();
        while (reader.Read()) WriteEnergyDocument(c, tx, new(Guid.Parse(reader.GetString(0)), reader.GetString(1), (byte[])reader[2]));
        CheckEnergyDocuments(c, tx, settings);
    }

    private static string Iso(DateTime time) => time.ToString("O", CultureInfo.InvariantCulture);
}
