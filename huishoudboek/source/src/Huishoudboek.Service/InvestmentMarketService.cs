using Huishoudboek.Core;
using System.Text.Json;

namespace Huishoudboek.Service;

/// <summary>Single outbound request lane shared by scheduled refresh, connection tests, lookup and manual actions.</summary>
public sealed class InvestmentMarketService(InvestmentMarketStore store, AlphaVantageClient client, AdministrationCoordinator administration) : IDisposable
{
    private readonly SemaphoreSlim _operation = new(1, 1);
    private bool _reportActive;
    public InvestmentMarketSnapshot Read() => store.Snapshot(DateTime.UtcNow);

    public async Task SaveSettingsAsync(InvestmentQuoteSettings settings, string? replacementKey, bool removeKey)
    {
        settings.Validate();
        if (!string.IsNullOrEmpty(replacementKey) && (replacementKey.Length is < 8 or > 256 ||
            !replacementKey.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new InvalidOperationException("Ongeldige API-sleutel. Voer alleen de eigen Alpha Vantage-sleutel in.");
        await _operation.WaitAsync();
        try
        {
            store.Update(s =>
            {
                if (removeKey) s.ProtectedKey = "";
                else if (!string.IsNullOrEmpty(replacementKey)) s.ProtectedKey = store.Protect(replacementKey);
                if (settings.Enabled && s.ProtectedKey.Length == 0) throw new InvalidOperationException("Voer eerst uw eigen API-sleutel in.");
                s.Settings = settings with { };
                s.NextRefreshUtc = settings.Enabled ? NextRefresh(settings, DateTime.UtcNow) : null;
                s.Status = s.ProtectedKey.Length == 0 ? "NotConfigured" : settings.Enabled ? "Ready" : "Disabled";
                // Key replacement, disable/re-enable, and financial restore do not reset quota or provider cooldown.
            });
        }
        finally { _operation.Release(); }
    }

    public static DateTime? NextRefresh(InvestmentQuoteSettings settings, DateTime utc)
    {
        if (!settings.Enabled || settings.Frequency == QuoteFrequency.Manual) return null;
        if (settings.Frequency == QuoteFrequency.Daily)
        {
            var zone = TimeZoneInfo.Local;
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
            var next = DateTime.SpecifyKind(local.Date.AddHours(settings.DailyHour), DateTimeKind.Unspecified);
            if (next <= local) next = next.AddDays(1);
            // Handle DST gaps without crashing the scheduler.
            while (zone.IsInvalidTime(next)) next = next.AddMinutes(30);
            return TimeZoneInfo.ConvertTimeToUtc(next, zone);
        }
        return settings.Frequency switch
        {
            QuoteFrequency.SeveralPerDay => utc.AddHours(24d / settings.UpdatesPerDay),
            QuoteFrequency.Hourly => utc.AddHours(1),
            _ => utc.AddSeconds(settings.RealtimeSeconds)
        };
    }

    public async Task TestAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            BeginReport("Test", [new("Test", "IBM", "Pending", InvestmentDataState.NotApplicable)]);
            await RunRequestAsync(0, async () =>
            {
                var test = new InvestmentInstrument { ProviderSymbol = "IBM", Currency = "USD" };
                using var json = await QueryAsync(new Dictionary<string, string> { ["function"] = "GLOBAL_QUOTE", ["symbol"] = "IBM" }, cancellationToken);
                _ = AlphaVantageClient.ParseQuote(json.RootElement, test, QuoteFreshness.EndOfDay, DateTime.UtcNow);
                return new("Test", "IBM", "Updated", InvestmentDataState.NotApplicable);
            });
            CompleteReport("ConnectionOk", markSuccess: true);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<InvestmentSymbol>> SearchAsync(string keywords, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keywords) || keywords.Length > 100) throw new InvalidOperationException("Voer een zoekterm van maximaal 100 tekens in.");
        IReadOnlyList<InvestmentSymbol> result = [];
        await ExecuteAsync(async () =>
        {
            // Search terms and provider response text do not belong in persistent diagnostics.
            BeginReport("Search", [new("Search", "", "Pending", InvestmentDataState.NotApplicable)]);
            await RunRequestAsync(0, async () =>
            {
                using var json = await QueryAsync(new Dictionary<string, string> { ["function"] = "SYMBOL_SEARCH", ["keywords"] = keywords.Trim() }, cancellationToken);
                result = AlphaVantageClient.ParseSearch(json.RootElement);
                return new("Search", "", "Updated", InvestmentDataState.NotApplicable);
            });
            CompleteReport("SearchOk");
        }, cancellationToken);
        return result;
    }

