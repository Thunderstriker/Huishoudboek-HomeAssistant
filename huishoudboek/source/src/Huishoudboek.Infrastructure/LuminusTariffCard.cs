using System.Globalization;
using System.Text.RegularExpressions;
using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

public sealed record LuminusCard(string Product, DateOnly? IssueMonth, decimal? SingleRate, decimal? DayRate, decimal? NightRate,
    decimal? SingleAnnualFee, decimal? DayNightAnnualFee, decimal? ConsumptionVat, decimal? InjectionVat,
    (decimal Coefficient, decimal Offset)? SingleInjection, (decimal Coefficient, decimal Offset)? DayInjection,
    (decimal Coefficient, decimal Offset)? NightInjection, bool FirstYearRule, bool SettlementBill);

public static class LuminusTariffCard
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-BE");
    private const string Number = @"\d{1,5}[,.]\d{1,8}";
    private const string Split = @"[ \t]+";
    private const string SharedPriceLabel = @"^[ \t]*Geleverde[ \t]+energie[ \t]*\(c€\s*/\s*kWh\)";
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    // Narrow, label-based BasicFix parser. Never copy illustrative index values, dates of supply, or grid charges.
    public static LuminusCard? Parse(IReadOnlyList<string> pages)
    {
        if (pages.Count == 0) return null;
        var first = pages[0].Replace('\u00a0', ' ');
        var second = pages.Count > 1 ? pages[1].Replace('\u00a0', ' ') : "";
        var title = One(first, @"^[ \t]*(?:(?<supplier>Luminus)[ \t]+)?(?<product>BasicFix(?:[ \t]+Online)?[ \t]+Elektriciteit)\b");
        var incl = Is(first, @"Luminus energieprijzen\s*\(incl\.\s*BTW\)");
        var meterHeading = Is(first, @"Enkelvoudige[^\n]*Tweevoudige");
        // The shared residential card has a single supplier-price table and no region header.
        // Do not infer a region from its grid-cost columns or from the general conditions.
        var sharedLayout = title.Groups["supplier"].Success && incl && meterHeading
            && Is(first, @"\bVerbruikers[ \t]*:[ \t]*residentieel\b")
            && Is(first, @"^[ \t]*Dag[ \t]+Nacht(?=\s|$)", RegexOptions.Multiline)
            && Is(first, SharedPriceLabel, RegexOptions.Multiline);
        var flemish = Is(first, @"\bRegio[ \t]*:[ \t]*Vlaanderen\b");
        if (!title.Success || !Is(first + second, @"\bLuminus\b") || !Is(first, @"Tarief:\s*vast\b")
            || (!flemish && (!sharedLayout || Is(first, @"\bRegio[ \t]*:")))) return null;
        var issue = One(first, @"^[ \t]*\(([a-z]+)[ \t]+(\d{4})\)");
        DateOnly? month = issue.Success && DateOnly.TryParseExact($"1 {issue.Groups[1].Value} {issue.Groups[2].Value}", "d MMMM yyyy", Dutch, DateTimeStyles.None, out var date)
            && BelgianEnergyTime.Supported(date) ? date : null;
        var consumptionVat = One(first, @"(\d{1,2})\s*%\s*BTW\b");
        var heading = sharedLayout || meterHeading && Is(first, @"Piekuren[^\n]*Daluren");
        var priceLabel = (sharedLayout ? SharedPriceLabel : @"^[ \t]*Energieprijs[ \t]*\(c€\s*/\s*kWh\)") + @"[ \t]*[¹²³123]?";
        var priceValues = $@"({Number}){Split}({Number}){Split}({Number})(?=\s|$)";
        var prices = heading && incl ? One(first, $"{priceLabel}{Split}{priceValues}") : Match.Empty;
        // Poppler can place the row values a line below the label when the source baselines differ.
        if (heading && incl && !prices.Success)
            prices = One(first, $@"{priceLabel}(?:[ \t]{{8,}}[^\r\n]*)?\r?\n{Split}{priceValues}");
        var fee = heading && incl ? One(first, $@"^[ \t]*Vaste vergoeding[ \t]*\(€\s*/\s*jaar\)[ \t]*[¹²³123]?(?:{Split})({Number}){Split}({Number})(?=\s|$)") : Match.Empty;
        var injectionVat = One(first, @"BTW[ \t]+bedraagt[ \t]+(\d{1,2})\s*%");
        var left = string.Join('\n', second.Split('\n').Select(l => Regex.Split(l.Trim(), @"[ \t]{8,}", RegexOptions.CultureInvariant, Timeout)[0]));
        var isMonthly = Is(left, @"maandelijks\s+geïndexeerd") && Is(left, @"Belpex\s*=\s*\d+[,.]\d+\s*€\s*/\s*MWh")
            && (!sharedLayout || Is(left, @"Tariefformule voor de vergoeding van geïnjecteerde energie\b"));
        (decimal, decimal)? Formula(string label)
        {
            if (!isMonthly) return null;
            var m = One(left, $@"{label}(?:[ \t]+[^=\r\n]{{1,160}}\r?\n)?\s*=\s*({Number})\s*[x×*]\s*Belpex\s*([+\-−])\s*({Number})");
            if (Value(m, 1) is not { } factor || Value(m, 3) is not { } offset) return null;
            return (factor, m.Groups[2].Value == "+" ? offset : -offset);
        }
        var firstYear = Is(left, @"eerste jaar na de start van levering.*vaste vergoeding verschuldigd", RegexOptions.Singleline)
            && Is(second, @"verhouding tot de werkelijke verdere duur van onafgebroken levering");
        return new(Regex.Replace(title.Groups["product"].Value, @"\s+", " "), month, Value(prices, 1) / 100, Value(prices, 2) / 100, Value(prices, 3) / 100,
            Value(fee, 1), Value(fee, 2), incl ? Value(consumptionVat, 1) : null, Value(injectionVat, 1),
            Formula(sharedLayout ? @"\bDag \(dagmeter\)" : "Enkelvoudige meter"),
            Formula(sharedLayout ? @"\bDag \(dag- en nachtmeter\)" : @"Piekuren \(Tweevoudige meter\)"),
            Formula(sharedLayout ? @"(?:^|;)[ \t]*Nacht\b" : "Daluren"), firstYear,
            Is(second, @"aangerekend op de afrekenings- of slotfactuur"));
    }

    private static Match One(string text, string pattern)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, Timeout);
        return m.Success && !m.NextMatch().Success ? m : Match.Empty;
    }
    private static bool Is(string text, string regex, RegexOptions options = RegexOptions.None) =>
        Regex.IsMatch(text, regex, options | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static decimal? Value(Match match, int group) => match.Success && decimal.TryParse(match.Groups[group].Value.Replace(',', '.'),
        NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
}
