namespace Huishoudboek.Core;

public sealed record EnergyWindow(EnergyContract Contract, EnergyTariff Tariff, DateOnly From, DateOnly Through);

public static class EnergyTerms
{
    private static readonly DateOnly LastDay = new(2199, 12, 31);
    public static bool ValidEan(string? ean) => ean is { Length: 18 } && ean.All(char.IsAsciiDigit);
    public static IEnumerable<EnergyWindow> Windows(EnergyContract contract, IEnumerable<EnergyTariff> tariffs)
    {
        var terms = tariffs.Where(t => t.ContractId == contract.Id).OrderBy(t => t.From).ToArray();
        for (var i = 0; i < terms.Length; i++)
        {
            var tariff = terms[i];
            var from = contract.SupplyStart > tariff.From ? contract.SupplyStart.Value : tariff.From;
            var through = tariff.Through ?? (i + 1 < terms.Length ? terms[i + 1].From.AddDays(-1) : LastDay);
            if (contract.SupplyEnd < through) through = contract.SupplyEnd.Value;
            if (from <= through) yield return new(contract, tariff, from, through);
        }
    }

    public static decimal? WithVat(decimal? amount, EnergyVat vat, decimal? percent) => vat switch
    {
        _ when amount == 0 => 0,
        EnergyVat.Included or EnergyVat.NotApplicable => amount,
        EnergyVat.Excluded when percent.HasValue => amount * (1m + percent / 100m),
        _ => null
    };

    public static decimal? DynamicRate(EnergyTariff tariff, EnergyRegister register, decimal? market) =>
        WithVat(Formula(market, Night(tariff, register) ? tariff.MarketNightCoefficient : tariff.MarketCoefficient,
            Night(tariff, register) ? tariff.MarketNightOffset : tariff.MarketOffset), tariff.ConsumptionVat, tariff.ConsumptionVatPercent);

    public static decimal? FeeGross(EnergyTariff tariff) => WithVat(tariff.FeeAmount, tariff.FeeVat, tariff.FeeVatPercent);

    // Contract-anniversary periods, never a bank position or an invoice line.
    public static decimal? AllocatedFee(EnergyWindow window, DateOnly day)
    {
        var tariff = window.Tariff;
        var gross = FeeGross(tariff);
        if (gross == 0) return 0;
        if (gross == null || tariff.FeeRule == EnergyFeeRule.Unknown || window.Contract.SupplyStart is not { } start) return null;
        var months = (int)tariff.FeePeriod;
        var elapsed = (day.Year - start.Year) * 12 + day.Month - start.Month;
        var period = elapsed / months * months;
        var from = start.AddMonths(period);
        if (day < from) from = start.AddMonths(period -= months);
        return gross / (start.AddMonths(period + months).DayNumber - from.DayNumber);
    }

    public static decimal? FirstYearFeeOnEarlyEnd(EnergyContract contract, IReadOnlyList<EnergyWindow> windows)
    {
        if (contract.SupplyStart is not { } start || contract.SupplyEnd is not { } end || end >= start.AddYears(1).AddDays(-1)) return null;
        var first = windows.FirstOrDefault(w => w.From <= start && w.Through >= start)?.Tariff;
        if (first?.FeeRule != EnergyFeeRule.FullFirstYearThenProRata || FeeGross(first) is not { } amount) return null;
        var next = start;
        foreach (var window in windows.Where(w => w.Through >= start && w.From <= end))
        {
            if (window.From > next || window.Tariff.FeeRule != first.FeeRule || FeeGross(window.Tariff) != amount) return null;
            if (window.Through >= end) return amount;
            next = window.Through.AddDays(1);
        }
        return null;
    }

    public static bool Night(EnergyTariff tariff, EnergyRegister register) => tariff.Billing == EnergyBilling.DayNight && register == EnergyRegister.Night;
    public static decimal? Formula(decimal? eurPerMwh, decimal? coefficient, decimal? offset) => (eurPerMwh * coefficient + offset) / 100m;

