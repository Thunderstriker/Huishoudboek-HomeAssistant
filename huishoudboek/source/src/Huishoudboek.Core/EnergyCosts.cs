namespace Huishoudboek.Core;

public sealed record EnergyComponent(decimal? Kwh, decimal? Amount, decimal PricedKwh, decimal PricedAmount, int Intervals, int RatedIntervals, int EstimatedIntervals)
{
    public int DailyTotals { get; init; }
    public int PartialDailyTotals { get; init; }
    public int UnpricedDailyTotals { get; init; }
    public int EstimatedDailyTotals { get; init; }
    public decimal? WeightedPrice => PricedKwh == 0 ? null : PricedAmount / PricedKwh;
    public int UnpricedIntervals => Intervals - RatedIntervals;
}

public sealed record EnergyDayCost(DateOnly Day, int Expected, EnergyComponent Consumption, EnergyComponent Injection, decimal? AllocatedFee, bool InjectionNotApplicable)
{
    public EnergyUsageSurcharges UsageSurcharges { get; init; } = new(null, null, null);
    public decimal? PartialSubtotal => EnergyCosts.Subtotal(Consumption.Amount, Injection.Amount, AllocatedFee, UsageSurcharges.Total);
}

public sealed record EnergyUsageSurcharges(decimal? Supplier, decimal? Network, decimal? Tax)
{
    public decimal? Total => EnergyCosts.SumKnown([Supplier, Network, Tax]);
}

public sealed record EnergyPeakEstimate(decimal MonthKw, decimal AverageKw, int Months, bool Provisional);

public sealed record EnergyCostReport(IReadOnlyList<EnergyDayCost> Days, EnergyComponent Consumption, EnergyComponent Injection, decimal? AllocatedFee,
    int FeeDays, int ApplicableDays, decimal? EarlyEndFee, decimal? MonthlyCharges)
{
    public int Expected => Days.Sum(d => d.Expected);
    public int ExpectedInjection => Days.Where(d => !d.InjectionNotApplicable || d.Injection.Kwh.HasValue).Sum(d => d.Expected);
    public bool HasReadings => Consumption.Kwh.HasValue || Injection.Kwh.HasValue;
    public EnergyUsageSurcharges UsageSurcharges { get; init; } = new(null, null, null);
    public decimal? CapacityCharge { get; init; }
    public decimal? DataCharge { get; init; }
    public EnergyPeakEstimate? Peak { get; init; }
    public bool HasSurchargeRates { get; init; }
    public bool HasCapacityRate { get; init; }
    public int CoveredSurchargeDays { get; init; }
    public decimal? FixedNetworkCharges => EnergyCosts.SumKnown([MonthlyCharges, CapacityCharge, DataCharge]);
    public decimal? PartialSubtotal => EnergyCosts.Subtotal(Consumption.Amount, Injection.Amount, AllocatedFee,
        EnergyCosts.SumKnown([UsageSurcharges.Total, FixedNetworkCharges]));
}

public sealed record EnergyPriceSlot(DateTime StartUtc, DateTime EndUtc, EnergyMarketPrice? Market, decimal? DayRate, decimal? NightRate);

