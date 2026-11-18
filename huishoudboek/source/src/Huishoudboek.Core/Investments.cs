namespace Huishoudboek.Core;

public enum InvestmentTradeKind { Buy, Sell, Split, Opening }
public enum QuoteFrequency { Manual, Daily, SeveralPerDay, Hourly, Realtime }
public enum QuoteFreshness { EndOfDay, Delayed, Realtime, Manual }

public sealed record InvestmentInstrument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Isin { get; set; } = "";
    public string Exchange { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public string ProviderSymbol { get; set; } = "";
    public bool MappingConfirmed { get; set; }
    public bool IsUsListing { get; set; }
    public decimal? ManualPrice { get; set; }
    public decimal? ManualPreviousClose { get; set; }
    public DateOnly ManualPriceDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal? ManualEurRate { get; set; }
}

/// <summary>Separate securities ledger: never a bank/cash posting. Costs are in the execution currency.</summary>
public sealed record InvestmentTrade
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid InstrumentId { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int Sequence { get; set; } = 1;
    public InvestmentTradeKind Kind { get; set; }
    // For a split, Quantity is the new/old share ratio, e.g. 2 for a two-for-one split.
    public decimal Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public string Currency { get; set; } = "EUR";
    public decimal Fees { get; set; }
    public decimal Tax { get; set; }
    public decimal FxCosts { get; set; }
    // Actual all-in EUR debit (buy) or net EUR credit (sell). Never revalue this with current FX.
    public decimal? SettlementEur { get; set; }
    public decimal? HistoricalEurRate { get; set; }
    public string Note { get; set; } = "";
}

public sealed record InvestmentQuote(Guid InstrumentId, string Symbol, string Currency, decimal Price,
    decimal? PreviousClose, DateOnly PriceDate, DateTime RetrievedUtc, QuoteFreshness Freshness);
public sealed record InvestmentFxRate(string Currency, decimal EurRate, DateTime RetrievedUtc);
public sealed record InvestmentSymbol(string Symbol, string Name, string Region, string Currency);

public enum InvestmentDataState { NotApplicable, Updated, Retained, Missing }

/// <summary>Safe request diagnostics: application codes and listing identifiers, never provider text or URLs.</summary>
public sealed record InvestmentRequestResult(string Kind, string Target, string Status, InvestmentDataState DataState,
    DateOnly? PriceDate = null, DateTime? DataUtc = null, int? HttpStatusCode = null);

public sealed record InvestmentMarketReport(string Operation, DateTime StartedUtc, DateTime? CompletedUtc,
    string Status, IReadOnlyList<InvestmentRequestResult> Requests, string? ErrorCode = null);

/// <summary>A provider retry time, or a non-binding suggestion when the provider gives no reset time.</summary>
public sealed record InvestmentProviderLimit(DateTime ReportedUtc, DateTime RetryUtc, bool ProviderSuppliedRetryTime);

public sealed record InvestmentQuoteSettings
{
    public bool Enabled { get; set; }
    public bool Paid { get; set; }
    public QuoteFrequency Frequency { get; set; } = QuoteFrequency.Daily;
    public int DailyHour { get; set; } = 7;
    public int UpdatesPerDay { get; set; } = 2;
    public int RealtimeSeconds { get; set; } = 60;
    public QuoteFreshness Freshness { get; set; } = QuoteFreshness.EndOfDay;
    public int DailyLimit { get; set; } = 25;
    public int RequestsPerMinute { get; set; } = 5;

    public void Validate()
    {
        if (!Enum.IsDefined(Frequency) || !Enum.IsDefined(Freshness) || Freshness == QuoteFreshness.Manual ||
            DailyHour is < 0 or > 23 || UpdatesPerDay is < 2 or > 24 || RealtimeSeconds is < 60 or > 3600 ||
            DailyLimit is < 0 or > 1_000_000 || RequestsPerMinute is < 1 or > 1200 ||
            !Paid && (DailyLimit is < 1 or > 25 || RequestsPerMinute > 5 || Freshness != QuoteFreshness.EndOfDay || Frequency == QuoteFrequency.Realtime) ||
            Frequency == QuoteFrequency.Realtime && Freshness != QuoteFreshness.Realtime)
            throw new InvalidOperationException("Ongeldige koersinstellingen. Gratis: maximaal 25 aanvragen/dag, 5/minuut en slotkoersen. Realtime vereist betaalde rechten.");
    }
}

