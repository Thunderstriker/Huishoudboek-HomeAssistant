using System.Globalization;
using System.Text.Json;
using Huishoudboek.Core;

namespace Huishoudboek.Service;

/// <summary>Fixed HTTPS host; no HttpClientFactory logging handlers, redirects, raw errors or secret-bearing URLs escape.</summary>
public sealed class AlphaVantageClient : IDisposable
{
    public sealed record Response(JsonDocument Document, int HttpStatusCode) : IDisposable
    {
        public void Dispose() => Document.Dispose();
    }

    private readonly HttpClient _http;
    public AlphaVantageClient() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    public AlphaVantageClient(HttpMessageHandler handler) => _http = new(handler) { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<Response> QueryAsync(string key, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        try
        {
            // ResponseHeadersRead does not apply HttpClient.Timeout to subsequent body reads.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_http.Timeout);
            var requestToken = timeout.Token;
            var query = string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
            using var response = await _http.GetAsync("https://www.alphavantage.co/query?" + query + "&apikey=" + Uri.EscapeDataString(key),
                HttpCompletionOption.ResponseHeadersRead, requestToken);
            if ((int)response.StatusCode == 429) throw new MarketDataException("ProviderLimit", 429, RetryAfterUtc(response), InvestmentProviderReason.RequestLimit);
            if (!response.IsSuccessStatusCode) throw new MarketDataException("HttpError", (int)response.StatusCode);
            await using var input = await response.Content.ReadAsStreamAsync(requestToken);
            using var output = new MemoryStream();
            var buffer = new byte[8192]; int count;
            while ((count = await input.ReadAsync(buffer, requestToken)) > 0)
            {
                if (output.Length + count > 1_048_576) throw new MarketDataException("InvalidResponse", (int)response.StatusCode);
                output.Write(buffer, 0, count);
            }
            JsonDocument document;
            try { document = JsonDocument.Parse(output.ToArray()); }
            catch (JsonException) { throw new MarketDataException("InvalidResponse", (int)response.StatusCode); }
            try { Check(document.RootElement); return new(document, (int)response.StatusCode); }
            catch (MarketDataException ex)
            {
                document.Dispose();
                throw new MarketDataException(ex.Code, (int)response.StatusCode, ex.Code == "ProviderLimit" ? RetryAfterUtc(response) : null, ex.ProviderReason);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new MarketDataException("Timeout"); }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        { throw new MarketDataException("ConnectionFailed"); }
    }

    private static DateTime? RetryAfterUtc(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        var now = DateTime.UtcNow;
        if (retry?.Date is { } date) return date.UtcDateTime;
        if (retry?.Delta is { } delta && delta >= TimeSpan.Zero && delta <= DateTime.MaxValue - now)
            return now.Add(delta);
        return null;
    }

    public static void Check(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new MarketDataException("InvalidResponse");
        if (root.TryGetProperty("Error Message", out _))
            throw new MarketDataException("SymbolUnavailable", providerReason: InvestmentProviderReason.ProviderErrorMessage);
        static int Priority(InvestmentProviderReason reason) => reason switch
        {
            InvestmentProviderReason.InvalidApiKey => 0,
            InvestmentProviderReason.RequestLimit => 1,
            InvestmentProviderReason.PremiumEndpoint or InvestmentProviderReason.MissingEntitlement => 2,
            _ => 3
        };
        (string Code, InvestmentProviderReason Reason)? rejection = null;
        foreach (var name in new[] { "Note", "Information" })
            if (root.TryGetProperty(name, out var error))
            {
                var candidate = ClassifyNotice(error.ToString());
                // A generic upgrade notice must not hide a concrete key/quota error in the other field.
                if (rejection == null || Priority(candidate.Reason) < Priority(rejection.Value.Reason)) rejection = candidate;
            }
        if (rejection is { } rejected) throw new MarketDataException(rejected.Code, providerReason: rejected.Reason);
    }

    private static (string Code, InvestmentProviderReason Reason) ClassifyNotice(string text)
    {
        bool Contains(params string[] phrases) => phrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (Contains("invalid key", "invalid api key", "api key is invalid", "apikey is invalid", "api key is not valid", "missing api key", "api key is missing"))
            return ("KeyOrAccessInvalid", InvestmentProviderReason.InvalidApiKey);
        if (Contains("rate limit", "requests per day", "requests per minute", "too many requests", "quota exceeded", "quota exhausted") ||
            Contains("frequency") && Contains("exceed", "call frequency", "frequency limit"))
            return ("ProviderLimit", InvestmentProviderReason.RequestLimit);
        if (Contains("this is a premium endpoint", "this endpoint is premium", "this endpoint requires a premium", "this function is a premium endpoint"))
            return ("EntitlementRequired", InvestmentProviderReason.PremiumEndpoint);
        if (Contains("premium access required", "premium access is required", "entitlement required", "entitlement is required", "required entitlement", "not entitled", "missing entitlement"))
            return ("EntitlementRequired", InvestmentProviderReason.MissingEntitlement);
        // Merely mentioning premium plans or entitlements is not evidence that the requested endpoint requires them.
        return ("ProviderRejected", InvestmentProviderReason.UnrecognizedNotice);
    }

    public static InvestmentQuote ParseQuote(JsonElement root, InvestmentInstrument instrument, QuoteFreshness freshness, DateTime now)
    {
        Check(root);
        if (!root.TryGetProperty("Global Quote", out var q) || q.ValueKind != JsonValueKind.Object || !q.EnumerateObject().Any())
            throw new MarketDataException("EmptyQuote");
        if (!q.TryGetProperty("01. symbol", out var symbol) || symbol.ValueKind != JsonValueKind.String ||
            !string.Equals(symbol.GetString(), instrument.ProviderSymbol, StringComparison.OrdinalIgnoreCase))
            throw new MarketDataException("SymbolMismatch");
        if (!TryDecimal(q, "05. price", out var price) || price is <= 0 or > 1_000_000_000 ||
            !q.TryGetProperty("07. latest trading day", out var date) || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
            day > DateOnly.FromDateTime(now.AddDays(1)) || day.Year < 1900)
            throw new MarketDataException("InvalidResponse");
        decimal? previous = TryDecimal(q, "08. previous close", out var close) && close > 0 ? close : null;
        return new(instrument.Id, instrument.ProviderSymbol, instrument.Currency, price, previous, day, now, freshness);
    }

    public static InvestmentFxRate ParseFx(JsonElement root, string currency, DateTime now)
    {
        Check(root);
        if (!root.TryGetProperty("Realtime Currency Exchange Rate", out var fx) ||
            !fx.TryGetProperty("1. From_Currency Code", out var from) || from.GetString() != currency ||
            !fx.TryGetProperty("3. To_Currency Code", out var to) || to.GetString() != "EUR" ||
            !TryDecimal(fx, "5. Exchange Rate", out var rate) || rate is <= 0 or > 1_000_000 ||
            !fx.TryGetProperty("6. Last Refreshed", out var refreshed) ||
            !fx.TryGetProperty("7. Time Zone", out var zone) || zone.GetString() != "UTC" ||
            !DateTime.TryParseExact(refreshed.GetString(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) || at > now.AddMinutes(5))
            throw new MarketDataException("InvalidResponse");
        return new(currency, rate, at);
    }

    public static IReadOnlyList<InvestmentSymbol> ParseSearch(JsonElement root)
    {
        Check(root);
        if (!root.TryGetProperty("bestMatches", out var matches) || matches.ValueKind != JsonValueKind.Array)
            throw new MarketDataException("InvalidResponse");
        return matches.EnumerateArray().Take(30).Select(m => new InvestmentSymbol(m.GetProperty("1. symbol").GetString() ?? "",
            m.GetProperty("2. name").GetString() ?? "", m.GetProperty("4. region").GetString() ?? "", m.GetProperty("8. currency").GetString() ?? ""))
            .Where(s => s.Symbol.Length is > 0 and <= 40 && Investments.CurrencyValid(s.Currency)).ToArray();
    }
    private static bool TryDecimal(JsonElement root, string name, out decimal value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element) && decimal.TryParse(element.ToString(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
    }
    public void Dispose() => _http.Dispose();
}