    public async Task RefreshAsync(bool scheduled = false, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            var state = store.Read(s => s);
            var now = DateTime.UtcNow;
            if (scheduled && (!state.Settings.Enabled || state.Settings.Frequency == QuoteFrequency.Manual || state.NextRefreshUtc > now)) return;
            var data = administration.Read().Data;
            var held = Investments.Positions(data, [], [], DateOnly.FromDateTime(DateTime.Today)).Where(p => p.Quantity > 0).ToArray();
            var instruments = held.Select(p => p.Instrument).Where(i => i.ProviderSymbol.Length > 0).DistinctBy(i => i.Id).ToArray();
            var currencies = held.Select(p => p.Instrument.Currency).Where(c => c != "EUR").Distinct().ToArray();
            var groups = instruments.GroupBy(i => (i.ProviderSymbol, i.Currency, i.IsUsListing))
                .OrderBy(g => g.Select(i => state.Quotes.FirstOrDefault(q => q.InstrumentId == i.Id && q.Symbol == i.ProviderSymbol && q.Currency == i.Currency)?.RetrievedUtc ?? DateTime.MinValue).Min()).ToArray();
            var requests = new List<InvestmentRequestResult>();
            foreach (var currency in currencies)
            {
                var cached = state.Rates.FirstOrDefault(r => r.Currency == currency);
                requests.Add(new("Fx", currency + "→EUR", "Pending", cached == null ? InvestmentDataState.Missing : InvestmentDataState.Retained, DataUtc: cached?.RetrievedUtc));
            }
            foreach (var group in groups)
            {
                var cached = state.Quotes.Where(q => group.Any(i => i.Id == q.InstrumentId && i.ProviderSymbol == q.Symbol && i.Currency == q.Currency)).OrderByDescending(q => q.PriceDate).FirstOrDefault();
                requests.Add(new("Quote", group.Key.ProviderSymbol, "Pending", cached == null ? InvestmentDataState.Missing : InvestmentDataState.Retained, PriceDate: cached?.PriceDate));
            }
            BeginReport("Refresh", requests);
            if (state.LastAttemptUtc > now.AddSeconds(-30)) throw new MarketDataException("RefreshCooldown");
            // Persist the next run BEFORE any outbound request; never replay missed runs after a restart.
            store.Update(s => s.NextRefreshUtc = NextRefresh(s.Settings, now));
            if (requests.Count == 0) { CompleteReport("NoSymbols"); return; }
            var index = 0;
            // An unsupported FX endpoint must not hide an otherwise available native stock quote.
            // Account/key, quota and connection failures still stop the entire request lane.
            foreach (var currency in currencies)
            {
                await RunRequestAsync(index++, async () =>
                {
                    using var json = await QueryAsync(new Dictionary<string, string> { ["function"] = "CURRENCY_EXCHANGE_RATE", ["from_currency"] = currency, ["to_currency"] = "EUR" }, cancellationToken);
                    var rate = AlphaVantageClient.ParseFx(json.RootElement, currency, DateTime.UtcNow);
                    store.Update(s =>
                    {
                        var old = s.Rates.FirstOrDefault(r => r.Currency == currency);
                        if (old != null && old.RetrievedUtc > rate.RetrievedUtc) throw new MarketDataException("OlderData");
                        s.Rates.RemoveAll(r => r.Currency == currency); s.Rates.Add(rate);
                    });
                    return new("Fx", currency + "→EUR", "Updated", InvestmentDataState.Updated, DataUtc: rate.RetrievedUtc);
                }, continueOnUnavailable: true);
            }
            foreach (var group in groups)
            {
                var first = group.First();
                var freshness = first.IsUsListing ? state.Settings.Freshness : QuoteFreshness.EndOfDay;
                var parameters = new Dictionary<string, string> { ["function"] = "GLOBAL_QUOTE", ["symbol"] = first.ProviderSymbol };
                if (freshness != QuoteFreshness.EndOfDay) parameters["entitlement"] = freshness == QuoteFreshness.Realtime ? "realtime" : "delayed";
                await RunRequestAsync(index++, async () =>
                {
                    using var json = await QueryAsync(parameters, cancellationToken);
                    var quote = AlphaVantageClient.ParseQuote(json.RootElement, first, freshness, DateTime.UtcNow);
                    store.Update(s =>
                    {
                        if (s.Quotes.Any(q => group.Any(i => i.Id == q.InstrumentId) && q.Symbol == quote.Symbol && q.Currency == quote.Currency && q.PriceDate > quote.PriceDate))
                            throw new MarketDataException("OlderData");
                        foreach (var instrument in group)
                        {
                            s.Quotes.RemoveAll(q => q.InstrumentId == instrument.Id);
                            s.Quotes.Add(quote with { InstrumentId = instrument.Id });
                        }
                    });
                    return new("Quote", first.ProviderSymbol, "Updated", InvestmentDataState.Updated, PriceDate: quote.PriceDate);
                }, continueOnUnavailable: true);
            }
            store.Update(s => s.Quotes.RemoveAll(q => !data.Instruments.Any(i => i.Id == q.InstrumentId)));
            CompleteReport("Updated", markSuccess: true);
        }, cancellationToken);
    }

    private void BeginReport(string operation, IReadOnlyList<InvestmentRequestResult> requests)
    {
        store.Update(s => s.Report = new(operation, DateTime.UtcNow, null, "Requesting", requests));
        _reportActive = true;
    }

    private void UpdateRequest(int index, Func<InvestmentRequestResult, InvestmentRequestResult> update) => store.Update(s =>
    {
        var report = s.Report!;
        var requests = report.Requests.ToArray();
        requests[index] = update(requests[index]);
        s.Report = report with { Requests = requests };
    });

    private async Task RunRequestAsync(int index, Func<Task<InvestmentRequestResult>> request, bool continueOnUnavailable = false)
    {
        UpdateRequest(index, r => r with { Status = "Requesting" });
        try { var result = await request(); UpdateRequest(index, _ => result); }
        catch (Exception ex) when (ex is MarketDataException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            var error = ex as MarketDataException ?? new MarketDataException("InvalidResponse");
            UpdateRequest(index, r => r with { Status = error.Code, HttpStatusCode = error.HttpStatusCode });
            if (!continueOnUnavailable || error.Code is not ("InvalidResponse" or "SymbolUnavailable" or "EmptyQuote" or "SymbolMismatch" or "OlderData" or "EntitlementRequired"))
                throw error;
        }
    }

    private void CompleteReport(string successStatus, bool markSuccess = false)
    {
        store.Update(s =>
        {
            var report = s.Report!;
            var failed = report.Requests.Any(r => r.Status != "Updated");
            s.Status = failed ? report.Requests.Any(r => r.Status == "Updated") ? "PartialUpdate" : "UpdateFailed" : successStatus;
            s.Report = report with { Status = s.Status, CompletedUtc = DateTime.UtcNow };
            if (!failed && markSuccess) s.LastSuccessUtc = DateTime.UtcNow;
        });
        _reportActive = false;
    }

    private void FailReport(string code)
    {
        if (!_reportActive) return;
        store.Update(s =>
        {
            var report = s.Report!;
            s.Report = report with { CompletedUtc = DateTime.UtcNow, ErrorCode = code,
                Status = report.Requests.Any(r => r.Status == "Updated") ? "PartialUpdate" : "UpdateFailed",
                Requests = report.Requests.Select(r => r.Status is "Pending" or "Requesting" ? r with { Status = "NotRequested" } : r).ToArray() };
        });
        _reportActive = false;
    }

    private async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        // Fail concurrent UI actions rather than queueing multiple refreshes and wasting quota.
        if (!await _operation.WaitAsync(0, cancellationToken)) throw new MarketDataException("Busy");
        _reportActive = false;
        try { await action(); }
        catch (MarketDataException ex)
        {
            FailReport(ex.Code);
            store.Update(s =>
            {
                s.Status = ex.Code;
                // Checking an existing pause must not extend it forever on an hourly schedule.
                if (ex.Code == "ProviderLimit" && !(s.BlockedUntilUtc > DateTime.UtcNow))
                    s.BlockedUntilUtc = DateTime.UtcNow.AddHours(24);
            });
            throw;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        { FailReport("InvalidResponse"); store.Update(s => s.Status = "InvalidResponse"); throw new MarketDataException("InvalidResponse"); }
        catch (OperationCanceledException)
        { FailReport("Interrupted"); store.Update(s => s.Status = "Interrupted"); throw; }
        finally { _reportActive = false; _operation.Release(); }
    }

    private async Task<System.Text.Json.JsonDocument> QueryAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var key = store.Key();
        if (key.Length == 0) throw new MarketDataException("NotConfigured");
        var settings = store.Read(s => s.Settings);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTime.UtcNow;
            var wait = TimeSpan.Zero;
            store.Update(s =>
            {
                if (!s.Settings.Enabled) throw new MarketDataException("Disabled");
                if (s.BlockedUntilUtc > now) throw new MarketDataException("ProviderLimit");
                if (s.BudgetDayUtc != DateOnly.FromDateTime(now)) { s.BudgetDayUtc = DateOnly.FromDateTime(now); s.RequestsToday = 0; }
                if (settings.DailyLimit > 0 && s.RequestsToday >= settings.DailyLimit) throw new MarketDataException("DailyLimit");
                s.RecentRequestsUtc.RemoveAll(t => t <= now.AddMinutes(-1));
                if (s.RecentRequestsUtc.Count >= settings.RequestsPerMinute)
                { wait = s.RecentRequestsUtc.Min().AddMinutes(1).AddMilliseconds(100) - now; return; }
                // A failed request still consumes quota. Reserve durably before sending.
                s.RequestsToday++; s.RecentRequestsUtc.Add(now); s.LastAttemptUtc = now;
            });
            if (wait <= TimeSpan.Zero) break;
            await Task.Delay(wait, cancellationToken);
        }
        return await client.QueryAsync(key, parameters, cancellationToken);
    }
    public void Dispose() => _operation.Dispose();
}

public sealed class InvestmentQuoteWorker(InvestmentMarketService market) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var state = market.Read();
                if (state.Settings.Enabled && state.Settings.Frequency != QuoteFrequency.Manual &&
                    (state.NextRefreshUtc == null || state.NextRefreshUtc <= DateTime.UtcNow))
                    await market.RefreshAsync(scheduled: true, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            // Provider failures must never stop the household app, or log request URLs/secrets.
            catch (Exception) { }
        }
    }
}