public sealed record InvestmentMarketSnapshot(InvestmentQuoteSettings Settings, bool HasKey,
    IReadOnlyList<InvestmentQuote> Quotes, IReadOnlyList<InvestmentFxRate> Rates, int RequestsToday,
    DateTime? LastAttemptUtc, DateTime? LastSuccessUtc, DateTime? NextRefreshUtc, string Status)
{
    public InvestmentMarketReport? Report { get; init; }
    public InvestmentProviderLimit? ProviderLimit { get; init; }
    public DateTime? DailyBudgetResetUtc { get; init; }
}

public sealed record InvestmentPosition(Guid AccountId, InvestmentInstrument Instrument, decimal Quantity,
    decimal? RemainingCost, decimal? RemainingCostEur, decimal? Realized, decimal? RealizedEur,
    decimal? Price, decimal? PreviousClose, DateOnly? PriceDate, DateTime? RetrievedUtc,
    QuoteFreshness Freshness, decimal? EurRate, DateTime? FxDateUtc, bool EstimatedEurCost, bool ManualFx)
{
    public decimal? Value => Price * Quantity;
    public decimal? ValueEur => Value * EurRate;
    public decimal? Gain => Value - RemainingCost;
    public decimal? GainEur => ValueEur - RemainingCostEur;
    public decimal? GainPercent => RemainingCost > 0 ? Gain / RemainingCost * 100 : null;
    public decimal? GainEurPercent => RemainingCostEur > 0 ? GainEur / RemainingCostEur * 100 : null;
    public decimal? DailyPercent => PreviousClose > 0 ? (Price / PreviousClose - 1) * 100 : null;
    public decimal? AverageCost => Quantity > 0 ? RemainingCost / Quantity : null;
}

public static class Investments
{
    private sealed class Lot(decimal quantity, decimal? cost, decimal? eur, bool estimated)
    {
        public decimal Quantity = quantity;
        public decimal? Cost = cost;
        public decimal? Eur = eur;
        public bool Estimated = estimated;
    }