public static class EnergyCosts
{
    public static EnergyCostReport Calculate(EnergySettings settings, EnergyContract selected, DateOnly month, DateOnly today,
        IEnumerable<EnergyReading> readings, IEnumerable<EnergyMarketPrice> marketPrices, IEnumerable<EnergyMonthlyPeak>? peaks = null)
    {
        var from = new DateOnly(month.Year, month.Month, 1);
        var through = from.AddMonths(1).AddDays(-1);
        if (through > today) through = today;
        var charges = settings.MonthlyCharges.SingleOrDefault(c => c.Ean == selected.Ean && c.Month == from)?.Amount;
        var rates = settings.SurchargeRates.Where(rate => rate.Ean == selected.Ean && rate.From <= through && rate.Through >= from).ToList();
        var peak = EstimatePeak(peaks ?? [], selected.Ean, from, today);
        var contracts = settings.Contracts.Where(c => c.Ean == selected.Ean).ToList();
        var windows = contracts.SelectMany(c => EnergyTerms.Windows(c, settings.Tariffs)).OrderBy(w => w.From).ToList();
        var indexes = settings.Indexes.ToDictionary(i => EnergyTerms.IndexKey(i.Series, i.Month));
        var prices = marketPrices.ToDictionary(p => (p.Series, p.StartUtc, p.EndUtc));
        var byDay = readings.Where(r => r.Ean == selected.Ean).ToLookup(r => BelgianEnergyTime.Day(r.StartUtc));
        var days = new List<EnergyDayCost>();
        var hasReadings = false;
        var applicable = 0;
        var coveredSurchargeDays = 0;
        var fixedDays = new Dictionary<EnergySurchargeRates, int>();
        for (var day = from; day <= through; day = day.AddDays(1))
        {
            var window = windows.FirstOrDefault(w => w.From <= day && w.Through >= day);
            var tariff = window?.Tariff;
            if (window != null) applicable++;
            var expected = BelgianEnergyTime.Quarters(day);
            var consumption = MeasureDay(byDay[day].Where(r => r.Direction == EnergyDirection.Consumption), expected,
                r => ConsumptionRate(tariff, r, prices), requiresQuarters: tariff?.Pricing == EnergyPricing.Dynamic);
            var injection = MeasureDay(byDay[day].Where(r => r.Direction == EnergyDirection.Injection), expected,
                r => InjectionRate(tariff, r, day, indexes));
            hasReadings |= consumption.Kwh.HasValue || injection.Kwh.HasValue;
            var rate = rates.Count != 0 && contracts.Any(c => (c.SupplyStart == null || c.SupplyStart <= day) && (c.SupplyEnd == null || c.SupplyEnd >= day))
                ? rates.FirstOrDefault(rate => rate.From <= day && day <= rate.Through) : null;
            var supplier = consumption.Kwh * rate?.SupplierPerKwh;
            // A saved monthly network/tax amount is an override, not an additional network charge.
            var network = charges.HasValue ? null : consumption.Kwh * rate?.NetworkPerKwh;
            var tax = charges.HasValue ? null : consumption.Kwh * rate?.TaxPerKwh;
            if (supplier.HasValue && (charges.HasValue || (network.HasValue && tax.HasValue))) coveredSurchargeDays++;
            if (!charges.HasValue && rate != null) fixedDays[rate] = fixedDays.GetValueOrDefault(rate) + 1;
            days.Add(new(day, expected, consumption, injection, window == null ? null : EnergyTerms.AllocatedFee(window, day), tariff?.Injection == EnergyInjection.None)
            { UsageSurcharges = new(supplier, network, tax) });
        }
        if (!hasReadings) days = days.Select(d => d with { AllocatedFee = null }).ToList();
        return new(days, Total(days.Select(d => d.Consumption)), Total(days.Select(d => d.Injection)), SumKnown(days.Select(d => d.AllocatedFee)),
            days.Count(d => d.AllocatedFee.HasValue), applicable, EnergyTerms.FirstYearFeeOnEarlyEnd(selected, windows.Where(w => w.Contract.Id == selected.Id).ToList()), charges)
        {
            UsageSurcharges = new(SumKnown(days.Select(d => d.UsageSurcharges.Supplier)), SumKnown(days.Select(d => d.UsageSurcharges.Network)),
                SumKnown(days.Select(d => d.UsageSurcharges.Tax))),
            CapacityCharge = SumKnown(fixedDays.Select(pair => pair.Key.CapacityPerKwMonth == 0 ? 0 :
                pair.Key.CapacityPerKwMonth * peak?.AverageKw * pair.Value / DateTime.DaysInMonth(from.Year, from.Month))),
            DataCharge = SumKnown(fixedDays.Select(pair => pair.Key.DataPerYear * pair.Value / (DateTime.IsLeapYear(from.Year) ? 366 : 365))),
            Peak = peak, HasSurchargeRates = rates.Count > 0, HasCapacityRate = fixedDays.Keys.Any(rate => rate.CapacityPerKwMonth > 0),
            CoveredSurchargeDays = coveredSurchargeDays
        };
    }