    public static void Validate(EnergySettings settings)
    {
        Require(Unique(settings.Contracts.Select(c => c.Id)) && Unique(settings.Tariffs.Select(t => t.Id)) && Unique(settings.Indexes.Select(i => i.Id)), "Ongeldige of dubbele energie-identificatie.");
        var contracts = settings.Contracts.ToDictionary(c => c.Id);
        foreach (var c in settings.Contracts)
            Require(ValidEan(c.Ean) && Text(c.Supplier, 120, true) && Text(c.Product, 120, true) && Date(c.SupplyStart) && Date(c.SupplyEnd) && Date(c.Renewal) && (c.SupplyStart == null || c.SupplyEnd == null || c.SupplyStart <= c.SupplyEnd),
                "Vul een EAN van 18 cijfers, leverancier, product en geldige leveringsdatums in.");
        foreach (var t in settings.Tariffs)
        {
            Require(contracts.ContainsKey(t.ContractId) && Date(t.From) && Date(t.Through) && (t.Through == null || t.Through >= t.From) &&
                Date(t.CardIssued) && (!t.CardIssueMonthOnly || t.CardIssued is { Day: 1 }) && Text(t.CardSource, 500) && Text(t.Notes, 3000) && Text(t.FeeTerms, 1500) && Text(t.DocumentName, 180),
                "Ongeldige datums, bron of notities bij de tariefversie.");
            Require(Enum.IsDefined(t.Pricing) && Enum.IsDefined(t.Billing) && Enum.IsDefined(t.Injection) && Enum.IsDefined(t.MarketSeries) && Enum.IsDefined(t.Settlement) &&
                Enum.IsDefined(t.FeePeriod) && Enum.IsDefined(t.FeeRule) &&
                (t.FeeRule != EnergyFeeRule.FullFirstYearThenProRata || t.FeePeriod == EnergyFeePeriod.Yearly) &&
                Vat(t.ConsumptionVat, t.ConsumptionVatPercent) && Vat(t.InjectionVat, t.InjectionVatPercent) && Vat(t.FeeVat, t.FeeVatPercent), "Ongeldige tariefsoort, btw of regel voor de vaste vergoeding.");
            Require(Rate(t.ConsumptionRate) && Rate(t.ConsumptionNightRate) && Rate(t.InjectionRate) && Rate(t.InjectionNightRate) &&
                Coefficient(t.MarketCoefficient) && Coefficient(t.MarketNightCoefficient) && Coefficient(t.InjectionCoefficient) && Coefficient(t.InjectionNightCoefficient) &&
                Rate(t.MarketOffset) && Rate(t.MarketNightOffset) && Rate(t.InjectionOffset) && Rate(t.InjectionNightOffset) &&
                (t.FeeAmount == null || t.FeeAmount is >= 0 and <= 1_000_000) && Text(t.InjectionIndex, 180), "Een energieprijs, index, coëfficiënt of vaste vergoeding is ongeldig.");
        }
        foreach (var group in settings.Tariffs.GroupBy(t => t.ContractId))
        {
            EnergyTariff? previous = null;
            foreach (var tariff in group.OrderBy(t => t.From))
            {
                Require(previous == null || (previous.From < tariff.From && (previous.Through == null || previous.Through < tariff.From)), "Tariefversies mogen elkaar niet overlappen.");
                previous = tariff;
            }
        }
        var lookups = settings.Tariffs.ToLookup(t => t.ContractId);
        foreach (var group in settings.Contracts.SelectMany(c => Windows(c, lookups[c.Id])).GroupBy(w => w.Contract.Ean))
        {
            DateOnly? until = null;
            foreach (var window in group.OrderBy(w => w.From))
            {
                Require(until == null || until < window.From, "Per EAN kan maar één contracttarief tegelijk gelden. Sluit de vorige leveringsperiode af.");
                until = window.Through;
            }
        }
        var keys = new HashSet<(string, DateOnly)>();
        foreach (var i in settings.Indexes)
            Require(Text(i.Series, 180, true) && Text(i.Source, 500, true) && Date(i.Month) && i.Month.Day == 1 && Market(i.EuroPerMwh) && keys.Add(IndexKey(i.Series, i.Month)),
                "Vul per maand en indexnaam één geverifieerde waarde in €/MWh met bron in.");
    }

    public static (string, DateOnly) IndexKey(string series, DateOnly date) => (series.Trim().ToUpperInvariant(), new(date.Year, date.Month, 1));

    public static bool ValidReading(EnergyReading r) => r.Id != Guid.Empty && ValidEan(r.Ean) && Text(r.Meter, 100, true) &&
        ValidInterval(r.StartUtc, r.EndUtc, 15) && Enum.IsDefined(r.Direction) && Enum.IsDefined(r.Register) && r.Quantity is >= 0 and <= 1_000_000 &&
        r.Unit == "kWh" && Text(r.Source, 180, true) && Text(r.Description, 500) && ReadingQuality(r.Status) != 0 &&
        (!r.Status.Equals("Geen verbruik", StringComparison.OrdinalIgnoreCase) || r.Quantity == 0);

    public static int ReadingQuality(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "UITGELEZEN" or "GEEN VERBRUIK" or "GEVALIDEERD" or "GEVALIDEERDE WAARDE" or "VALIDATED" or "GEMETEN" or "WERKELIJK" or "MEASURED" => 1,
        "GESCHAT" or "GESCHATTE WAARDE" or "ESTIMATED" or "BEREKEND" or "VOORLOPIG" => 2,
        _ => 0
    };

    public static bool ValidPrice(EnergyMarketPrice p) => p.Id != Guid.Empty && p.Series == EnergyMarketSeries.BelgianDayAhead &&
        (ValidInterval(p.StartUtc, p.EndUtc, 15) || ValidInterval(p.StartUtc, p.EndUtc, 60)) && Market(p.EuroPerMwh) && p.Unit == "EUR/MWh" &&
        Text(p.Source, 180, true) && Text(p.License, 100, true) && p.FetchedUtc.Kind == DateTimeKind.Utc &&
        (p.SourceGeneratedUtc == null || p.SourceGeneratedUtc.Value.Kind == DateTimeKind.Utc);

    private static bool ValidInterval(DateTime start, DateTime end, int minutes) => start.Kind == DateTimeKind.Utc && end.Kind == DateTimeKind.Utc &&
        start.Year is >= 1899 and <= 2199 && BelgianEnergyTime.Supported(BelgianEnergyTime.Day(start)) &&
        start.Ticks % (TimeSpan.TicksPerMinute * minutes) == 0 && end - start == TimeSpan.FromMinutes(minutes);
    private static bool Date(DateOnly? date) => date == null || BelgianEnergyTime.Supported(date.Value);
    private static bool Rate(decimal? value) => value == null || value is >= -100_000 and <= 100_000;
    private static bool Coefficient(decimal? value) => value == null || value is >= -1000 and <= 1000;
    private static bool Market(decimal value) => value is >= -1_000_000 and <= 1_000_000;
    private static bool Vat(EnergyVat vat, decimal? percent) => Enum.IsDefined(vat) && (percent == null || percent is >= 0 and <= 100);
    private static bool Text(string? text, int length, bool required = false) => text != null && text.Length <= length && (!required || !string.IsNullOrWhiteSpace(text));
    private static bool Unique(IEnumerable<Guid> ids) { var seen = new HashSet<Guid>(); return ids.All(id => id != Guid.Empty && seen.Add(id)); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