    public static void Validate(HouseholdData data)
    {
        static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        static bool Number(decimal n) => n is >= 0 and <= 1_000_000_000 && decimal.Round(n, 8) == n;
        Require(data.Instruments.All(i => i.Id != Guid.Empty) && data.Instruments.Select(i => i.Id).Distinct().Count() == data.Instruments.Count,
            "Ongeldige of dubbele effectenidentificatie.");
        Require(data.InvestmentTrades.All(t => t.Id != Guid.Empty) && data.InvestmentTrades.Select(t => t.Id).Distinct().Count() == data.InvestmentTrades.Count,
            "Ongeldige of dubbele effectentransactie.");
        foreach (var i in data.Instruments)
        {
            Require(!string.IsNullOrWhiteSpace(i.Name) && i.Name.Length <= 200 && i.Isin != null && i.Isin.Length <= 20 &&
                i.Exchange != null && i.Exchange.Length <= 100 && CurrencyValid(i.Currency) && i.ProviderSymbol != null &&
                i.ProviderSymbol.Length <= 40 && i.ProviderSymbol.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') &&
                (i.ProviderSymbol.Length == 0 || i.MappingConfirmed), "Controleer effectnaam, beurs, valuta en bevestigde providersymboolkoppeling.");
            Require((i.ManualPrice == null || Number(i.ManualPrice.Value)) &&
                (i.ManualPreviousClose == null || i.ManualPreviousClose > 0 && Number(i.ManualPreviousClose.Value)) &&
                (i.ManualEurRate == null || i.ManualEurRate is > 0 and <= 1_000_000) && i.ManualPriceDate.Year is >= 1900 and <= 2200,
                "Ongeldige handmatige koers, koersdatum of EUR-wisselkoers.");
        }
        Require(data.InvestmentTrades.Select(t => (t.AccountId, t.InstrumentId, t.Date, t.Sequence)).Distinct().Count() == data.InvestmentTrades.Count,
            "Geef transacties voor hetzelfde effect op dezelfde dag een verschillende volgorde.");
        foreach (var t in data.InvestmentTrades)
        {
            var instrument = data.Instruments.FirstOrDefault(i => i.Id == t.InstrumentId);
            Require(instrument != null && data.Accounts.Any(a => a.Id == t.AccountId && a.Kind == AccountKind.Investment), "Kies een geldig effect en een beleggingsrekening.");
            Require(Enum.IsDefined(t.Kind) && t.Date.Year is >= 1900 and <= 2200 && t.Sequence is >= 1 and <= 100000 &&
                t.Currency == instrument!.Currency && t.Quantity > 0 && Number(t.Quantity) &&
                (t.UnitPrice == null || Number(t.UnitPrice.Value)) && Number(t.Fees) && Number(t.Tax) && Number(t.FxCosts) &&
                (t.SettlementEur == null || t.SettlementEur is > 0 and <= 1_000_000_000_000_000_000m) &&
                (t.HistoricalEurRate == null || t.HistoricalEurRate is > 0 and <= 1_000_000) && t.Note != null && t.Note.Length <= 2000,
                "Controleer transactiedatum, volgorde, aantal, prijzen, kosten en valuta (maximaal acht decimalen).");
            Require(t.Kind is not (InvestmentTradeKind.Buy or InvestmentTradeKind.Sell) || t.UnitPrice > 0, "Een aankoop of verkoop vereist een positieve uitvoeringsprijs.");
            Require(t.Kind != InvestmentTradeKind.Opening || t.UnitPrice == null || t.UnitPrice > 0, "Laat de kostprijs bij onbekende openingshistorie leeg.");
            Require(t.Kind != InvestmentTradeKind.Split || t.UnitPrice == null && t.Fees == 0 && t.Tax == 0 && t.FxCosts == 0 && t.SettlementEur == null && t.HistoricalEurRate == null,
                "Een splitsing bevat alleen de verhouding nieuw/oud, geen prijs, kosten of afrekening.");
            Require(t.UnitPrice != null || t.Fees == 0 && t.Tax == 0 && t.FxCosts == 0 && t.SettlementEur == null && t.HistoricalEurRate == null,
                "Onbekende openingskosten kunnen niet met een afrekening of kosten worden gecombineerd.");
            Require(t.Kind != InvestmentTradeKind.Sell || NativeAmount(t) > 0, "Verkoopkosten mogen de opbrengst niet volledig opgebruiken.");
            Require(t.Currency != "EUR" || t.SettlementEur == null || CalculatedNativeAmount(t) is { } native && Math.Abs(native - t.SettlementEur.Value) <= .01m,
                "Bij een EUR-transactie moet de afrekening overeenkomen met aantal × prijs plus aankoopkosten of min verkoopkosten.");
        }
        // Also checks chronological inventory and rejects overselling, including after historical edits.
        _ = Positions(data, [], [], new DateOnly(2200, 12, 31));
    }

    public static bool CurrencyValid(string? currency) => currency is { Length: 3 } && currency.All(c => c is >= 'A' and <= 'Z');
    private static decimal? CalculatedNativeAmount(InvestmentTrade t) => t.UnitPrice * t.Quantity +
        (t.Kind == InvestmentTradeKind.Sell ? -1 : 1) * (t.Fees + t.Tax + t.FxCosts);
    public static decimal? NativeAmount(InvestmentTrade t) => t.Currency == "EUR" && t.SettlementEur != null
        ? t.SettlementEur : CalculatedNativeAmount(t);
    public static decimal? EurAmount(InvestmentTrade t) => t.SettlementEur ??
        (t.Currency == "EUR" ? NativeAmount(t) : NativeAmount(t) * t.HistoricalEurRate);