    private static EnergyPeakEstimate? EstimatePeak(IEnumerable<EnergyMonthlyPeak> peaks, string ean, DateOnly month, DateOnly today)
    {
        if (month > today) return null;
        var history = peaks.Where(p => p.Ean == ean && p.Month >= month.AddMonths(-11) && p.Month <= month).ToList();
        var current = history.FirstOrDefault(p => p.Month == month);
        return current == null ? null : new(current.Kw, history.Average(p => Math.Max(2.5m, p.Kw)), history.Count,
            today < month.AddMonths(1) || history.Any(p => EnergyTerms.ReadingQuality(p.Status) == 2));
    }

    // Expose actual market resolution, but never price the supplier formula at a different resolution.
    public static IReadOnlyList<EnergyPriceSlot> Published(EnergyWindow window, DateOnly day, IEnumerable<EnergyMarketPrice> prices)
    {
        var t = window.Tariff;
        if (t.Pricing != EnergyPricing.Dynamic || t.MarketSeries != EnergyMarketSeries.BelgianDayAhead || day < window.From || day > window.Through) return [];
        var from = BelgianEnergyTime.MidnightUtc(day);
        var until = BelgianEnergyTime.MidnightUtc(day.AddDays(1));
        var available = prices.Where(p => p.Series == t.MarketSeries && p.StartUtc >= from && p.EndUtc <= until).ToList();
        var resolution = available.Select(p => (int)(p.EndUtc - p.StartUtc).TotalMinutes).Distinct().ToList();
        // A mixed/unexpected response is not a basis for inventing a regular time series.
        if (resolution.Count > 1) return [];
        var minutes = resolution.Count == 1 ? resolution[0] : (int)t.Settlement;
        var byStart = available.ToDictionary(p => p.StartUtc);
        var result = new List<EnergyPriceSlot>();
        for (var start = from; start < until; start = start.AddMinutes(minutes))
        {
            var price = byStart.GetValueOrDefault(start);
            var matched = minutes == (int)t.Settlement ? price?.EuroPerMwh : null;
            result.Add(new(start, start.AddMinutes(minutes), price, EnergyTerms.DynamicRate(t, EnergyRegister.Day, matched),
                t.Billing == EnergyBilling.DayNight ? EnergyTerms.DynamicRate(t, EnergyRegister.Night, matched) : null));
        }
        return result;
    }

    private static decimal? ConsumptionRate(EnergyTariff? t, EnergyReading r, Dictionary<(EnergyMarketSeries, DateTime, DateTime), EnergyMarketPrice> prices)
    {
        if (t == null) return null;
        if (t.Pricing != EnergyPricing.Dynamic)
            return EnergyTerms.WithVat(EnergyTerms.Night(t, r.Register) ? t.ConsumptionNightRate : t.ConsumptionRate, t.ConsumptionVat, t.ConsumptionVatPercent);
        if (t.MarketSeries != EnergyMarketSeries.BelgianDayAhead || r.Resolution != EnergyReadingResolution.QuarterHour) return null;
        var start = t.Settlement == EnergySettlement.Hour ? r.StartUtc.AddMinutes(-r.StartUtc.Minute) : r.StartUtc;
        return EnergyTerms.DynamicRate(t, r.Register, prices.GetValueOrDefault((t.MarketSeries, start, start.AddMinutes((int)t.Settlement)))?.EuroPerMwh);
    }

    private static decimal? InjectionRate(EnergyTariff? t, EnergyReading r, DateOnly day, Dictionary<(string, DateOnly), EnergyIndexValue> indexes)
    {
        if (t == null || t.Injection == EnergyInjection.Unknown) return null;
        if (t.Injection == EnergyInjection.None) return 0;
        var night = EnergyTerms.Night(t, r.Register);
        var price = t.Injection == EnergyInjection.Fixed ? (night ? t.InjectionNightRate : t.InjectionRate) : EnergyTerms.Formula(
            indexes.GetValueOrDefault(EnergyTerms.IndexKey(t.InjectionIndex, day))?.EuroPerMwh, night ? t.InjectionNightCoefficient : t.InjectionCoefficient,
            night ? t.InjectionNightOffset : t.InjectionOffset);
        return EnergyTerms.WithVat(price, t.InjectionVat, t.InjectionVatPercent);
    }

