using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;

namespace Huishoudboek.Service;

public sealed partial class AdministrationCoordinator
{
    private string _energyRevision = NewRevision();

    public EnergyView ReadEnergy(Guid? contractId, DateOnly month, DateOnly today)
    {
        CheckMonth(month);
        lock (_gate)
        {
            var settings = _store.ReadEnergySettings();
            var contract = settings.Contracts.FirstOrDefault(c => c.Id == contractId) ?? settings.Contracts
                .OrderByDescending(c => c.SupplyEnd == null || c.SupplyEnd >= today).ThenByDescending(c => c.SupplyStart).ThenBy(c => c.Supplier).FirstOrDefault();
            var readingConnections = _store.ReadEnergyReadingConnections();
            if (contract == null) return new(_energyRevision, settings, null, month, [], [], readingConnections);

            var historyStart = new DateOnly(month.Year, 1, 1);
            var historyEnd = new DateOnly(month.Year + 1, 1, 1);
            var afterCurrentMonth = MonthOf(today).AddMonths(1);
            var afterSelectedMonth = month.AddMonths(1);
            if (historyEnd > afterCurrentMonth) historyEnd = afterCurrentMonth > afterSelectedMonth ? afterCurrentMonth : afterSelectedMonth;
            var contractWindows = EnergyTerms.Windows(contract, settings.Tariffs).ToList();
            var usesMarketHistory = DynamicRanges(MeterWindows(settings, contract), historyStart, Min(historyEnd.AddDays(-1), today)).Count != 0;
            var readings = _store.ReadEnergyReadings(contract.Ean, historyStart, historyEnd).ToLookup(r => MonthOf(BelgianEnergyTime.Day(r.StartUtc)));
            var peaks = _store.ReadEnergyPeaks(contract.Ean, historyStart.AddMonths(-11), historyEnd);
            var historicalPrices = usesMarketHistory ? _store.ReadEnergyPrices(historyStart, historyEnd) : [];
            var prices = historicalPrices.ToLookup(p => MonthOf(BelgianEnergyTime.Day(p.StartUtc)));
            var months = new List<EnergyMonthReport>();
            for (var period = historyStart; period < historyEnd; period = period.AddMonths(1))
            {
                var costs = EnergyCosts.Calculate(settings, contract, period, today, readings[period], prices[period], peaks);
                var canFetch = DynamicRanges(contractWindows, period, Min(period.AddMonths(1).AddDays(-1), today)).Count != 0;
                months.Add(new(period, costs, canFetch));
            }
            var publish = new List<EnergyPublishedDay>();
            var windows = contractWindows.Where(w => w.Tariff.Pricing == EnergyPricing.Dynamic && w.From <= today.AddDays(1) && w.Through >= today).ToList();
            if (windows.Count != 0)
            {
                var market = windows.Any(w => ConfirmedDynamic(w.Tariff)) ? _store.ReadEnergyPrices(today, today.AddDays(2)) : [];
                foreach (var day in new[] { today, today.AddDays(1) })
                    if (windows.FirstOrDefault(w => w.From <= day && w.Through >= day) is { } w)
                        publish.Add(new(day, w.Tariff, EnergyCosts.Published(w, day, market)));
            }
            return new(_energyRevision, settings, contract.Id, month, months, publish, readingConnections);
        }
    }

    public void SaveEnergy(string revision, EnergyContract contract, EnergyTariff? tariff, EnergyTariffDocument? document, bool keepPdf)
    {
        lock (_gate)
        {
            CheckEnergyRevision(revision);
            var settings = _store.ReadEnergySettings();
            if (settings.Contracts.Any(c => c.Id == contract.Id && c.Ean != contract.Ean))
                throw new InvalidOperationException("Het EAN van een opgeslagen energiecontract kan niet worden gewijzigd. Voeg een nieuw contract toe voor een ander aansluitpunt.");
            var clean = contract with { Ean = contract.Ean.Trim(), Supplier = contract.Supplier.Trim(), Product = contract.Product.Trim() };
            Replace(settings.Contracts, clean, c => c.Id == contract.Id);
            if (tariff != null)
            {
                var old = settings.Tariffs.FirstOrDefault(t => t.Id == tariff.Id);
                if (old != null && old.ContractId != contract.Id) throw new InvalidOperationException("De tariefversie hoort bij een ander contract.");
                var name = keepPdf ? document?.Name ?? old?.DocumentName ?? "" : "";
                if (keepPdf && name.Length == 0) throw new InvalidOperationException("Upload eerst een PDF of zet PDF bewaren uit.");
                Replace(settings.Tariffs, tariff with { ContractId = contract.Id, DocumentName = name, InjectionIndex = tariff.InjectionIndex.Trim() }, t => t.Id == tariff.Id);
            }
            _store.SaveEnergySettings(settings, keepPdf ? document : null);
            _energyRevision = NewRevision();
        }
    }

    public void SaveEnergyIndex(string revision, EnergyIndexValue value)
    {
        lock (_gate)
        {
            CheckEnergyRevision(revision);
            var settings = _store.ReadEnergySettings();
            Replace(settings.Indexes, value with { Series = value.Series.Trim(), Source = value.Source.Trim() }, i => i.Id == value.Id);
            _store.SaveEnergySettings(settings);
            _energyRevision = NewRevision();
        }
    }