    public static IReadOnlyList<InvestmentPosition> Positions(HouseholdData data, IReadOnlyList<InvestmentQuote> quotes,
        IReadOnlyList<InvestmentFxRate> rates, DateOnly today, Guid? ownerId = null)
    {
        var result = new List<InvestmentPosition>();
        foreach (var group in data.InvestmentTrades.Where(t => t.Date <= today &&
            data.Accounts.Any(a => a.Id == t.AccountId && (ownerId == null || a.OwnerIds.Contains(ownerId.Value))))
            .GroupBy(t => (t.AccountId, t.InstrumentId)))
        {
            var instrument = data.Instruments.Single(i => i.Id == group.Key.InstrumentId);
            var lots = new List<Lot>();
            decimal? realized = 0, realizedEur = 0;
            var realizedEstimated = false;
            DateOnly? lastSplit = null;
            foreach (var t in group.OrderBy(t => t.Date).ThenBy(t => t.Sequence).ThenBy(t => t.Id))
            {
                if (t.Kind == InvestmentTradeKind.Split)
                {
                    foreach (var lot in lots)
                    {
                        var next = lot.Quantity * t.Quantity;
                        if (lot.Quantity > 0 && next == 0) throw new InvalidOperationException("De splitsing maakt het aantal aandelen te klein om exact te bewaren.");
                        lot.Quantity = next;
                    }
                    lastSplit = t.Date;
                }
                else if (t.Kind != InvestmentTradeKind.Sell)
                    lots.Add(new(t.Quantity, NativeAmount(t), EurAmount(t), t.Currency != "EUR" && t.SettlementEur == null && t.HistoricalEurRate != null));
                else
                {
                    var left = t.Quantity;
                    decimal? soldCost = 0, soldEur = 0;
                    foreach (var lot in lots.Where(l => l.Quantity > 0))
                    {
                        if (left == 0) break;
                        var take = Math.Min(left, lot.Quantity);
                        // Take the final residual exactly; do not round away fractions of a lot's cost.
                        var cost = take == lot.Quantity ? lot.Cost : lot.Cost * take / lot.Quantity;
                        var eur = take == lot.Quantity ? lot.Eur : lot.Eur * take / lot.Quantity;
                        realizedEstimated |= lot.Estimated;
                        soldCost += cost; soldEur += eur;
                        lot.Cost -= cost; lot.Eur -= eur; lot.Quantity -= take; left -= take;
                    }
                    if (left > 0) throw new InvalidOperationException("Een verkoop overschrijdt het aantal beschikbare aandelen. Vul ontbrekende aankopen of een openingspositie aan.");
                    realized += NativeAmount(t) - soldCost; realizedEur += EurAmount(t) - soldEur;
                    realizedEstimated |= t.Currency != "EUR" && t.SettlementEur == null && t.HistoricalEurRate != null;
                }
                if (lots.Sum(l => l.Quantity) > 1_000_000_000_000m) throw new InvalidOperationException("De effectenhoeveelheid is te groot.");
            }
            var open = lots.Where(l => l.Quantity > 0).ToArray();
            decimal? costTotal = open.All(l => l.Cost != null) ? open.Sum(l => l.Cost!.Value) : null;
            decimal? eurTotal = open.All(l => l.Eur != null) ? open.Sum(l => l.Eur!.Value) : null;
            var manual = instrument.ProviderSymbol.Length == 0;
            var quote = quotes.FirstOrDefault(q => q.InstrumentId == instrument.Id && q.Symbol == instrument.ProviderSymbol && q.Currency == instrument.Currency);
            var date = manual ? instrument.ManualPrice != null ? (DateOnly?)instrument.ManualPriceDate : null : quote?.PriceDate;
            var usable = date <= today && (lastSplit == null || date >= lastSplit);
            var rate = rates.FirstOrDefault(r => r.Currency == instrument.Currency);
            var manualFx = instrument.Currency != "EUR" && instrument.ManualEurRate != null && instrument.ManualPriceDate <= today &&
                (rate == null || instrument.ManualPriceDate > DateOnly.FromDateTime(rate.RetrievedUtc));
            var fx = instrument.Currency == "EUR" ? 1m : manualFx ? instrument.ManualEurRate : rate?.EurRate;
            result.Add(new(group.Key.AccountId, instrument, open.Sum(l => l.Quantity), costTotal, eurTotal, realized, realizedEur,
                usable ? manual ? instrument.ManualPrice : quote?.Price : null,
                usable && (lastSplit == null || date > lastSplit) ? manual ? instrument.ManualPreviousClose : quote?.PreviousClose : null,
                date, manual ? null : quote?.RetrievedUtc, manual ? QuoteFreshness.Manual : quote?.Freshness ?? QuoteFreshness.EndOfDay,
                fx, manualFx ? instrument.ManualPriceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : rate?.RetrievedUtc,
                open.Any(l => l.Estimated) || realizedEstimated, manualFx));
        }
        return result;
    }
}
