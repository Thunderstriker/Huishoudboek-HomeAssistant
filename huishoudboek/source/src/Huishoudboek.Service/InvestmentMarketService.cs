using Huishoudboek.Core;
using System.Text.Json;

namespace Huishoudboek.Service;

/// <summary>Single outbound request lane shared by scheduled refresh, connection tests, lookup and manual actions.</summary>
public sealed class InvestmentMarketService(InvestmentMarketStore store, AlphaVantageClient client, AdministrationCoordinator administration) : IDisposable
{
    private readonly SemaphoreSlim _operation = new(1, 1);
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
            var test = new InvestmentInstrument { ProviderSymbol = "IBM", Currency = "USD" };
            using var json = await QueryAsync(new Dictionary<string, string> { ["function"] = "GLOBAL_QUOTE", ["symbol"] = "IBM" }, cancellationToken);
            _ = AlphaVantageClient.ParseQuote(json.RootElement, test, QuoteFreshness.EndOfDay, DateTime.UtcNow);
            store.Update(s => { s.LastSuccessUtc = DateTime.UtcNow; s.Status = "ConnectionOk"; });
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<InvestmentSymbol>> SearchAsync(string keywords, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keywords) || keywords.Length > 100) throw new InvalidOperationException("Voer een zoekterm van maximaal 100 tekens in.");
        IReadOnlyList<InvestmentSymbol> result = [];
        await ExecuteAsync(async () =>
        {
            using var json = await QueryAsync(new Dictionary<string, string> { ["function"] = "SYMBOL_SEARCH", ["keywords"] = keywords.Trim() }, cancellationToken);
            result = AlphaVantageClient.ParseSearch(json.RootElement);
            store.Update(s => s.Status = "SearchOk");
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
            if (state.LastAttemptUtc > now.AddSeconds(-30)) throw new MarketDataException("RefreshCooldown");
            // Persist the next run BEFORE any outbound request; never replay missed runs after a restart.
            store.Update(s => s.NextRefreshUtc = NextRefresh(s.Settings, now));
            var data = administration.Read().Data;
            var held = Investments.Positions(data, [], [], DateOnly.FromDateTime(DateTime.Today)).Where(p => p.Quantity > 0).ToArray();
            var instruments = held.Select(p => p.Instrument).Where(i => i.ProviderSymbol.Length > 0).DistinctBy(i => i.Id).ToArray();
            if (instruments.Length == 0 && held.All(p => p.Instrument.Currency == "EUR")) { store.Update(s => s.Status = "NoSymbols"); return; }
            var failed = false;
            // Currency rates are shared by every holding; fetching them first avoids a fully priced but unconvertible portfolio.
            foreach (var currency in held.Select(p => p.Instrument.Currency).Where(c => c != "EUR").Distinct())
            {
                try
                {
                    using var json = await QueryAsync(new Dictionary<string, string> { ["function"] = "CURRENCY_EXCHANGE_RATE", ["from_currency"] = currency, ["to_currency"] = "EUR" }, cancellationToken);
                    var rate = AlphaVantageClient.ParseFx(json.RootElement, currency, DateTime.UtcNow);
                    store.Update(s =>
                    {
                        var old = s.Rates.FirstOrDefault(r => r.Currency == currency);
                        if (old != null && old.RetrievedUtc > rate.RetrievedUtc) { failed = true; return; }
                        s.Rates.RemoveAll(r => r.Currency == currency); s.Rates.Add(rate);
                    });
                }
                catch (MarketDataException ex) when (ex.Code is "InvalidResponse" or "SymbolUnavailable") { failed = true; }
            }
            foreach (var group in instruments.GroupBy(i => (i.ProviderSymbol, i.Currency, i.IsUsListing))
                .OrderBy(g => g.Select(i => state.Quotes.FirstOrDefault(q => q.InstrumentId == i.Id && q.Symbol == i.ProviderSymbol && q.Currency == i.Currency)?.RetrievedUtc ?? DateTime.MinValue).Min()))
            {
                var first = group.First();
                var freshness = first.IsUsListing ? state.Settings.Freshness : QuoteFreshness.EndOfDay;
                var parameters = new Dictionary<string, string> { ["function"] = "GLOBAL_QUOTE", ["symbol"] = first.ProviderSymbol };
                if (freshness != QuoteFreshness.EndOfDay) parameters["entitlement"] = freshness == QuoteFreshness.Realtime ? "realtime" : "delayed";
                try
                {
                    using var json = await QueryAsync(parameters, cancellationToken);
                    var quote = AlphaVantageClient.ParseQuote(json.RootElement, first, freshness, DateTime.UtcNow);
                    store.Update(s =>
                    {
                        foreach (var instrument in group)
                        {
                            var old = s.Quotes.FirstOrDefault(q => q.InstrumentId == instrument.Id && q.Symbol == quote.Symbol && q.Currency == quote.Currency);
                            if (old != null && old.PriceDate > quote.PriceDate) { failed = true; continue; }
                            s.Quotes.RemoveAll(q => q.InstrumentId == instrument.Id);
                            s.Quotes.Add(quote with { InstrumentId = instrument.Id });
                        }
                    });
                }
                catch (MarketDataException ex) when (ex.Code is "SymbolUnavailable" or "InvalidResponse") { failed = true; }
            }
            store.Update(s => { s.LastSuccessUtc = failed ? s.LastSuccessUtc : DateTime.UtcNow; s.Status = failed ? "PartialUpdate" : "Updated";
                s.Quotes.RemoveAll(q => !data.Instruments.Any(i => i.Id == q.InstrumentId)); });
        }, cancellationToken);
    }

    private async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        // Fail concurrent UI actions rather than queueing multiple refreshes and wasting quota.
        if (!await _operation.WaitAsync(0, cancellationToken)) throw new MarketDataException("Busy");
        try { await action(); }
        catch (MarketDataException ex)
        {
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
        { store.Update(s => s.Status = "InvalidResponse"); throw new MarketDataException("InvalidResponse"); }
        finally { _operation.Release(); }
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
