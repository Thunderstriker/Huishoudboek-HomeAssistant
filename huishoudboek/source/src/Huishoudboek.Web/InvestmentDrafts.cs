using Huishoudboek.Core;

namespace Huishoudboek.Web;

public sealed class InvestmentInstrumentDraft
{
    public InvestmentInstrument Item { get; }
    public ReceiptNumberDraft Price { get; }
    public ReceiptNumberDraft PreviousClose { get; }
    public ReceiptNumberDraft EurRate { get; }
    public InvestmentInstrumentDraft(InvestmentInstrument item)
    {
        Item = item with { };
        if (Item.ProviderSymbol.Length > 0) Item.MappingConfirmed = false;
        Price = new(item.ManualPrice, UiCulture.Culture, decimalPlaces: 8, minimum: 0);
        PreviousClose = Number(item.ManualPreviousClose); EurRate = Number(item.ManualEurRate, 1_000_000);
    }
    public static ReceiptNumberDraft Number(decimal? value, decimal maximum = 1_000_000_000) =>
        new(value, UiCulture.Culture, decimalPlaces: 8, minimum: .00000001m, maximum: maximum);
    public bool Valid => Price.IsValid && PreviousClose.IsValid && EurRate.IsValid;
    public InvestmentInstrument Build() => Item with { Name = Item.Name.Trim(), Currency = Item.Currency.Trim().ToUpperInvariant(),
        Isin = Item.Isin.Trim().ToUpperInvariant(), Exchange = Item.Exchange.Trim(), ProviderSymbol = Item.ProviderSymbol.Trim().ToUpperInvariant(),
        ManualPrice = Price.Value, ManualPreviousClose = PreviousClose.Value, ManualEurRate = EurRate.Value };
}

public sealed class InvestmentTradeDraft
{
    public InvestmentTrade Item { get; }
    public ReceiptNumberDraft Quantity { get; }
    public ReceiptNumberDraft Price { get; }
    public ReceiptNumberDraft Fees { get; }
    public ReceiptNumberDraft Tax { get; }
    public ReceiptNumberDraft FxCosts { get; }
    public ReceiptNumberDraft Settlement { get; }
    public ReceiptNumberDraft Rate { get; }
    public InvestmentTradeDraft(InvestmentTrade item)
    {
        Item = item with { };
        Quantity = InvestmentInstrumentDraft.Number(item.Quantity == 0 ? null : item.Quantity);
        Price = InvestmentInstrumentDraft.Number(item.UnitPrice);
        Fees = Costs(item.Fees); Tax = Costs(item.Tax); FxCosts = Costs(item.FxCosts);
        Settlement = InvestmentInstrumentDraft.Number(item.SettlementEur, 1_000_000_000_000_000_000m);
        Rate = InvestmentInstrumentDraft.Number(item.HistoricalEurRate, 1_000_000);
    }
    private static ReceiptNumberDraft Costs(decimal amount) => new(amount, UiCulture.Culture, decimalPlaces: 8, minimum: 0, emptyAsZero: true);
    public bool Valid => Quantity.IsValid && Quantity.Value > 0 && (Item.Kind == InvestmentTradeKind.Split ||
        Price.IsValid && (Item.Kind == InvestmentTradeKind.Opening || Price.Value > 0) && Fees.IsValid && Tax.IsValid && FxCosts.IsValid && Settlement.IsValid && Rate.IsValid);
    public InvestmentTrade Build(HouseholdData data)
    {
        var instrument = data.Instruments.SingleOrDefault(i => i.Id == Item.InstrumentId) ?? throw new InvalidOperationException(UiCulture.Text("InvestmentChooseInstrument"));
        return Item with { Currency = instrument.Currency, Quantity = Quantity.Value!.Value,
            UnitPrice = Item.Kind == InvestmentTradeKind.Split ? null : Price.Value,
            Fees = Item.Kind == InvestmentTradeKind.Split ? 0 : Fees.Value ?? 0,
            Tax = Item.Kind == InvestmentTradeKind.Split ? 0 : Tax.Value ?? 0,
            FxCosts = Item.Kind == InvestmentTradeKind.Split ? 0 : FxCosts.Value ?? 0,
            SettlementEur = Item.Kind == InvestmentTradeKind.Split ? null : Settlement.Value,
            HistoricalEurRate = Item.Kind == InvestmentTradeKind.Split ? null : Rate.Value, Note = Item.Note.Trim() };
    }
}
