using System.Globalization;
using System.Text.Json;
using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

public static class EnergyChartsPrices
{
    public const string SourceUrl = "https://api.energy-charts.info/";
    public const string LicenseUrl = "https://creativecommons.org/licenses/by/4.0/";
    // The verified BE day-ahead market time unit changed for delivery on this date.
    private static readonly DateOnly QuarterHourStart = new(2025, 10, 1);

    public static IEnumerable<(DateOnly From, DateOnly Through)> Requests(DateOnly from, DateOnly through)
    {
        if (from < QuarterHourStart && through >= QuarterHourStart)
        {
            yield return (from, QuarterHourStart.AddDays(-1));
            yield return (QuarterHourStart, through);
        }
        else yield return (from, through);
    }

    public static string Path(DateOnly from, DateOnly through) => FormattableString.Invariant($"v2/price?bzn=BE&start={from:yyyy-MM-dd}&end={through:yyyy-MM-dd}");

    public static IReadOnlyList<EnergyMarketPrice> Parse(JsonElement body, DateOnly from, DateOnly through, DateTime fetchedUtc)
    {
        if (Text(body, "endpoint") != "price" || Text(body, "bidding_zone") != "BE" || Text(body, "timezone") != "Europe/Brussels" ||
            Text(body, "unit") != "EUR / MWh" || Text(body, "license") is not { } license || !license.Contains("CC BY 4.0", StringComparison.Ordinal) ||
            !license.Contains("SMARD.de", StringComparison.Ordinal) || !body.TryGetProperty("interval_minutes", out var interval) ||
            interval.ValueKind != JsonValueKind.Number || !interval.TryGetInt32(out var minutes) || minutes is not (15 or 60) ||
            !body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 3500)
            throw new InvalidDataException("De prijsbron stuurde een onbekende markt, licentie, eenheid of intervalduur.");
        var first = BelgianEnergyTime.MidnightUtc(from);
        var until = BelgianEnergyTime.MidnightUtc(through.AddDays(1));
        var generated = Timestamp(Text(body, "generated_at"));
        var result = new List<EnergyMarketPrice>();
        var seen = new HashSet<DateTime>();
        foreach (var row in data.EnumerateArray())
        {
            if (Timestamp(Text(row, "timestamp")) is not { } utc || utc < first || utc >= until || !seen.Add(utc) ||
                !row.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object || !values.TryGetProperty("day_ahead_price", out var price))
                throw new InvalidDataException("De prijsbron stuurde een ongeldig of dubbel marktinterval.");
            if (price.ValueKind == JsonValueKind.Null) continue;
            if (price.ValueKind != JsonValueKind.Number || !price.TryGetDecimal(out var euros)) throw new InvalidDataException("De prijsbron stuurde een ongeldige marktprijs.");
            var quote = new EnergyMarketPrice { StartUtc = utc, EndUtc = utc.AddMinutes(minutes), EuroPerMwh = euros, SourceGeneratedUtc = generated, FetchedUtc = fetchedUtc };
            if (!EnergyTerms.ValidPrice(quote) || quote.EndUtc > until) throw new InvalidDataException("De prijsbron stuurde een ongeldige marktprijs of intervalgrens.");
            result.Add(quote);
        }
        return result;
    }

    private static DateTime? Timestamp(string? value)
    {
        if (value == null || (!value.EndsWith('Z') && (value.Length < 6 || value[^6] is not ('+' or '-')))) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time.UtcDateTime : null;
    }
    private static string? Text(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
