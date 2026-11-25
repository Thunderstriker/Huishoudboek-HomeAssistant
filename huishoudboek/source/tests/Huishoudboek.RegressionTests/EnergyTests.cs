using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Shared;
using Microsoft.Data.Sqlite;

internal static class EnergyTests
{
    internal const string RemoveSchema20Tables = "DROP TABLE EnergyContract; DROP TABLE EnergyTariff; DROP TABLE EnergyIndexValue; DROP TABLE EnergyReading; DROP TABLE EnergyMarketPrice; DROP TABLE energy_tariff_documents;";
    private const string Ean = "000000000000000001";
    private const string Header = "EAN-code;Meternummer;Datum;Starttijd;Eindtijd;Register;Volume;Eenheid;Validatiestatus;Omschrijving";
    private static readonly DateOnly November = new(2026, 11, 1);

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("Energy uses selected day/night or single scheme, verified index in cents and historical versions", () =>
        {
            var c = Contract(); var t = Tariff(c);
            var second = t with { Id = Guid.NewGuid(), From = November.AddDays(1), Billing = EnergyBilling.Single,
                ConsumptionRate = .1691m, InjectionCoefficient = .0428m, InjectionOffset = -.5467m };
            var i = new EnergyIndexValue { Series = "Luminus Belpex", Month = November, Source = "Synthetic publication", EuroPerMwh = 65.33m };
            var r = new[] { Reading(November, 0, 1), Reading(November, 15, 2, EnergyRegister.Night), Reading(November.AddDays(1), 0, 1),
                Reading(November.AddDays(1), 15, 2, EnergyRegister.Night), Reading(November.AddDays(1), 30, 2, direction: EnergyDirection.Injection) };
            var settings = new EnergySettings([c], [t, second], [i]);
            EnergyTerms.Validate(settings);
            var report = EnergyCosts.Calculate(settings, c, November, November.AddMonths(1), r, []);
            Equal<decimal?>(.4799m, report.Days[0].Consumption.Amount);
            Equal<decimal?>(.5073m, report.Days[1].Consumption.Amount);
            Equal<decimal?>(.04498848m, report.Injection.Amount);
            Equal<decimal?>(.1691m, report.Days[1].Consumption.WeightedPrice);
            Equal<decimal?>(null, report.Days[2].Consumption.Kwh);
            Equal(30 * 96, report.Expected); Equal(4, report.Consumption.Intervals);
            var missing = EnergyCosts.Calculate(settings with { Indexes = [] }, c, November, November.AddDays(1), r, []);
            Equal<decimal?>(2m, missing.Injection.Kwh); Equal<decimal?>(null, missing.Injection.Amount); Equal(1, missing.Injection.UnpricedIntervals);
            var zero = EnergyCosts.Calculate(settings with { Indexes = [] }, c, November, November.AddDays(1), [r[^1] with { Quantity = 0 }], []);
            Equal<decimal?>(0m, zero.Injection.Kwh); Equal<decimal?>(0m, zero.Injection.Amount); Equal(1, zero.Injection.UnpricedIntervals);
            Throws<InvalidDataException>(() => EnergyTerms.Validate(settings with { Tariffs = [t with { Through = second.From }, second] }));
        });
        yield return ("Energy fixed fee distinguishes first-year charge from estimated allocation and later proration", () =>
        {
            var c = Contract() with { SupplyStart = new(2025, 1, 1), SupplyEnd = new(2025, 12, 1) };
            var t = Tariff(c) with { From = c.SupplyStart.Value, FeeAmount = 365m, FeeRule = EnergyFeeRule.FullFirstYearThenProRata };
            var early = EnergyTerms.Windows(c, [t]).ToArray();
            Equal<decimal?>(365m, EnergyTerms.FirstYearFeeOnEarlyEnd(c, early));
            Equal<decimal?>(1m, EnergyTerms.AllocatedFee(early.Single(), new(2025, 2, 1)));
            var later = c with { SupplyEnd = new(2026, 11, 2) };
            var report = EnergyCosts.Calculate(new([later], [t], []), later, November, November.AddDays(29), [Reading(November, 0, 0)], []);
            Equal<decimal?>(null, report.EarlyEndFee); Equal<decimal?>(2m, report.AllocatedFee); Equal(2, report.FeeDays);
            var noData = EnergyCosts.Calculate(new([later], [t], []), later, November, November.AddDays(29), [], []);
            True(!noData.HasReadings); Equal<decimal?>(null, noData.PartialSubtotal);
        });
        yield return ("Energy dynamic prices preserve negative and zero values and match only the configured time unit", () =>
        {
            var c = Contract(); var t = Tariff(c) with { Pricing = EnergyPricing.Dynamic, MarketSeries = EnergyMarketSeries.BelgianDayAhead, MarketCoefficient = .1m,
                MarketOffset = 2m, MarketNightCoefficient = .1m, MarketNightOffset = 0m, FeeAmount = null, Settlement = EnergySettlement.Hour };
            var start = BelgianEnergyTime.MidnightUtc(November);
            var p = Price(start, 60, -10m);
            var r = new[] { Reading(November, 15, 2), Reading(November, 30, 1, EnergyRegister.Night), Reading(November, 60, 2) };
            var wrong = Price(start.AddHours(1), 15, 0m);
            var report = EnergyCosts.Calculate(new([c], [t], []), c, November, November, r, [p, wrong]);
            Equal<decimal?>(.01m, report.Consumption.Amount); Equal(1, report.Consumption.UnpricedIntervals);
            var zero = EnergyCosts.Calculate(new([c], [t], []), c, November, November, r, [p, Price(start.AddHours(1), 60, 0m)]);
            Equal<decimal?>(.05m, zero.Consumption.Amount); Equal(0, zero.Consumption.UnpricedIntervals);
            var w = EnergyTerms.Windows(c, [t]).Single();
            var published = EnergyCosts.Published(w, November, [Price(start, 15, -100m)]);
            Equal(96, published.Count); Equal<decimal?>(-100m, published[0].Market?.EuroPerMwh); Equal<decimal?>(null, published[0].DayRate);
            Equal(0, EnergyCosts.Published(w with { Tariff = t with { Pricing = EnergyPricing.Fixed } }, November, [p]).Count);
        });
        yield return ("Energy market response validates zone, intervals, unit, license and unpublished rows", () =>
        {
            const string response = """
                {"endpoint":"price","bidding_zone":"BE","timezone":"Europe/Brussels","unit":"EUR / MWh","interval_minutes":15,
                 "generated_at":"2026-11-25T10:00:00+01:00","license":"CC BY 4.0 from Bundesnetzagentur | SMARD.de", "data":[
                 {"timestamp":"2026-11-01T00:00:00+01:00","values":{"day_ahead_price":-22.15}},
                 {"timestamp":"2026-11-01T00:15:00+01:00","values":{"day_ahead_price":0}},
                 {"timestamp":"2026-11-01T00:30:00+01:00","values":{"day_ahead_price":null}}]}
                """;
            IReadOnlyList<EnergyMarketPrice> Parse(string input)
            {
                using var json = JsonDocument.Parse(input);
                return EnergyChartsPrices.Parse(json.RootElement, November, November, DateTime.UtcNow);
            }
            var prices = Parse(response);
            Equal(2, prices.Count); Equal(-22.15m, prices[0].EuroPerMwh); Equal(0m, prices[1].EuroPerMwh);
            Equal(BelgianEnergyTime.MidnightUtc(November), prices[0].StartUtc); Equal(DateTimeKind.Utc, prices[0].SourceGeneratedUtc!.Value.Kind);
            Throws<InvalidDataException>(() => Parse(response.Replace("\"BE\"", "\"NL\"")));
            Throws<InvalidDataException>(() => Parse(response.Replace("CC BY 4.0", "No reuse")));
            Throws<InvalidDataException>(() => Parse(response.Replace("00:15:00+01:00", "00:00:00+01:00")));
            Equal(2, EnergyChartsPrices.Requests(new(2025, 9, 29), new(2025, 10, 2)).Count());
        });
        yield return ("Energy CSV safely parses spreadsheet EAN, decimal comma, actual zeros and both DST transitions", () =>
        {
            var spring = Csv(Row("29-03-2026", "01:45", "03:00", "0,0012"), Row("29-03-2026", "02:00", "02:15", "1"),
                Row("29-03-2026", "23:45", "00:00", "0", "Injectie Nacht"),
                Row("29-03-2026", "22:45", "23:00", "1", point: "=1+1"));
            Equal(2, spring.Readings.Count); Equal(2, spring.IssueCount); Equal(.0012m, spring.Readings[0].Quantity);
            Equal(0m, spring.Readings[1].Quantity); Equal(EnergyDirection.Injection, spring.Readings[1].Direction);
            Equal(92, BelgianEnergyTime.Quarters(new(2026, 3, 29))); Equal(100, BelgianEnergyTime.Quarters(new(2026, 10, 25)));
            var fall = Csv(Row("25-10-2026", "01:45", "02:00", "1"), Row("25-10-2026", "02:00", "02:15", "1"),
                Row("25-10-2026", "02:45", "02:00", "1"), Row("25-10-2026", "02:45", "03:00", "1"));
            Equal(3, fall.Readings.Count); Equal(EnergyCsvProblem.AmbiguousTime, fall.Issues.Single().Problem);
            True(fall.Readings.All(r => r.EndUtc - r.StartUtc == TimeSpan.FromMinutes(15)));
            var r = EnergyCosts.Calculate(new([Contract()], [], []), Contract(), new(2026, 10, 1), new(2026, 10, 31), fall.Readings, []);
            Equal(31 * 96 + 4, r.Expected); Equal(3, r.Days.Single(d => d.Day.Day == 25).Consumption.Intervals);
        });
        yield return ("Energy accepts actual Fluvius heading spellings and distinguishes explicit and empty zero statuses", () =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', new[] {
                "Van (datum);Van (tijdstip);Tot (datum);Tot (tijdstip);EAN-code;Meter;Metertype;Register;Volume;Eenheid;Validatiestatus;Omschrijving",
                $"01-11-2026;23:45:00;02-11-2026;00:00:00;=\"{Ean}\";meter-a;Elektriciteit;Afname Dag;0,125;kWh;Uitgelezen;Kwartier",
                $"01-11-2026;23:45:00;02-11-2026;00:00:00;=\"{Ean}\";meter-a;Elektriciteit;Injectie Dag;0;kWh;Geen verbruik;Kwartier",
                $"02-11-2026;00:00:00;02-11-2026;00:15:00;=\"{Ean}\";meter-a;Elektriciteit;Injectie Nacht;;kWh;Geen verbruik;Kwartier" })));
            var parsed = EnergyCsv.Read(stream);
            Equal(2, parsed.Readings.Count); Equal(0m, parsed.Readings[1].Quantity); Equal(EnergyCsvProblem.Quantity, parsed.Issues.Single().Problem);
            Equal(new DateOnly(2026, 11, 2), BelgianEnergyTime.Day(parsed.Readings[0].EndUtc));
        });
        yield return ("Energy CSV import atomically checks duplicate, correction, overlapping meter and revision", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var store = new SqliteHouseholdStore(Path.Combine(directory, "huishoudboek.db"));
            var first = Row("01-11-2026", "00:00", "00:15", "1,25");
            var second = Row("01-11-2026", "00:00", "00:15", "0", "Injectie Nacht");
            var original = Csv(first, second, first);
            Equal(1, original.DuplicateRows);
            coordinator.CommitEnergy(coordinator.PreviewEnergy(original), false);
            var same = coordinator.PreviewEnergy(Csv(first, second)); Equal(0, same.Changes.Count); Equal(2, same.Unchanged);
            var correction = Csv(first.Replace("1,25", "2,50"), Row("01-11-2026", "00:15", "00:30", "0,3"));
            var preview = coordinator.PreviewEnergy(correction);
            Equal(1, preview.Conflicts); Equal(2, preview.Changes.Count); Equal(2, preview.Changes.First().After.Count);
            Throws<InvalidOperationException>(() => coordinator.CommitEnergy(preview, false));
            Equal(2, store.ReadEnergyReadings(Ean, November, November.AddMonths(1)).Count);
            coordinator.CommitEnergy(preview, true);
            Throws<AdministrationConflictException>(() => coordinator.CommitEnergy(same, false));
            var values = store.ReadEnergyReadings(Ean, November, November.AddMonths(1));
            Equal(3, values.Count); Equal(2.5m, values.Single(r => r.StartUtc == values[0].StartUtc && r.Direction == EnergyDirection.Consumption).Quantity);
            var switched = coordinator.PreviewEnergy(Csv(first.Replace("meter-a", "meter-b")));
            Equal(1, switched.Conflicts); Equal(2, switched.Changes.Single().Before.Count); Equal(1, switched.Changes.Single().After.Count);
            var innerConflict = Csv(first, first.Replace("1,25", "2")); Equal(0, innerConflict.Readings.Count); Equal(2, innerConflict.IssueCount);
        }));
        yield return ("Energy market plans and publication views are gated by consumption, not monthly injection", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var c = Contract(); var t = Tariff(c) with { MarketSeries = EnergyMarketSeries.BelgianDayAhead };
            var view = coordinator.ReadEnergy(null, November, November);
            coordinator.SaveEnergy(view.Revision, c, t, null, false);
            foreach (var pricing in new[] { EnergyPricing.Fixed, EnergyPricing.Variable })
            {
                view = coordinator.ReadEnergy(c.Id, November, November);
                coordinator.SaveEnergy(view.Revision, c, t with { Pricing = pricing }, null, false);
                view = coordinator.ReadEnergy(c.Id, November, November);
                True(!view.CanFetchHistorical); Equal(0, view.Published.Count);
                Equal(0, coordinator.PlanEnergyFetch(view.Revision, c.Id, November, true, November).Count);
            }
            var next = t with { Id = Guid.NewGuid(), Pricing = EnergyPricing.Dynamic, From = November.AddDays(1) };
            coordinator.SaveEnergy(view.Revision, c, next, null, false);
            view = coordinator.ReadEnergy(c.Id, November, November);
            Equal(November.AddDays(1), view.Published.Single().Day); True(!view.CanFetchHistorical);
            Equal((November.AddDays(1), November.AddDays(1)), coordinator.PlanEnergyFetch(view.Revision, c.Id, November, true, November).Single());
            True(view.Published.Single().Slots.All(p => p.Market == null));
        }));
        yield return ("Energy BasicFix PDF prefill extracts labeled rates and formulas, never the illustrative index", () =>
        {
            string[] pages = ["""
                BasicFix Elektriciteit                                      Regio: Vlaanderen
                (juli 2025)                                                Tarief: vast
                6% BTW
                Luminus energieprijzen (incl. BTW)¹:
                           Enkelvoudige       Tweevoudige
                                             Piekuren Daluren
                Energieprijs (c€/kWh)²         16,91   21,55   13,22  13,22
                Vaste vergoeding (€/jaar)      25,00          25,00   -
                BTW bedraagt 0%.
                """, """
                Index (excl. BTW): Belpex = 65,33 €/MWh (voorbeeld); Enkelvoudige meter
                = 0,0428 x Belpex - 0,5467; Piekuren (Tweevoudige meter) = 0,0428 x Belpex - 0,5467; Daluren         andere kolom
                = 0,0428 x Belpex - 0,5467;
                Uw tarief wordt maandelijks geïndexeerd.
                (= in het eerste jaar na de start van levering), is de klant de jaarlijkse vaste vergoeding verschuldigd
                enkel aangerekend in verhouding tot de werkelijke verdere duur van onafgebroken levering
                De vaste vergoeding wordt aangerekend op de afrekenings- of slotfactuur.
                """];
            var card = LuminusTariffCard.Parse(pages)!;
            Equal<DateOnly?>(new DateOnly(2025, 7, 1), card.IssueMonth); Equal<decimal?>(.1691m, card.SingleRate);
            Equal<decimal?>(.2155m, card.DayRate); Equal<decimal?>(.1322m, card.NightRate); Equal<decimal?>(25m, card.DayNightAnnualFee);
            Equal<decimal?>(6m, card.ConsumptionVat); Equal<decimal?>(0m, card.InjectionVat);
            Equal<(decimal, decimal)?>((.0428m, -.5467m), card.SingleInjection); Equal(card.SingleInjection, card.DayInjection); Equal(card.SingleInjection, card.NightInjection);
            True(card.FirstYearRule && card.SettlementBill);
            var wrapped = LuminusTariffCard.Parse([pages[0].Replace("(c€/kWh)²         ", "(c€/kWh)²         footnote\n                            "),
                pages[1].Replace("Daluren         andere kolom", "Daluren andere kolom").Replace("Enkelvoudige meter\n", "Enkelvoudige meter andere kolom\n")])!;
            Equal(card.DayRate, wrapped.DayRate); Equal(card.SingleInjection, wrapped.SingleInjection); Equal(card.NightInjection, wrapped.NightInjection);
            Equal<decimal?>(null, LuminusTariffCard.Parse([pages[0].Replace("Energieprijs", "Onbekende rij")])!.NightRate);
            True(LuminusTariffCard.Parse([pages[0].Replace("Vlaanderen", "Wallonië")]) == null);
        });
        yield return ("Energy schema 19 migration takes safety copy; SQLite backup restores terms, readings, prices and optional PDF without bank changes", () => InDirectory(directory =>
        {
            var path = Path.Combine(directory, "household.db"); var initial = new SqliteHouseholdStore(path);
            var bank = HouseholdData.CreateEmpty(); var owner = new Owner { Name = "Synthetic owner" }; bank.Owners.Add(owner);
            var account = new Account { Name = "Synthetic account", OwnerIds = [owner.Id], OpeningDate = November };
            bank.Accounts.Add(account); bank.Transactions.Add(new() { AccountId = account.Id, Amount = -123.45m, Date = November, Description = "Synthetic payment" });
            var subscription = new SubscriptionContract { Name = "Synthetic link", Provider = "Synthetic provider", Amount = 0 };
            bank.Contracts.Add(subscription); initial.Save(bank);
            var baseline = JsonSerializer.Serialize(initial.Load());
            Sql(path, RemoveSchema20Tables + "ALTER TABLE [SubscriptionContract] DROP COLUMN [IsIndefinite]; PRAGMA user_version=19;");
            var store = new SqliteHouseholdStore(path, allowCreate: false);
            var old = Directory.GetFiles(directory, $"*.voor-versie-{SqliteHouseholdStore.SchemaVersion}-*.db").Single();
            Equal(baseline, JsonSerializer.Serialize(SqliteHouseholdStore.ReadBackup(old)));
            Equal(0, store.ReadEnergySettings().Contracts.Count);
            var c = Contract() with { SubscriptionId = subscription.Id }; var t = Tariff(c) with { DocumentName = "synthetic.pdf" };
            var bytes = Encoding.UTF8.GetPreamble().Concat("%PDF-1.4\n synthetic"u8.ToArray()).ToArray();
            var i = new EnergyIndexValue { Series = "Luminus Belpex", Month = November, Source = "Synthetic source", EuroPerMwh = -12.345678m };
            store.SaveEnergySettings(new([c], [t], [i]), EnergyPdf.Create(t.Id, t.DocumentName, bytes));
            store.SaveEnergyPrices([Price(BelgianEnergyTime.MidnightUtc(November), 15, -1.11m)]);
            store.CommitEnergyImport(Csv(Row("01-11-2026", "00:00", "00:15", "0,1234")), false);
            Equal(baseline, JsonSerializer.Serialize(store.Load()));
            store.Save(store.Load()); // A later financial save must not delete energy history.
            var backup = Path.Combine(directory, "backup.db"); store.Backup(backup);
            var target = new SqliteHouseholdStore(Path.Combine(directory, "target.db")); target.Restore(backup);
            Equal(baseline, JsonSerializer.Serialize(target.Load()));
            Equal(c, target.ReadEnergySettings().Contracts.Single()); Equal(t, target.ReadEnergySettings().Tariffs.Single()); Equal(i, target.ReadEnergySettings().Indexes.Single());
            Equal(.1234m, target.ReadEnergyReadings(Ean, November, November.AddMonths(1)).Single().Quantity);
            Equal(-1.11m, target.ReadEnergyPrices(November, November.AddMonths(1)).Single().EuroPerMwh);
            True(bytes.SequenceEqual(target.ReadEnergyDocument(t.Id)!.Content));
            target.Restore(old); Equal(0, target.ReadEnergySettings().Contracts.Count); Equal<EnergyTariffDocument?>(null, target.ReadEnergyDocument(t.Id));
            Equal(0, target.ReadEnergyReadings(Ean, November, November.AddMonths(1)).Count);
        }));
        yield return ("Energy cost UI translates coverage and labels all incomplete charges as a partial subtotal", () =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                var c = Contract(); var r = EnergyCosts.Calculate(new([c], [Tariff(c)], []), c, November, November, [Reading(November, 0, 0)], []);
                foreach (var name in new[] { "nl-NL", "en-US" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                    var html = WebUtility.HtmlDecode(UxTests.Render<EnergyCostTable>(new WebState(null!), new Dictionary<string, object?> { ["Report"] = r }));
                    True(html.Contains(UiCulture.Text("EnergyPartialSubtotal"))); True(html.Contains(UiCulture.Text("EnergyOtherChargesMissing"))); True(html.Contains("1/96"));
                }
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        });
    }

    private static EnergyContract Contract() => new() { Ean = Ean, Supplier = "Synthetic supplier", Product = "Synthetic fixed product", SupplyStart = November };
    private static EnergyTariff Tariff(EnergyContract c) => new() { ContractId = c.Id, From = November, Billing = EnergyBilling.DayNight, ConsumptionVat = EnergyVat.Included,
        ConsumptionRate = .2155m, ConsumptionNightRate = .1322m, Injection = EnergyInjection.MonthlyIndex, InjectionIndex = "Luminus Belpex",
        InjectionVat = EnergyVat.Excluded, InjectionVatPercent = 0, FeeAmount = 25m, FeeVat = EnergyVat.Included, FeeRule = EnergyFeeRule.FullFirstYearThenProRata };
    private static EnergyMarketPrice Price(DateTime start, int minutes, decimal rate) => new() { StartUtc = start, EndUtc = start.AddMinutes(minutes), EuroPerMwh = rate, FetchedUtc = DateTime.UtcNow };
    private static EnergyReading Reading(DateOnly day, int minutes, decimal amount, EnergyRegister register = EnergyRegister.Day, EnergyDirection direction = EnergyDirection.Consumption)
    {
        var start = BelgianEnergyTime.MidnightUtc(day).AddMinutes(minutes);
        return new() { Ean = Ean, Meter = "meter-a", StartUtc = start, EndUtc = start.AddMinutes(15), Quantity = amount, Status = "Gevalideerd", Register = register, Direction = direction };
    }
    private static string Row(string day, string from, string end, string amount, string register = "Afname Dag", string? point = null) =>
        $"{point ?? $"=\"{Ean}\""};meter-a;{day};{from};{end};{register};{amount};kWh;Gevalideerd;\"Kwartier; test\"";
    private static EnergyCsvFile Csv(params string[] rows)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', rows.Prepend(Header))));
        return EnergyCsv.Read(stream);
    }
    private static void Sql(string path, string sql)
    {
        using var c = new SqliteConnection($"Data Source={path};Pooling=False"); c.Open();
        using var command = c.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }
    private static void InDirectory(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"huishoudboek-energy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { test(directory); } finally { Directory.Delete(directory, recursive: true); }
    }
    private static void True(bool condition) { if (!condition) throw new Exception("Expected true."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
