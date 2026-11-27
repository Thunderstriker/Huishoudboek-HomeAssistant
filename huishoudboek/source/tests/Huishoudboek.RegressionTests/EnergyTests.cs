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
    private const string RemoveSchema24RatesAndPeaks = "DROP TABLE EnergySurchargeRates; DROP TABLE EnergyMonthlyPeak;";
    internal const string RemoveSchema23Charges = RemoveSchema24RatesAndPeaks + "DROP TABLE EnergyMonthlyCharge;";
    internal const string RemoveSchema20Tables = RemoveSchema23Charges + "DROP TABLE EnergyContract; DROP TABLE EnergyTariff; DROP TABLE EnergyIndexValue; DROP TABLE EnergyReading; DROP TABLE EnergyMarketPrice; DROP TABLE energy_tariff_documents;";
    internal const string RemoveSchema22Resolution = "DROP INDEX ix_energy_reading_slot; ALTER TABLE EnergyReading DROP COLUMN Resolution; " +
        "CREATE UNIQUE INDEX ix_energy_reading_slot ON EnergyReading(Ean, StartUtc, Direction, Register);";
    private const string Ean = "000000000000000001";
    private const string Header = "EAN-code;Meternummer;Datum;Starttijd;Eindtijd;Register;Volume;Eenheid;Validatiestatus;Omschrijving";
    private const string FluviusHeader = "Van (datum);Van (tijdstip);Tot (datum);Tot (tijdstip);EAN-code;Meter;Metertype;Register;Volume;Eenheid;Validatiestatus;Omschrijving";
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
        yield return ("Fluvius charges and levies add once per EAN and month, not to daily costs", () =>
        {
            var june = new DateOnly(2026, 6, 1);
            var july = june.AddMonths(1);
            var c = Contract() with { SupplyStart = june };
            var t = Tariff(c) with { From = june, ConsumptionRate = 1m, FeeAmount = 30m, FeePeriod = EnergyFeePeriod.Monthly,
                FeeRule = EnergyFeeRule.ProRata, Injection = EnergyInjection.Fixed, InjectionRate = 1m };
            var other = Contract() with { Ean = "000000000000000002" };
            var readings = new[] { Daily(june, 92.11m), Daily(june, 2m, direction: EnergyDirection.Injection) };
            var charges = new EnergyMonthlyCharge { Ean = c.Ean, Month = june, Amount = 14.37m };
            var settings = new EnergySettings([c, other], [t], []) { MonthlyCharges = [charges, charges with { Id = Guid.NewGuid(), Month = july, Amount = 13.89m }] };
            EnergyTerms.Validate(settings);

            var baseline = EnergyCosts.Calculate(settings with { MonthlyCharges = [] }, c, june, july, readings, []);
            var report = EnergyCosts.Calculate(settings, c, june, july, readings, []);
            Equal<decimal?>(120.11m, baseline.PartialSubtotal);
            Equal<decimal?>(134.48m, report.PartialSubtotal);
            Equal<decimal?>(14.37m, report.MonthlyCharges);
            True(report.Days.SequenceEqual(baseline.Days));

            var julyReport = EnergyCosts.Calculate(settings, c, july, july, [], []);
            True(!julyReport.HasReadings);
            Equal<decimal?>(13.89m, julyReport.PartialSubtotal);
            Equal<decimal?>(null, EnergyCosts.Calculate(settings, c, july.AddMonths(1), july.AddMonths(1), [], []).MonthlyCharges);
            Equal<decimal?>(null, EnergyCosts.Calculate(settings, other, june, july, [], []).MonthlyCharges);
            Throws<InvalidDataException>(() => EnergyTerms.Validate(settings with { MonthlyCharges = [charges, charges with { Id = Guid.NewGuid() }] }));
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
        yield return ("Fluvius daily totals follow Brussels calendar days, including the winter-clock export convention", () =>
        {
            var spring = FluviusRow("29-03-2026", "30-03-2026", "7,811");
            var fall = FluviusRow("25-10-2026", "25-10-2026", "8,787", until: "23:00:00");
            var zero = FluviusRow("01-11-2026", "02-11-2026", "0,000", "Injectie Nacht", "Geen verbruik");
            var missing = FluviusRow("01-11-2026", "02-11-2026", "", "Afname Nacht", "Geen verbruik");
            var badEnd = FluviusRow("26-10-2026", "26-10-2026", "8,787", until: "23:00:00");
            var sameMidnight = FluviusRow("01-11-2026", "01-11-2026", "1");
            var quarter = FluviusRow("01-11-2026", "01-11-2026", "2", "Injectie Nacht", until: "00:15:00");
            var parsed = ReadCsv(FluviusHeader, spring, fall, zero, missing, badEnd, sameMidnight, quarter, fall);
            Equal(8, parsed.Rows); Equal(1, parsed.DuplicateRows); Equal(4, parsed.Readings.Count); Equal(3, parsed.IssueCount);
            True(parsed.Issues.Select(i => i.Problem).SequenceEqual([EnergyCsvProblem.MissingQuantity, EnergyCsvProblem.Time, EnergyCsvProblem.Time]));
            True(parsed.Readings.Take(3).All(r => r.Resolution == EnergyReadingResolution.DailyTotal && EnergyTerms.ValidReading(r)));
            Equal(23d, (parsed.Readings[0].EndUtc - parsed.Readings[0].StartUtc).TotalHours);
            Equal(25d, (parsed.Readings[1].EndUtc - parsed.Readings[1].StartUtc).TotalHours);
            Equal(BelgianEnergyTime.MidnightUtc(new DateOnly(2026, 10, 26)), parsed.Readings[1].EndUtc);
            Equal(8.787m, parsed.Readings[1].Quantity); Equal(0m, parsed.Readings[2].Quantity);
            Equal(EnergyDirection.Injection, parsed.Readings[2].Direction); Equal(EnergyRegister.Night, parsed.Readings[2].Register);
            Equal(EnergyReadingResolution.QuarterHour, parsed.Readings[3].Resolution);
            var conflicting = ReadCsv(FluviusHeader, fall, fall.Replace("8,787", "8,789"));
            Equal(0, conflicting.Readings.Count); Equal(2, conflicting.IssueCount);
        });
        yield return ("Daily totals fill quarter coverage gaps without double counting and retain independent register rates", () =>
        {
            var c = Contract(); var t = Tariff(c) with { InjectionCoefficient = .05m, InjectionOffset = 0, InjectionNightCoefficient = .02m, InjectionNightOffset = 0 };
            var i = new EnergyIndexValue { Series = t.InjectionIndex, Month = November, Source = "Synthetic publication", EuroPerMwh = 100m };
            var day2 = November.AddDays(1); var day3 = November.AddDays(2);
            var readings = new[] { Reading(November, 0, 1), Daily(November, 5), Daily(November, 3, EnergyRegister.Night),
                Daily(November, 3, direction: EnergyDirection.Injection), Daily(November, 2, EnergyRegister.Night, EnergyDirection.Injection),
                Daily(day2, 20), Daily(day2, 30, EnergyRegister.Night), Daily(day3, 2) with { Status = "Geschat" } }
                .Concat(Enumerable.Range(0, 96).Select(q => Reading(day2, q * 15, .1m)));
            var report = EnergyCosts.Calculate(new([c], [t], [i]), c, November, November.AddDays(3), readings, []);
            True(report.HasReadings);
            Equal<decimal?>(8m, report.Days[0].Consumption.Kwh); Equal<decimal?>(1.4741m, report.Days[0].Consumption.Amount);
            Equal<decimal?>(5m, report.Days[0].Injection.Kwh); Equal<decimal?>(.19m, report.Days[0].Injection.Amount);
            Equal<decimal?>(9.6m, report.Days[1].Consumption.Kwh); Equal(0, report.Days[1].Consumption.DailyTotals);
            Equal(2, report.Consumption.DailyTotals); Equal(1, report.Consumption.PartialDailyTotals); Equal(1, report.Consumption.EstimatedDailyTotals);
            Equal(97, report.Consumption.Intervals); Equal(0, report.Consumption.UnpricedDailyTotals);
            Equal<decimal?>(19.6m, report.Consumption.Kwh); Equal<decimal?>(null, report.Days[3].Consumption.Kwh);
        });
        yield return ("Dynamic tariffs cannot price daily totals even when a midnight market price exists", () =>
        {
            var c = Contract(); var t = Tariff(c) with { Pricing = EnergyPricing.Dynamic, MarketSeries = EnergyMarketSeries.BelgianDayAhead,
                Settlement = EnergySettlement.QuarterHour, MarketCoefficient = .1m, MarketOffset = 2m };
            var market = Price(BelgianEnergyTime.MidnightUtc(November), 15, 100m);
            var daily = new[] { Daily(November, 5), Daily(November, 3, EnergyRegister.Night) };
            var settings = new EnergySettings([c], [t], []);
            var report = EnergyCosts.Calculate(settings, c, November, November, daily, [market]);
            True(report.HasReadings); Equal(0, report.Consumption.Intervals); Equal(1, report.Consumption.UnpricedDailyTotals);
            Equal<decimal?>(8m, report.Consumption.Kwh); Equal<decimal?>(null, report.Consumption.Amount);
            var overlap = EnergyCosts.Calculate(settings, c, November, November, daily.Append(Reading(November, 0, 2)), [market]);
            Equal<decimal?>(8m, overlap.Consumption.Kwh); Equal<decimal?>(.24m, overlap.Consumption.Amount);
            Equal(2m, overlap.Consumption.PricedKwh); Equal(1, overlap.Consumption.UnpricedDailyTotals);
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
        yield return ("Energy year overview compares the selected EAN across supplier changes and prices the correct months", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var june = new DateOnly(2026, 6, 1);
            var july = june.AddMonths(1);
            var today = july.AddDays(1);
            var fixedContract = Contract() with { SupplyStart = june, SupplyEnd = july.AddDays(-1) };
            var dynamicContract = Contract() with { SupplyStart = july };
            var otherContract = Contract() with { Ean = "000000000000000002", SupplyStart = june };
            var view = coordinator.ReadEnergy(null, june, today);
            coordinator.SaveEnergy(view.Revision, fixedContract, Tariff(fixedContract) with { From = june, ConsumptionRate = .50m, FeeAmount = null }, null, false);
            view = coordinator.ReadEnergy(fixedContract.Id, june, today);
            coordinator.SaveEnergy(view.Revision, dynamicContract, Tariff(dynamicContract) with { From = july, Pricing = EnergyPricing.Dynamic, FeeAmount = null,
                MarketSeries = EnergyMarketSeries.BelgianDayAhead, MarketCoefficient = .1m, MarketOffset = 2m }, null, false);
            view = coordinator.ReadEnergy(dynamicContract.Id, july, today);
            coordinator.SaveEnergy(view.Revision, otherContract, null, null, false);
            var readings = new[] { Daily(june, 10), Reading(july, 0, 4), Daily(june, 100) with { Ean = otherContract.Ean } };
            coordinator.CommitEnergy(coordinator.PreviewEnergy(new(readings, readings.Length, 0, 0, [])), false);
            view = coordinator.ReadEnergy(dynamicContract.Id, june, today);
            coordinator.SaveEnergyPrices(view.Revision, [Price(BelgianEnergyTime.MidnightUtc(july), 15, 100m)]);
            coordinator.SaveEnergyMonthlyCharge(view.Revision, fixedContract.Ean, june, 14.37m);

            view = coordinator.ReadEnergy(dynamicContract.Id, june, today);
            True(view.Months.Select(m => m.Month.Month).SequenceEqual(Enumerable.Range(1, 7)));
            Equal<decimal?>(null, view.Months[0].Costs.PartialSubtotal);
            Equal<decimal?>(19.37m, view.Report!.PartialSubtotal);
            True(!view.CanFetchHistorical);
            var julyView = view with { Month = july };
            Equal<decimal?>(.48m, julyView.Report!.PartialSubtotal);
            Equal(2, julyView.Report.Days.Count);
            True(julyView.CanFetchHistorical);
            Equal(12, coordinator.ReadEnergy(fixedContract.Id, june.AddYears(-1), today).Months.Count);
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
        yield return ("Energy BasicFix PDF prefill supports the shared residential layout without taking regional or example charges", () =>
        {
            string[] pages = ["""
                Luminus BasicFix Elektriciteit                                         Verbruikers: residentieel
                                                                                       Tarief: vast
                (augustus 2025)
                6% BTW
                Luminus energieprijzen (incl. BTW)1:
                                           Enkelvoudige     Tweevoudige          Exclusief
                                               meter            meter            nacht meter       VL     WAL
                                                             Dag       Nacht
                Geleverde energie (c€/kWh)     17,91        22,55      14,22       16,70         4,21     5,22
                Vergoeding geïnjecteerde energie (c€/kWh)    2,25       2,26        2,27
                Vaste vergoeding (€/jaar)      26,00             27,00              -
                BTW bedraagt 0%.
                """, """
                Tariefformule voor de vergoeding van geïnjecteerde energie
                (excl. BTW):
                Index (excl. BTW): Belpex = 99,99 €/MWh (voorbeeld); Dag (dagmeter) = 0,0428              Netkosten
                x Belpex - 0,5467; Dag (dag- en nachtmeter) = 0,0333 x Belpex + 0,4444; Nacht = 0,0123 x Belpex
                - 0,4567;
                Uw tarief wordt maandelijks geïndexeerd.
                In het eerste jaar na de start van levering is de klant de jaarlijkse vaste vergoeding verschuldigd
                in verhouding tot de werkelijke verdere duur van onafgebroken levering
                De vaste vergoeding wordt aangerekend op de afrekenings- of slotfactuur.
                """];
            var card = LuminusTariffCard.Parse(pages)!;
            Equal("BasicFix Elektriciteit", card.Product); Equal<DateOnly?>(new DateOnly(2025, 8, 1), card.IssueMonth);
            Equal<decimal?>(.1791m, card.SingleRate); Equal<decimal?>(.2255m, card.DayRate); Equal<decimal?>(.1422m, card.NightRate);
            Equal<decimal?>(26m, card.SingleAnnualFee); Equal<decimal?>(27m, card.DayNightAnnualFee);
            Equal<decimal?>(6m, card.ConsumptionVat); Equal<decimal?>(0m, card.InjectionVat);
            Equal<(decimal, decimal)?>((.0428m, -.5467m), card.SingleInjection);
            Equal<(decimal, decimal)?>((.0333m, .4444m), card.DayInjection);
            Equal<(decimal, decimal)?>((.0123m, -.4567m), card.NightInjection);
            True(card.FirstYearRule && card.SettlementBill);

            True(LuminusTariffCard.Parse([pages[0] + "\nRegio: Wallonië", pages[1]]) == null);
            True(LuminusTariffCard.Parse([pages[0].Replace("Tarief: vast", "Tarief: variabel"), pages[1]]) == null);
            True(LuminusTariffCard.Parse([pages[0].Replace("Luminus BasicFix", "BasicFix"), pages[1]]) == null);
            Equal<(decimal, decimal)?>(null, LuminusTariffCard.Parse([pages[0]])!.SingleInjection);
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
        yield return ("Schema 22 keeps daily totals and midnight quarters independent through corrections and backup restore", () => InDirectory(directory =>
        {
            var path = Path.Combine(directory, "energy.db"); var initial = new SqliteHouseholdStore(path);
            var quarter = Csv(Row("01-11-2026", "00:00", "00:15", "1"));
            initial.CommitEnergyImport(quarter, false);
            Sql(path, RemoveSchema23Charges + RemoveSchema22Resolution + "PRAGMA user_version=21;");

            var store = new SqliteHouseholdStore(path, allowCreate: false);
            var old = Directory.GetFiles(directory, $"*.voor-versie-{SqliteHouseholdStore.SchemaVersion}-*.db").Single();
            var target = new SqliteHouseholdStore(Path.Combine(directory, "target.db"));
            List<EnergyReading> Read(SqliteHouseholdStore db) => db.ReadEnergyReadings(Ean, November, November.AddDays(1));
            target.Restore(old); Equal(quarter.Readings.Single(), Read(target).Single());
            Equal(quarter.Readings.Single(), Read(store).Single());

            var row = FluviusRow("01-11-2026", "02-11-2026", "5");
            var day = ReadCsv(FluviusHeader, row);
            Equal(0, store.PreviewEnergyImport(day, "").Conflicts);
            True(store.CommitEnergyImport(day, false)); True(!store.CommitEnergyImport(day, false));
            var night = ReadCsv(FluviusHeader, FluviusRow("01-11-2026", "02-11-2026", "3", "Afname Nacht"));
            True(store.CommitEnergyImport(night, false));
            var correction = ReadCsv(FluviusHeader, row.Replace(";5;", ";6;"));
            Equal(1, store.PreviewEnergyImport(correction, "").Conflicts);
            Throws<InvalidOperationException>(() => store.CommitEnergyImport(correction, false));
            True(store.CommitEnergyImport(correction, true));
            Equal(3, Read(store).Count); Equal(quarter.Readings.Single(), Read(store)[0]);
            Equal(9m, Read(store).Where(r => r.Resolution == EnergyReadingResolution.DailyTotal).Sum(r => r.Quantity));
            var quarterCorrection = Csv(Row("01-11-2026", "00:00", "00:15", "2"));
            True(store.CommitEnergyImport(quarterCorrection, true));
            Equal(3, Read(store).Count); Equal(2m, Read(store)[0].Quantity);

            var backup = Path.Combine(directory, "backup.db"); store.Backup(backup); target.Restore(backup);
            True(Read(store).SequenceEqual(Read(target)));
        }));
        yield return ("Schema 23 preserves existing history and backs up monthly charges, including zero and edits across contracts", () => InDirectory(directory =>
        {
            var path = Path.Combine(directory, "huishoudboek.db"); var initial = new SqliteHouseholdStore(path);
            var c = Contract() with { SupplyEnd = November.AddDays(14) };
            var next = Contract() with { SupplyStart = November.AddDays(15) };
            var t = Tariff(c);
            initial.SaveEnergySettings(new([c, next], [t], []));
            var day = Daily(November, 7);
            initial.CommitEnergyImport(new([day], 1, 0, 0, []), false);
            Sql(path, RemoveSchema23Charges + "PRAGMA user_version=22;");

            using var coordinator = new AdministrationCoordinator(directory, claim: true, allowUpgrade: true);
            var store = new SqliteHouseholdStore(path, allowCreate: false);
            var old = Directory.GetFiles(directory, $"*.voor-versie-{SqliteHouseholdStore.SchemaVersion}-*.db").Single();
            var view = coordinator.ReadEnergy(c.Id, November, November);
            Equal<decimal?>(null, view.Report!.MonthlyCharges);
            Equal(t, view.Settings.Tariffs.Single());
            Equal(day, store.ReadEnergyReadings(Ean, November, November.AddMonths(1)).Single());
            Throws<InvalidDataException>(() => coordinator.SaveEnergyMonthlyCharge(view.Revision, Ean, November, 14.371m));
            Throws<InvalidDataException>(() => coordinator.SaveEnergyMonthlyCharge(view.Revision, Ean, November, -1m));
            Throws<InvalidDataException>(() => coordinator.SaveEnergyMonthlyCharge(view.Revision, Ean, November.AddDays(1), 14.37m));
            coordinator.SaveEnergyMonthlyCharge(view.Revision, Ean, November, 14.37m);
            Throws<AdministrationConflictException>(() => coordinator.SaveEnergyMonthlyCharge(view.Revision, Ean, November, null));
            view = coordinator.ReadEnergy(next.Id, November, November);
            Equal<decimal?>(14.37m, view.Report!.MonthlyCharges);
            var charge = view.Settings.MonthlyCharges.Single();

            var backup = Path.Combine(directory, "backup.db"); store.Backup(backup);
            var target = new SqliteHouseholdStore(Path.Combine(directory, "target.db")); target.Restore(backup);
            Equal(charge, target.ReadEnergySettings().MonthlyCharges.Single());
            target.Restore(old);
            Equal(0, target.ReadEnergySettings().MonthlyCharges.Count);
            Equal(day, target.ReadEnergyReadings(Ean, November, November.AddMonths(1)).Single());

            coordinator.SaveEnergyMonthlyCharge(view.Revision, Ean, November, 0m);
            view = coordinator.ReadEnergy(next.Id, November, November);
            Equal<decimal?>(0m, view.Report!.MonthlyCharges);
            Equal(charge.Id, view.Settings.MonthlyCharges.Single().Id);
            coordinator.SaveEnergyMonthlyCharge(view.Revision, Ean, November, null);
            Equal(0, store.ReadEnergySettings().MonthlyCharges.Count);
        }));
        yield return ("Fluvius peak CSV keeps monthly kW and provisional status separate from energy, rejects bad periods and detects duplicates", () =>
        {
            var first = PeakRow("01-10-2026", "01-11-2026", "3,111");
            var latest = PeakRow("01-11-2026", "01-12-2026", "6,008", "Voorlopig");
            var file = ReadCsv(FluviusHeader, first, first, latest, FluviusRow("01-11-2026", "02-11-2026", "2,5"),
                PeakRow("01-09-2026", "01-10-2026", "3,25", unit: "kWh"), PeakRow("01-08-2026", "31-08-2026", "2,5"));
            Equal(2, file.Peaks.Count); Equal(1, file.Readings.Count); Equal(1, file.DuplicateRows); Equal(2, file.IssueCount);
            Equal(3.111m, file.Peaks[0].Kw); Equal(November, file.Peaks[1].Month); Equal("Voorlopig", file.Peaks[1].Status);
            var conflicting = ReadCsv(FluviusHeader, first, first.Replace("3,111", "3,112"));
            Equal(0, conflicting.Peaks.Count); True(conflicting.Issues.All(i => i.Problem == EnergyCsvProblem.ConflictingRows));
        });
        yield return ("Surcharges price gross daily consumption once; capacity uses available peak history with the monthly floor and preserves manual overrides", () =>
        {
            var c = Contract() with { SupplyStart = November.AddMonths(-12) };
            var t = Tariff(c) with { FeeAmount = null, Injection = EnergyInjection.Fixed, InjectionRate = .10m };
            var rate = new EnergySurchargeRates { Ean = Ean, From = November, Through = November.AddMonths(1).AddDays(-1), Source = "Synthetic",
                SupplierPerKwh = .0156m, NetworkPerKwh = .05m, TaxPerKwh = .04m, CapacityPerKwMonth = 4m, DataPerYear = 18.25m };
            var settings = new EnergySettings([c], [t], []) { SurchargeRates = [rate] };
            EnergyTerms.Validate(settings);
            Throws<InvalidDataException>(() => EnergyTerms.Validate(settings with { SurchargeRates = [rate, rate with { Id = Guid.NewGuid() }] }));
            EnergyReading[] readings = [Daily(November, 7), Daily(November, 3, EnergyRegister.Night), Reading(November, 0, 2),
                Daily(November, 2, direction: EnergyDirection.Injection)];
            EnergyMonthlyPeak[] peaks = [Peak(November, 3m), Peak(November.AddMonths(-1), 1m), Peak(November.AddMonths(-12), 9m),
                Peak(November, 8m) with { Ean = "000000000000000002" }];
            var report = EnergyCosts.Calculate(settings, c, November, November.AddMonths(1), readings, [], peaks);
            Equal(new EnergyPeakEstimate(3m, 2.75m, 2, false), report.Peak);
            Equal(new EnergyUsageSurcharges(.156m, .5m, .4m), report.UsageSurcharges);
            Equal<decimal?>(11m, report.CapacityCharge); Equal<decimal?>(1.5m, report.DataCharge);
            Equal(1, report.CoveredSurchargeDays);
            Equal(report.Consumption.Amount - .20m + 1.056m + 11m + 1.5m, report.PartialSubtotal);
            Equal<decimal?>(null, EnergyCosts.Calculate(settings, c, November, November.AddMonths(1), readings, [], peaks.Skip(1)).CapacityCharge);
            True(EnergyCosts.Calculate(settings, c, November, November.AddDays(20), readings, [], peaks).Peak!.Provisional);

            settings.MonthlyCharges.Add(new() { Ean = Ean, Month = November, Amount = 0m });
            var manual = EnergyCosts.Calculate(settings, c, November, November.AddMonths(1), readings, [], peaks);
            Equal(new EnergyUsageSurcharges(.156m, null, null), manual.UsageSurcharges);
            Equal<decimal?>(null, manual.CapacityCharge); Equal<decimal?>(null, manual.DataCharge);
            Equal(report.Consumption.Amount - .20m + .156m, manual.PartialSubtotal);
        });
        yield return ("Schema 24 migrates and backs up surcharge periods and peaks; confirmed peak corrections and clearing are scoped by EAN", () => InDirectory(directory =>
        {
            var path = Path.Combine(directory, "huishoudboek.db");
            var initial = new SqliteHouseholdStore(path);
            var c = Contract(); var tariff = Tariff(c);
            var manual = new EnergyMonthlyCharge { Ean = Ean, Month = November.AddMonths(-1), Amount = 12.5m };
            initial.SaveEnergySettings(new([c], [tariff], []) { MonthlyCharges = [manual] });
            Sql(path, RemoveSchema24RatesAndPeaks + "PRAGMA user_version=23;");
            using var coordinator = new AdministrationCoordinator(directory, claim: true, allowUpgrade: true);
            var store = new SqliteHouseholdStore(path, allowCreate: false);
            var old = Directory.GetFiles(directory, "*.voor-versie-24-*.db").Single();
            var view = coordinator.ReadEnergy(c.Id, November, November.AddMonths(1));
            Equal(manual, view.Settings.MonthlyCharges.Single());
            var rate = new EnergySurchargeRates { Ean = Ean, From = November, Through = November.AddMonths(1), SupplierPerKwh = .0156m, Source = "Synthetic" };
            coordinator.SaveEnergySurchargeRates(view.Revision, rate);
            Throws<AdministrationConflictException>(() => coordinator.SaveEnergySurchargeRates(view.Revision, rate, true));
            const string otherEan = "000000000000000002";
            var peak = Peak(November, 3m);
            var batch = new EnergyCsvFile([Daily(November, 1)], 3, 0, 0, []) { Peaks = [peak, Peak(November, 2m) with { Ean = otherEan }] };
            var preview = coordinator.PreviewEnergy(batch);
            Equal(3, preview.ChangeCount); Equal(0, preview.Conflicts);
            coordinator.CommitEnergy(preview, false);
            Equal(3, coordinator.PreviewEnergy(batch).Unchanged);
            var changed = new EnergyCsvFile([], 1, 0, 0, []) { Peaks = [peak with { Kw = 4m }] };
            var correction = coordinator.PreviewEnergy(changed);
            Equal(1, correction.Conflicts);
            Throws<InvalidOperationException>(() => coordinator.CommitEnergy(correction, false));
            coordinator.CommitEnergy(correction, true);
            view = coordinator.ReadEnergy(c.Id, November, November.AddMonths(1));
            Equal(4m, view.Report!.Peak!.MonthKw);

            var backup = Path.Combine(directory, "rates-peaks.db"); store.Backup(backup);
            var target = new SqliteHouseholdStore(Path.Combine(directory, "target.db")); target.Restore(backup);
            Equal(rate, target.ReadEnergySettings().SurchargeRates.Single());
            Equal(peak with { Kw = 4m }, target.ReadEnergyPeaks(Ean, November, November.AddMonths(1)).Single());
            target.Restore(old);
            Equal(0, target.ReadEnergySettings().SurchargeRates.Count); Equal(manual, target.ReadEnergySettings().MonthlyCharges.Single());
            Equal(0, target.ReadEnergyPeaks(Ean, November, November.AddMonths(1)).Count);
            Equal(2, coordinator.ClearEnergyReadings(coordinator.PreviewClearEnergyReadings(Ean)));
            Equal(0, store.CountEnergyReadings(Ean)); Equal(1, store.CountEnergyReadings(otherEan));
            Equal(rate, store.ReadEnergySettings().SurchargeRates.Single());
        }));
        yield return ("Clearing an EAN deletes all imported readings, protects other data and rejects stale confirmation", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var store = new SqliteHouseholdStore(Path.Combine(directory, "huishoudboek.db"));
            var day = new DateOnly(2026, 10, 25);
            const string otherEan = "000000000000000002";
            var before = Reading(day.AddMonths(-1), 0, 1) with { Meter = "older-meter" };
            var first = Reading(day, 0, 1);
            var last = Reading(day, 99 * 15, 1);
            var after = Reading(day.AddMonths(1), 0, 1, direction: EnergyDirection.Injection);
            var other = Daily(day, 4) with { Ean = otherEan };
            var daily = Daily(day, 7);
            coordinator.CommitEnergy(coordinator.PreviewEnergy(new([before, first, last, after, other, daily], 6, 0, 0, [])), false);

            var view = coordinator.ReadEnergy(null, new(2026, 10, 1), day.AddDays(1));
            True(view.ReadingConnections.SequenceEqual([Ean, otherEan])); // Daily-only EANs appear even without a contract.
            var c = Contract(); var t = Tariff(c);
            coordinator.SaveEnergy(view.Revision, c, t, null, false);
            view = coordinator.ReadEnergy(c.Id, November, November);
            coordinator.SaveEnergyMonthlyCharge(view.Revision, Ean, November, 13.89m);
            var cleanup = coordinator.PreviewClearEnergyReadings(Ean);
            Equal(5, cleanup.Count);
            var empty = coordinator.PreviewClearEnergyReadings("000000000000000003");
            Equal(0, empty.Count);
            Throws<InvalidOperationException>(() => coordinator.ClearEnergyReadings(empty));
            Throws<InvalidDataException>(() => coordinator.PreviewClearEnergyReadings("invalid"));
            Throws<InvalidOperationException>(() => store.ClearEnergyReadings(Ean, 4));
            Equal(5, store.CountEnergyReadings(Ean));

            var newReading = new EnergyCsvFile([Reading(day, 15, 2)], 1, 0, 0, []);
            coordinator.CommitEnergy(coordinator.PreviewEnergy(newReading), false);
            Throws<AdministrationConflictException>(() => coordinator.ClearEnergyReadings(cleanup));
            var import = coordinator.PreviewEnergy(newReading);
            Equal(6, coordinator.ClearEnergyReadings(coordinator.PreviewClearEnergyReadings(Ean)));
            Throws<AdministrationConflictException>(() => coordinator.CommitEnergy(import, false));
            Equal(0, store.CountEnergyReadings(Ean));
            Equal(other, store.ReadEnergyReadings(otherEan, day, day.AddDays(1)).Single());
            view = coordinator.ReadEnergy(c.Id, November, November.AddMonths(1));
            True(view.ReadingConnections.SequenceEqual([otherEan]));
            True(!view.Report!.HasReadings);
            Equal<decimal?>(13.89m, view.Report.PartialSubtotal);
            Equal(c, view.Settings.Contracts.Single()); Equal(t, view.Settings.Tariffs.Single());
        }));
        yield return ("Energy cost UI translates coverage and labels all incomplete charges as a partial subtotal", () =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                var c = Contract(); var r = EnergyCosts.Calculate(new([c], [Tariff(c)], []), c, November, November, [Reading(November, 0, 0)], []);
                var totals = EnergyCosts.Calculate(new([c], [Tariff(c)], []), c, November, November, [Daily(November, 5), Daily(November, 3, EnergyRegister.Night)], []);
                var chargesOnly = EnergyCosts.Calculate(new([c], [Tariff(c)], []) { MonthlyCharges = [new() { Ean = c.Ean, Month = November, Amount = 14.37m }] },
                    c, November, November, [], []);
                foreach (var name in new[] { "nl-NL", "en-US" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                    string RenderCosts(EnergyCostReport report) => WebUtility.HtmlDecode(UxTests.Render<EnergyCostTable>(new WebState(null!),
                        new Dictionary<string, object?> { ["Month"] = November, ["Report"] = report }));
                    var html = RenderCosts(r);
                    True(html.Contains(UiCulture.Text("EnergyPartialSubtotal"))); True(html.Contains(UiCulture.Text("EnergyOtherChargesMissing"))); True(html.Contains("1/96"));
                    var daily = RenderCosts(totals);
                    True(daily.Contains(UiCulture.Text("EnergyDailyCoverage", 1, 1, 0, 0, 0))); True(!daily.Contains("1/96"));
                    var charges = RenderCosts(chargesOnly);
                    True(charges.Contains(UiCulture.Text("EnergyMonthlyCharges"))); True(charges.Contains(UiCulture.Text("EnergyNoReadings")));
                    True(charges.Contains(14.37m.ToString("0.00", UiCulture.Culture)));
                    var year = WebUtility.HtmlDecode(UxTests.Render<EnergyMonthlyOverview>(new WebState(null!), new Dictionary<string, object?>
                    {
                        ["Month"] = November,
                        ["Months"] = new EnergyMonthReport[] { new(November.AddMonths(-1), totals, false), new(November, chargesOnly, false) }
                    }));
                    True(year.Contains(UiCulture.Text("EnergyYearOverview", November.Year))); True(year.Contains("aria-current=\"true\""));
                    True(year.Contains("8 kWh")); True(year.Contains("0/1")); True(year.Contains("1/1"));
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
    private static EnergyReading Daily(DateOnly day, decimal amount, EnergyRegister register = EnergyRegister.Day, EnergyDirection direction = EnergyDirection.Consumption) =>
        Reading(day, 0, amount, register, direction) with { Resolution = EnergyReadingResolution.DailyTotal, EndUtc = BelgianEnergyTime.MidnightUtc(day.AddDays(1)) };
    private static string FluviusRow(string day, string end, string amount, string register = "Afname Dag", string status = "Uitgelezen", string until = "00:00:00") =>
        $"{day};00:00:00;{end};{until};=\"{Ean}\";meter-a;Digitale meter;{register};{amount};kWh;{status};";
    private static EnergyMonthlyPeak Peak(DateOnly month, decimal kw) => new() { Ean = Ean, Meter = "meter-a", Month = month, Kw = kw, Status = "Uitgelezen" };
    private static string PeakRow(string month, string end, string amount, string status = "Uitgelezen", string unit = "kW") =>
        $"{month};00:00:00;{end};00:00:00;=\"{Ean}\";meter-a;Digitale meter;Piekvermogen;{amount};{unit};{status};";
    private static EnergyCsvFile Csv(params string[] rows) => ReadCsv(Header, rows);
    private static EnergyCsvFile ReadCsv(string header, params string[] rows)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', rows.Prepend(header))));
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
