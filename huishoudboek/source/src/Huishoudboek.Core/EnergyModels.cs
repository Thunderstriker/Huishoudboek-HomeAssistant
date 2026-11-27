namespace Huishoudboek.Core;

public enum EnergyPricing { Fixed, Variable, Dynamic }
public enum EnergyBilling { Single, DayNight }
public enum EnergyInjection { Unknown, None, Fixed, MonthlyIndex }
public enum EnergyVat { Unknown, Included, Excluded, NotApplicable }
public enum EnergyFeePeriod { Monthly = 1, Quarterly = 3, Yearly = 12 }
public enum EnergyFeeRule { Unknown, ProRata, FullFirstYearThenProRata }
public enum EnergyMarketSeries { Unconfirmed, BelgianDayAhead }
public enum EnergySettlement { QuarterHour = 15, Hour = 60 }
public enum EnergyDirection { Consumption, Injection }
public enum EnergyRegister { Day, Night }
public enum EnergyReadingResolution { QuarterHour, DailyTotal }

public sealed record EnergyContract
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Ean { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string Product { get; set; } = "";
    public DateOnly? SupplyStart { get; set; }
    public DateOnly? SupplyEnd { get; set; } // Last day of supply, inclusive.
    public DateOnly? Renewal { get; set; }
    public Guid? SubscriptionId { get; set; }
}

public sealed record EnergyTariff
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContractId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly? Through { get; set; }
    public string CardSource { get; set; } = "";
    public DateOnly? CardIssued { get; set; }
    public bool CardIssueMonthOnly { get; set; }
    public string Notes { get; set; } = "";
    public string DocumentName { get; set; } = "";

    public EnergyPricing Pricing { get; set; }
    public EnergyBilling Billing { get; set; }
    public decimal? ConsumptionRate { get; set; } // EUR/kWh; single or day
    public decimal? ConsumptionNightRate { get; set; }
    public EnergyVat ConsumptionVat { get; set; }
    public decimal? ConsumptionVatPercent { get; set; }
    public EnergyMarketSeries MarketSeries { get; set; }
    public EnergySettlement Settlement { get; set; } = EnergySettlement.QuarterHour;
    public decimal? MarketCoefficient { get; set; } // cents/kWh per EUR/MWh
    public decimal? MarketOffset { get; set; } // cents/kWh
    public decimal? MarketNightCoefficient { get; set; }
    public decimal? MarketNightOffset { get; set; }

    public EnergyInjection Injection { get; set; }
    public decimal? InjectionRate { get; set; } // EUR/kWh; single or day
    public decimal? InjectionNightRate { get; set; }
    public string InjectionIndex { get; set; } = "";
    public decimal? InjectionCoefficient { get; set; }
    public decimal? InjectionOffset { get; set; }
    public decimal? InjectionNightCoefficient { get; set; }
    public decimal? InjectionNightOffset { get; set; }
    public EnergyVat InjectionVat { get; set; }
    public decimal? InjectionVatPercent { get; set; }

    public decimal? FeeAmount { get; set; } // EUR per connection per FeePeriod
    public EnergyFeePeriod FeePeriod { get; set; } = EnergyFeePeriod.Yearly;
    public EnergyVat FeeVat { get; set; }
    public decimal? FeeVatPercent { get; set; }
    public EnergyFeeRule FeeRule { get; set; }
    public string FeeTerms { get; set; } = "";
}

public sealed record EnergyIndexValue
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Series { get; init; } = "";
    public DateOnly Month { get; init; }
    public decimal EuroPerMwh { get; init; }
    public string Source { get; init; } = "";
}

public sealed record EnergyMonthlyCharge
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Ean { get; init; } = "";
    public DateOnly Month { get; init; }
    public decimal Amount { get; init; } // EUR including applicable VAT
}

public sealed record EnergySurchargeRates
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Ean { get; init; } = "";
    public DateOnly From { get; set; }
    public DateOnly Through { get; set; }
    public decimal? SupplierPerKwh { get; set; }
    public decimal? NetworkPerKwh { get; set; }
    public decimal? TaxPerKwh { get; set; }
    public decimal? CapacityPerKwMonth { get; set; }
    public decimal? DataPerYear { get; set; }
    public string Source { get; set; } = "";
}

public sealed record EnergyMonthlyPeak
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Ean { get; init; } = "";
    public string Meter { get; init; } = "";
    public DateOnly Month { get; init; }
    public decimal Kw { get; init; }
    public string Status { get; init; } = "";
}

public sealed record EnergyReading
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Ean { get; init; } = "";
    public string Meter { get; init; } = "";
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public EnergyReadingResolution Resolution { get; init; }
    public EnergyDirection Direction { get; init; }
    public EnergyRegister Register { get; init; }
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = "kWh";
    public string Status { get; init; } = "";
    public string Source { get; init; } = "Fluvius CSV";
    public string Description { get; init; } = "";
}

public sealed record EnergyMarketPrice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public EnergyMarketSeries Series { get; init; } = EnergyMarketSeries.BelgianDayAhead;
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public decimal EuroPerMwh { get; init; }
    public string Unit { get; init; } = "EUR/MWh";
    public string Source { get; init; } = "Energy-Charts.info / Bundesnetzagentur | SMARD.de";
    public string License { get; init; } = "CC BY 4.0";
    public DateTime? SourceGeneratedUtc { get; init; }
    public DateTime FetchedUtc { get; init; }
}

public sealed record EnergySettings(List<EnergyContract> Contracts, List<EnergyTariff> Tariffs, List<EnergyIndexValue> Indexes)
{
    public List<EnergyMonthlyCharge> MonthlyCharges { get; init; } = [];
    public List<EnergySurchargeRates> SurchargeRates { get; init; } = [];
}
public sealed record EnergyTariffDocument(Guid TariffId, string Name, byte[] Content);

public static class BelgianEnergyTime
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
    public static DateOnly Day(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
    public static DateOnly Today => Day(DateTime.UtcNow);
    public static DateTime MidnightUtc(DateOnly day) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), Zone);
    public static int Quarters(DateOnly day) => (int)(MidnightUtc(day.AddDays(1)) - MidnightUtc(day)).TotalMinutes / 15;
    public static bool Supported(DateOnly day) => day.Year is >= 1900 and <= 2199;
}