    private static EnergyComponent MeasureDay(IEnumerable<EnergyReading> readings, int expected, Func<EnergyReading, decimal?> rate, bool requiresQuarters = false)
    {
        var (quarters, daily) = (new List<EnergyReading>(), new List<EnergyReading>());
        foreach (var reading in readings)
            (reading.Resolution == EnergyReadingResolution.DailyTotal ? daily : quarters).Add(reading);
        var quarter = Measure(quarters, rate);
        var completeDaily = daily.Any(r => r.Register == EnergyRegister.Day) && daily.Any(r => r.Register == EnergyRegister.Night);
        // Prefer a complete quarter series; never add a daily aggregate to its overlapping quarters.
        if (daily.Count == 0 || quarter.Intervals == expected || (!completeDaily && quarter.Intervals > 0)) return quarter;

        var total = Measure(daily, rate);
        // Dynamic costs can only use real quarters from the same meter. They are a partial amount, not the cost of the daily kWh.
        var priced = !requiresQuarters ? total : quarters.All(r => r.Meter == daily[0].Meter) ? quarter : null;
        return quarter with
        {
            Kwh = total.Kwh, Amount = priced?.Amount, PricedKwh = priced?.PricedKwh ?? 0, PricedAmount = priced?.PricedAmount ?? 0,
            DailyTotals = 1, PartialDailyTotals = completeDaily ? 0 : 1, UnpricedDailyTotals = requiresQuarters || total.UnpricedIntervals > 0 ? 1 : 0,
            EstimatedDailyTotals = total.EstimatedIntervals
        };
    }

    private static EnergyComponent Measure(IEnumerable<EnergyReading> readings, Func<EnergyReading, decimal?> rate)
    {
        decimal? usage = null, amount = null;
        decimal pricedKwh = 0, pricedAmount = 0;
        int intervals = 0, rated = 0, estimated = 0;
        foreach (var group in readings.GroupBy(r => r.StartUtc))
        {
            intervals++;
            if (group.Any(r => EnergyTerms.ReadingQuality(r.Status) == 2)) estimated++;
            var fullyRated = true;
            foreach (var reading in group)
            {
                usage = (usage ?? 0) + reading.Quantity;
                if (rate(reading) is { } known)
                {
                    var cost = reading.Quantity * known;
                    amount = (amount ?? 0) + cost; pricedAmount += cost; pricedKwh += reading.Quantity;
                }
                else
                {
                    if (reading.Quantity == 0) amount ??= 0;
                    fullyRated = false;
                }
            }
            if (fullyRated) rated++;
        }
        return new(usage, amount, pricedKwh, pricedAmount, intervals, rated, estimated);
    }

    private static EnergyComponent Total(IEnumerable<EnergyComponent> components)
    {
        var values = components.ToList();
        return new(SumKnown(values.Select(v => v.Kwh)), SumKnown(values.Select(v => v.Amount)), values.Sum(v => v.PricedKwh), values.Sum(v => v.PricedAmount),
            values.Sum(v => v.Intervals), values.Sum(v => v.RatedIntervals), values.Sum(v => v.EstimatedIntervals))
        {
            DailyTotals = values.Sum(v => v.DailyTotals), PartialDailyTotals = values.Sum(v => v.PartialDailyTotals),
            UnpricedDailyTotals = values.Sum(v => v.UnpricedDailyTotals), EstimatedDailyTotals = values.Sum(v => v.EstimatedDailyTotals)
        };
    }

    internal static decimal? SumKnown(IEnumerable<decimal?> values) => values.Aggregate((decimal?)null, (sum, v) => v.HasValue ? (sum ?? 0) + v.Value : sum);
    internal static decimal? Subtotal(decimal? consumption, decimal? injection, decimal? fee, decimal? monthlyCharges = null) =>
        SumKnown([consumption, -injection, fee, monthlyCharges]);
}