    public void SaveEnergyMonthlyCharge(string revision, string ean, DateOnly month, decimal? amount)
    {
        CheckMonth(month);
        lock (_gate)
        {
            CheckEnergyRevision(revision);
            var settings = _store.ReadEnergySettings();
            if (!settings.Contracts.Any(c => c.Ean == ean)) throw new InvalidDataException("Kies een opgeslagen energiecontract.");
            var existing = settings.MonthlyCharges.FindIndex(c => c.Ean == ean && c.Month == month);
            if (amount is { } value)
            {
                if (existing < 0) settings.MonthlyCharges.Add(new() { Ean = ean, Month = month, Amount = value });
                else if (settings.MonthlyCharges[existing].Amount == value) return;
                else settings.MonthlyCharges[existing] = settings.MonthlyCharges[existing] with { Amount = value };
            }
            else if (existing >= 0) settings.MonthlyCharges.RemoveAt(existing);
            else return;

            _store.SaveEnergySettings(settings);
            _energyRevision = NewRevision();
        }
    }

    public EnergyImportPreview PreviewEnergy(EnergyCsvFile file)
    {
        lock (_gate) return _store.PreviewEnergyImport(file, _energyRevision);
    }

    public void SaveEnergySurchargeRates(string revision, EnergySurchargeRates rates, bool remove = false)
    {
        lock (_gate)
        {
            CheckEnergyRevision(revision);
            var settings = _store.ReadEnergySettings();
            var existing = settings.SurchargeRates.FindIndex(rate => rate.Id == rates.Id);
            if (existing >= 0 && settings.SurchargeRates[existing].Ean != rates.Ean)
                throw new InvalidDataException("Een toeslagperiode hoort bij één EAN.");
            if (remove)
            {
                if (existing < 0) return;
                settings.SurchargeRates.RemoveAt(existing);
            }
            else Replace(settings.SurchargeRates, rates with { Source = rates.Source.Trim() }, rate => rate.Id == rates.Id);
            _store.SaveEnergySettings(settings);
            _energyRevision = NewRevision();
        }
    }

    public void CommitEnergy(EnergyImportPreview preview, bool replace)
    {
        lock (_gate)
        {
            CheckEnergyRevision(preview.Revision);
            if (_store.CommitEnergyImport(preview.File, replace)) _energyRevision = NewRevision();
        }
    }

    public EnergyReadingsClearPreview PreviewClearEnergyReadings(string ean)
    {
        lock (_gate) return new(_energyRevision, ean, _store.CountEnergyReadings(ean));
    }

    public int ClearEnergyReadings(EnergyReadingsClearPreview preview)
    {
        lock (_gate)
        {
            CheckEnergyRevision(preview.Revision);
            var count = _store.ClearEnergyReadings(preview.Ean, preview.Count);
            _energyRevision = NewRevision();
            return count;
        }
    }

    public EnergyTariffDocument? EnergyDocument(Guid tariffId)
    {
        lock (_gate) return _store.ReadEnergyDocument(tariffId);
    }

    public IReadOnlyList<(DateOnly From, DateOnly Through)> PlanEnergyFetch(string revision, Guid contractId, DateOnly month, bool current, DateOnly today)
    {
        CheckMonth(month);
        lock (_gate)
        {
            CheckEnergyRevision(revision);
            var settings = _store.ReadEnergySettings();
            var contract = settings.Contracts.FirstOrDefault(c => c.Id == contractId) ?? throw new InvalidOperationException("Kies een opgeslagen energiecontract.");
            var windows = EnergyTerms.Windows(contract, settings.Tariffs);
            var dates = current ? DynamicRanges(windows, today, today.AddDays(1))
                : DynamicRanges(windows, month, Min(month.AddMonths(1).AddDays(-1), today));
            return dates.SelectMany(d => EnergyChartsPrices.Requests(d.From, d.Through)).ToList();
        }
    }

    public void SaveEnergyPrices(string revision, IReadOnlyList<EnergyMarketPrice> prices)
    {
        lock (_gate)
        {
            CheckEnergyRevision(revision);
            _store.SaveEnergyPrices(prices);
        }
    }

    private static IReadOnlyList<(DateOnly From, DateOnly Through)> DynamicRanges(IEnumerable<EnergyWindow> windows, DateOnly from, DateOnly through)
    {
        var result = new List<(DateOnly From, DateOnly Through)>();
        if (from > through) return result;
        foreach (var w in windows.Where(w => ConfirmedDynamic(w.Tariff) && w.From <= through && w.Through >= from).OrderBy(w => w.From))
        {
            var start = w.From > from ? w.From : from;
            var end = Min(w.Through, through);
            if (result.Count > 0 && result[^1].Through.AddDays(1) == start) result[^1] = (result[^1].From, end);
            else result.Add((start, end));
        }
        return result;
    }
    private static IEnumerable<EnergyWindow> MeterWindows(EnergySettings settings, EnergyContract contract) =>
        settings.Contracts.Where(c => c.Ean == contract.Ean).SelectMany(c => EnergyTerms.Windows(c, settings.Tariffs));
    private static bool ConfirmedDynamic(EnergyTariff t) => t.Pricing == EnergyPricing.Dynamic && t.MarketSeries == EnergyMarketSeries.BelgianDayAhead;
    private static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
    private static void CheckMonth(DateOnly month)
    {
        if (month.Day != 1 || !BelgianEnergyTime.Supported(month)) throw new InvalidDataException("Kies een geldige maand voor elektriciteit.");
    }
    private void CheckEnergyRevision(string revision)
    {
        if (revision != _energyRevision) throw new AdministrationConflictException();
    }
    private static void Replace<T>(List<T> list, T item, Predicate<T> match)
    {
        var index = list.FindIndex(match);
        if (index < 0) list.Add(item); else list[index] = item;
    }
}
