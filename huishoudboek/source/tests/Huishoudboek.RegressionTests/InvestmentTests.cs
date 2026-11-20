using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Claims;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class InvestmentTests
{
    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("Investments weight all purchase lots rather than averaging returns", () =>
        {
            var f = new Portfolio(); f.Buy(10, 100); f.Buy(5, 120); f.Instrument.ManualPrice = 110;
            var p = f.Position(); Equal(15m, p.Quantity); Equal<decimal?>(1600, p.RemainingCost); Equal<decimal?>(3.125m, p.GainPercent);
            Equal<decimal?>(50, p.GainEur); Equal<decimal?>(1600m / 15, p.AverageCost);
            f.Instrument.ManualPreviousClose = 100; Equal<decimal?>(10, f.Position().DailyPercent);
        });
        yield return ("FIFO removes oldest purchase costs and sale expenses exactly once", () =>
        {
            var f = new Portfolio(); f.Buy(10, 100).Fees = 10; f.Buy(5, 120).Tax = 5;
            var sale = f.Trade(InvestmentTradeKind.Sell, 12, 150); sale.Fees = 4; sale.Tax = 2;
            var p = f.Position(); Equal(3m, p.Quantity); Equal<decimal?>(363, p.RemainingCost);
            Equal<decimal?>(542, p.Realized); Equal<decimal?>(121, p.AverageCost);
            f.Trade(InvestmentTradeKind.Sell, 3, 140);
            p = f.Position(); Equal(0m, p.Quantity); Equal<decimal?>(0, p.RemainingCost); Equal<decimal?>(599, p.Realized);
        });
        yield return ("Actual rounded EUR settlement is used consistently for native and EUR costs", () =>
        {
            var f = new Portfolio(); f.Buy(3, 10.001m).SettlementEur = 30;
            var p = f.Position(); Equal<decimal?>(30, p.RemainingCost); Equal(p.RemainingCost, p.RemainingCostEur);
            f.Data.InvestmentTrades[0].SettlementEur = 20; Throws(() => Validation.Validate(f.Data));
        });
        yield return ("Fractional lots preserve residual costs after repeated partial sales", () =>
        {
            var f = new Portfolio(); f.Buy(.3m, 100).Fees = .01m;
            f.Trade(InvestmentTradeKind.Sell, .1m, 110); f.Trade(InvestmentTradeKind.Sell, .1m, 110); f.Trade(InvestmentTradeKind.Sell, .1m, 110);
            Equal<decimal?>(2.99m, f.Position().Realized); Equal<decimal?>(0, f.Position().RemainingCost);
        });
        yield return ("Actual historical EUR settlements take precedence over current and historical FX", () =>
        {
            var f = new Portfolio("USD"); var first = f.Buy(10, 100); first.SettlementEur = 800; first.HistoricalEurRate = .7m;
            f.Buy(5, 120).SettlementEur = 500;
            f.Instrument.ManualPrice = 110;
            var p = f.Position([new("USD", .9m, DateTime.UtcNow)]);
            Equal<decimal?>(1300, p.RemainingCostEur); Equal<decimal?>(1485, p.ValueEur);
            Equal<decimal?>(3.125m, p.GainPercent); Equal<decimal?>(185m / 1300 * 100, p.GainEurPercent);
            Equal(false, p.EstimatedEurCost);
            var sale = f.Trade(InvestmentTradeKind.Sell, 5, 120); sale.SettlementEur = 550;
            p = f.Position([new("USD", .5m, DateTime.UtcNow)]);
            Equal<decimal?>(900, p.RemainingCostEur); Equal<decimal?>(150, p.RealizedEur);
        });
        yield return ("Missing historical FX never uses current FX for purchase costs", () =>
        {
            var f = new Portfolio("USD"); f.Buy(2, 100); f.Instrument.ManualPrice = 110;
            var p = f.Position([new("USD", .9m, DateTime.UtcNow)]);
            Equal<decimal?>(198, p.ValueEur); Equal<decimal?>(null, p.RemainingCostEur); Equal<decimal?>(null, p.GainEurPercent);
            Equal<decimal?>(10, p.GainPercent);
            f.Data.InvestmentTrades[0].HistoricalEurRate = .8m;
            p = f.Position(); True(p.EstimatedEurCost); Equal<decimal?>(160, p.RemainingCostEur);
        });
        yield return ("Unknown opening costs stay unknown until those FIFO lots leave the holding", () =>
        {
            var f = new Portfolio(); f.Trade(InvestmentTradeKind.Opening, 5, null); f.Buy(5, 100); f.Instrument.ManualPrice = 110;
            Equal<decimal?>(null, f.Position().GainPercent);
            f.Trade(InvestmentTradeKind.Sell, 5, 110);
            var p = f.Position(); Equal<decimal?>(10, p.GainPercent); Equal<decimal?>(null, p.RealizedEur);
        });
        yield return ("Splits adjust quantities not costs and reject stale presplit quotes", () =>
        {
            var f = new Portfolio(); f.Buy(10, 100);
            f.Trade(InvestmentTradeKind.Split, 2, null).Date = Portfolio.Today.AddDays(-1);
            f.Instrument.ManualPriceDate = Portfolio.Today.AddDays(-2);
            Equal(20m, f.Position().Quantity); Equal<decimal?>(1000, f.Position().RemainingCost); Equal<decimal?>(null, f.Position().Price);
            f.Instrument.ManualPrice = 55; f.Instrument.ManualPriceDate = Portfolio.Today;
            Equal<decimal?>(10, f.Position().GainPercent);
            f.Trade(InvestmentTradeKind.Sell, 5, 60).Date = Portfolio.Today;
            Equal<decimal?>(750, f.Position().RemainingCost); Equal<decimal?>(50, f.Position().Realized);
        });
        yield return ("Reverse splits retain precise fractional shares", () =>
        {
            var f = new Portfolio(); f.Buy(1.25m, 100); f.Trade(InvestmentTradeKind.Split, .1m, null);
            Equal(.125m, f.Position().Quantity); Equal<decimal?>(125, f.Position().RemainingCost);
        });
        yield return ("Historical edits, currency mismatch, overselling and ambiguous order are rejected", () =>
        {
            var f = new Portfolio(); var buy = f.Buy(10, 100); var sell = f.Trade(InvestmentTradeKind.Sell, 5, 110);
            sell.Date = buy.Date.AddDays(-1); Throws(() => Validation.Validate(f.Data)); sell.Date = buy.Date;
            sell.Sequence = buy.Sequence; Throws(() => Validation.Validate(f.Data)); sell.Sequence = 2;
            sell.Currency = "USD"; Throws(() => Validation.Validate(f.Data)); sell.Currency = "EUR";
            sell.Quantity = 11; Throws(() => Validation.Validate(f.Data));
        });
        yield return ("Old purchases can predate the app's account opening balance", () =>
        {
            var f = new Portfolio(); f.Buy(10, 100).Date = new(2001, 1, 1);
            Validation.Validate(f.Data); Equal(10m, f.Position().Quantity);
        });
        yield return ("Investment scope follows account owners and never alters bookkeeping", () =>
        {
            var f = new Portfolio(); f.Account.OpeningBalance = 500;
            f.Data.Valuations.Add(new() { AccountId = f.Account.Id, Date = Portfolio.Today, SecuritiesValue = 200 });
            var before = Reporting.Accounts(f.Data, Portfolio.Today);
            f.Buy(10, 100); f.Instrument.ManualPrice = 110;
            var after = Reporting.Accounts(f.Data, Portfolio.Today);
            Equal(before.InvestmentTotal, after.InvestmentTotal); Equal(700m, after.InvestmentTotal);
            Equal(0, f.Data.Transactions.Count);
            Equal(1, Investments.Positions(f.Data, [], [], Portfolio.Today, f.Owner.Id).Count);
            Equal(0, Investments.Positions(f.Data, [], [], Portfolio.Today, Guid.NewGuid()).Count);
        });
        yield return ("Changed provider mappings cannot reuse prices from another symbol or currency", () =>
        {
            var f = new Portfolio("USD"); f.Instrument.ProviderSymbol = "AMD"; f.Instrument.MappingConfirmed = true; f.Buy(1, 100);
            var q = new InvestmentQuote(f.Instrument.Id, "AMZN", "USD", 110, 100, Portfolio.Today, DateTime.UtcNow, QuoteFreshness.EndOfDay);
            Equal<decimal?>(null, Investments.Positions(f.Data, [q], [], Portfolio.Today).Single().Price);
            q = q with { Symbol = "AMD", Currency = "EUR" };
            Equal<decimal?>(null, Investments.Positions(f.Data, [q], [], Portfolio.Today).Single().Price);
        });
        yield return ("Manual multi-currency tracking works without an API key", () =>
        {
            var f = new Portfolio("USD"); f.Buy(2, 100).SettlementEur = 180;
            f.Instrument.ManualPrice = 120; f.Instrument.ManualEurRate = .9m;
            var p = f.Position(); Equal<decimal?>(216, p.ValueEur); Equal<decimal?>(20, p.GainEurPercent); True(p.ManualFx);
        });
        yield return ("An explicitly entered worthless manual holding can show a full loss", () =>
        {
            var f = new Portfolio(); f.Buy(2, 100); f.Instrument.ManualPrice = 0;
            Equal<decimal?>(-100, f.Position().GainPercent); Equal<decimal?>(0, f.Position().ValueEur);
            True(new InvestmentInstrumentDraft(f.Instrument).Valid);
        });
        yield return ("Investment decimal drafts accept both separators and reject silently rounded inputs", () =>
        {
            var draft = new InvestmentTradeDraft(new() { Kind = InvestmentTradeKind.Buy });
            draft.Quantity.Text = "0,12345678"; draft.Price.Text = "12.12345678"; True(draft.Valid);
            draft.Price.Text = "12.123456789"; True(!draft.Valid);
            draft.Price.Text = ""; True(!draft.Valid); draft.Item.Kind = InvestmentTradeKind.Opening; True(draft.Valid);
        });
        yield return ("Investment SQLite round-trip and full database restore retain user records", () => InDirectory(directory =>
        {
            var f = new Portfolio("USD"); f.Buy(1.23456789m, 12.34567891m).SettlementEur = 15.23m;
            var store = new SqliteHouseholdStore(Path.Combine(directory, "source.db")); store.Save(f.Data);
            var path = Path.Combine(directory, "backup.db"); store.Backup(path);
            var target = new SqliteHouseholdStore(Path.Combine(directory, "target.db")); target.Restore(path);
            var loaded = target.Load(); Equal(1, loaded.Instruments.Count); Equal(1, loaded.InvestmentTrades.Count);
            Equal(f.Data.InvestmentTrades[0], loaded.InvestmentTrades[0]); Equal(f.Instrument, loaded.Instruments[0]);
        }));
        yield return ("Schema 14 upgrade and old restore preserve financials with empty investment defaults", () => InDirectory(directory =>
        {
            var f = new Portfolio(); var path = Path.Combine(directory, "old.db"); new SqliteHouseholdStore(path).Save(f.Data);
            using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
            { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = RecognitionTests.RemoveSchema16Columns + "DROP TABLE InvestmentTrade; DROP TABLE InvestmentInstrument; PRAGMA user_version=14;"; cmd.ExecuteNonQuery(); }
            var old = SqliteHouseholdStore.ReadBackup(path); Equal(0, old.Instruments.Count);
            var upgraded = new SqliteHouseholdStore(path, allowCreate: false); Equal(0, upgraded.Load().InvestmentTrades.Count);
            var backup = Directory.GetFiles(directory, "*.voor-versie-17-*.db").Single();
            upgraded.Save(f.Data); upgraded.Restore(backup); Equal(0, upgraded.Load().Instruments.Count);
        }));
        yield return ("Alpha Vantage parses previous close and rejects zero prices, wrong symbols and HTTP-200 errors", () =>
        {
            var f = new Portfolio("USD"); f.Instrument.ProviderSymbol = "IBM";
            using var json = JsonDocument.Parse(QuoteJson());
            var q = AlphaVantageClient.ParseQuote(json.RootElement, f.Instrument, QuoteFreshness.EndOfDay, DateTime.UtcNow);
            Equal(110m, q.Price); Equal<decimal?>(100, q.PreviousClose);
            foreach (var raw in new[] { QuoteJson("0"), QuoteJson(symbol: "OTHER"), "{\"Information\":\"API key private-secret is premium-only\"}", "{\"Note\":\"25 requests per day\"}" })
            {
                using var bad = JsonDocument.Parse(raw);
                try { AlphaVantageClient.ParseQuote(bad.RootElement, f.Instrument, QuoteFreshness.EndOfDay, DateTime.UtcNow); throw new Exception("Expected rejection"); }
                catch (MarketDataException ex) { True(!ex.ToString().Contains("private-secret")); }
            }
        });
        yield return ("FX validates direction, positive value and actual provider timestamp", () =>
        {
            using var json = JsonDocument.Parse(FxJson());
            var fx = AlphaVantageClient.ParseFx(json.RootElement, "USD", DateTime.UtcNow);
            Equal(.9m, fx.EurRate); Equal(new DateTime(2026, 11, 16, 21, 0, 0, DateTimeKind.Utc), fx.RetrievedUtc);
            try { AlphaVantageClient.ParseFx(json.RootElement, "GBP", DateTime.UtcNow); throw new Exception("Expected rejection"); } catch (MarketDataException) { }
        });
        yield return ("Free and paid schedules enforce entitlements and allow a deliberate paid budget", () =>
        {
            new InvestmentQuoteSettings().Validate();
            Throws(() => new InvestmentQuoteSettings { Freshness = QuoteFreshness.Realtime }.Validate());
            Throws(() => new InvestmentQuoteSettings { DailyLimit = 26 }.Validate());
            Throws(() => new InvestmentQuoteSettings { DailyLimit = 0 }.Validate());
            new InvestmentQuoteSettings { Paid = true, Frequency = QuoteFrequency.Realtime, Freshness = QuoteFreshness.Realtime, DailyLimit = 0, RequestsPerMinute = 75 }.Validate();
            var now = new DateTime(2026, 11, 16, 23, 0, 0, DateTimeKind.Utc);
            var next = InvestmentMarketService.NextRefresh(new() { Enabled = true }, now); True(next > now);
            Equal(7, next!.Value.ToLocalTime().Hour);
            Equal<DateTime?>(now.AddHours(1), InvestmentMarketService.NextRefresh(new() { Enabled = true, Frequency = QuoteFrequency.Hourly }, now));
            Equal<DateTime?>(null, InvestmentMarketService.NextRefresh(new() { Enabled = false }, now));
        });
        yield return ("Quota survives restarts and key replacement; snapshots and financial backups exclude secrets", () => InDirectory(directory =>
        {
            const string secret = "fictionalkey12345";
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var store = new InvestmentMarketStore(directory); var handler = new FakeHandler(_ => QuoteJson());
            using var client = new AlphaVantageClient(handler); using var market = new InvestmentMarketService(store, client, coordinator);
            var settings = new InvestmentQuoteSettings { Enabled = true, Frequency = QuoteFrequency.Manual, DailyLimit = 1 };
            market.SaveSettingsAsync(settings, secret, false).GetAwaiter().GetResult();
            market.TestAsync().GetAwaiter().GetResult(); Equal(1, handler.Calls);
            Equal(1, new InvestmentMarketStore(directory).Snapshot(DateTime.UtcNow).RequestsToday);
            market.SaveSettingsAsync(settings, "replacementkey123", false).GetAwaiter().GetResult();
            try { market.TestAsync().GetAwaiter().GetResult(); throw new Exception("Expected local budget failure"); }
            catch (MarketDataException ex) { Equal("DailyLimit", ex.Code); }
            Equal(1, handler.Calls);
            True(!File.ReadAllText(Path.Combine(directory, "market-data", "state.json")).Contains(secret));
            True(!JsonSerializer.Serialize(market.Read()).Contains(secret));
            True(!Encoding.UTF8.GetString(coordinator.Backup()).Contains(secret));
            Equal("replacementkey123", new InvestmentMarketStore(directory).Key());
            var backup = Path.Combine(directory, "restore.db"); File.WriteAllBytes(backup, coordinator.Backup());
            coordinator.Restore(coordinator.Read().Revision, backup);
            Equal(1, market.Read().RequestsToday); True(market.Read().HasKey);
        }));
        yield return ("Lookup, quotes and FX share quota and financial editor revisions stay unchanged", () => InDirectory(directory =>
        {
            var f = new Portfolio("USD"); f.Instrument.ProviderSymbol = "IBM"; f.Instrument.MappingConfirmed = true; f.Buy(1, 100);
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var original = coordinator.Update(coordinator.Read().Revision, d => { d.Owners = f.Data.Owners; d.Accounts = f.Data.Accounts; d.Instruments = f.Data.Instruments; d.InvestmentTrades = f.Data.InvestmentTrades; });
            var store = new InvestmentMarketStore(directory);
            var handler = new FakeHandler(uri => uri.Query.Contains("CURRENCY_EXCHANGE_RATE") ? FxJson() : uri.Query.Contains("SYMBOL_SEARCH") ? "{\"bestMatches\":[]}" : QuoteJson());
            using var client = new AlphaVantageClient(handler); using var market = new InvestmentMarketService(store, client, coordinator);
            market.SaveSettingsAsync(new() { Enabled = true, Frequency = QuoteFrequency.Manual }, "fictionalkey12345", false).GetAwaiter().GetResult();
            market.SearchAsync("IBM").GetAwaiter().GetResult();
            store.Update(s => s.LastAttemptUtc = DateTime.UtcNow.AddMinutes(-2));
            market.RefreshAsync().GetAwaiter().GetResult();
            Equal(3, market.Read().RequestsToday); Equal(1, market.Read().Quotes.Count); Equal(1, market.Read().Rates.Count);
            Equal(original.Revision, coordinator.Read().Revision);
            True(handler.Uris.All(u => !u.Query.Contains("Quantity") && !u.Query.Contains("100")));
            // Offline/provider errors preserve the last valid quote rather than writing zero.
            handler.Reply = _ => "{\"Information\":\"premium access required\"}";
            store.Update(s => s.LastAttemptUtc = DateTime.UtcNow.AddMinutes(-2));
            try { market.RefreshAsync().GetAwaiter().GetResult(); } catch (MarketDataException) { }
            Equal(110m, market.Read().Quotes.Single().Price);
        }));
        yield return ("AMZN baseline sends exactly the browser parameters without FX, entitlements or cache/financial changes", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Market.RefreshAsync().GetAwaiter().GetResult(); f.ClearCooldown();
            f.Market.SaveSettingsAsync(new() { Enabled = true, Paid = true, Frequency = QuoteFrequency.Manual, Freshness = QuoteFreshness.Realtime }, null, false).GetAwaiter().GetResult();
            var before = f.Market.Read(); var revision = f.Coordinator.Read().Revision;
            f.Market.TestAmznAsync().GetAwaiter().GetResult();
            var after = f.Market.Read(); var report = after.Report!; var request = report.Requests.Single();
            Equal(3, f.Handler.Calls); Equal(before.RequestsToday + 1, after.RequestsToday);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(f.Handler.Uris.Last().Query);
            Equal(3, query.Count); Equal("GLOBAL_QUOTE", query["function"].ToString()); Equal("AMZN", query["symbol"].ToString());
            Equal(MarketFixture.Secret, query["apikey"].ToString()); True(!query.ContainsKey("entitlement"));
            Equal("TestAmzn", report.Operation); Equal("AmznTestOk", report.Status); Equal("AMZN", request.Target);
            Equal(InvestmentDataState.NotApplicable, request.DataState); Equal<int?>(200, request.HttpStatusCode);
            Equal(new InvestmentRequestParameters("GLOBAL_QUOTE", "AMZN"), request.Parameters);
            Equal(before.Quotes.Single(), after.Quotes.Single()); Equal(before.Rates.Single(), after.Rates.Single());
            Equal(before.NextRefreshUtc, after.NextRefreshUtc); Equal(revision, f.Coordinator.Read().Revision);
            Equal(request, new InvestmentMarketStore(directory).Snapshot(DateTime.UtcNow).Report!.Requests.Single());
            True(!JsonSerializer.Serialize(after).Contains(MarketFixture.Secret));
        }));
        yield return ("AMZN baseline respects disabled/key/quota/provider/cooldown guards without sending a request", () =>
        {
            foreach (var code in new[] { "Disabled", "NotConfigured", "DailyLimit", "ProviderRetryWait", "DiagnosticCooldown" })
                InDirectory(directory =>
                {
                    using var f = new MarketFixture(directory);
                    f.Store.Update(s =>
                    {
                        if (code == "Disabled") s.Settings.Enabled = false;
                        if (code == "NotConfigured") s.ProtectedKey = "";
                        if (code == "DailyLimit") { s.BudgetDayUtc = DateOnly.FromDateTime(DateTime.UtcNow); s.RequestsToday = 25; }
                        if (code == "ProviderRetryWait") s.ProviderLimit = new(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(2), true);
                        if (code == "DiagnosticCooldown") s.LastAttemptUtc = DateTime.UtcNow;
                    });
                    ExpectMarketError(code, () => f.Market.TestAmznAsync().GetAwaiter().GetResult());
                    Equal(0, f.Handler.Calls); var report = f.Market.Read().Report!;
                    Equal(code, report.ErrorCode); Equal("TestAmzn", report.Operation);
                    Equal<InvestmentRequestParameters?>(null, report.Requests.Single().Parameters);
                    Equal<int?>(null, report.Requests.Single().HttpStatusCode);
                });
        });
        yield return ("AMZN baseline also works without portfolio holdings and validates the returned symbol", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var store = new InvestmentMarketStore(directory); var handler = new FakeHandler(_ => QuoteJson(symbol: "AMZN"));
            using var client = new AlphaVantageClient(handler); using var market = new InvestmentMarketService(store, client, coordinator);
            market.SaveSettingsAsync(new() { Enabled = true, Frequency = QuoteFrequency.Manual }, "fictionalkey12345", false).GetAwaiter().GetResult();
            market.TestAmznAsync().GetAwaiter().GetResult(); Equal(1, handler.Calls);
            Equal("AmznTestOk", market.Read().Report!.Status); Equal(0, market.Read().Quotes.Count); Equal(0, market.Read().Rates.Count);
            store.Update(s => s.LastAttemptUtc = DateTime.UtcNow.AddMinutes(-2)); handler.Reply = _ => QuoteJson(symbol: "IBM");
            ExpectMarketError("SymbolMismatch", () => market.TestAmznAsync().GetAwaiter().GetResult());
            Equal<int?>(200, market.Read().Report!.Requests.Single().HttpStatusCode); Equal(2, handler.Calls);
        }));
        yield return ("Refresh diagnostics record the actual quote entitlement and FX parameters, never current draft settings", () =>
        {
            foreach (var freshness in new[] { QuoteFreshness.EndOfDay, QuoteFreshness.Delayed, QuoteFreshness.Realtime })
            foreach (var us in new[] { false, true })
                InDirectory(directory =>
                {
                    using var f = new MarketFixture(directory);
                    f.Coordinator.Update(f.Coordinator.Read().Revision, d => d.Instruments.Single().IsUsListing = us);
                    f.Market.SaveSettingsAsync(new() { Enabled = true, Paid = true, Frequency = QuoteFrequency.Manual, Freshness = freshness }, null, false).GetAwaiter().GetResult();
                    f.Market.RefreshAsync().GetAwaiter().GetResult();
                    var report = f.Market.Read().Report!;
                    var expected = !us || freshness == QuoteFreshness.EndOfDay ? null : freshness == QuoteFreshness.Delayed ? "delayed" : "realtime";
                    var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(f.Handler.Uris[1].Query);
                    Equal(expected, query.TryGetValue("entitlement", out var value) ? value.ToString() : null);
                    Equal(new InvestmentRequestParameters("GLOBAL_QUOTE", "AMZN", Entitlement: expected), report.Requests[1].Parameters);
                    Equal(new InvestmentRequestParameters("CURRENCY_EXCHANGE_RATE", FromCurrency: "USD", ToCurrency: "EUR"), report.Requests[0].Parameters);
                    True(report.Requests.All(r => r.HttpStatusCode == 200)); Equal(2, f.Handler.Calls);
                    // A later settings change must not rewrite what was actually sent in this report.
                    f.Market.SaveSettingsAsync(new() { Enabled = true, Frequency = QuoteFrequency.Manual }, null, false).GetAwaiter().GetResult();
                    Equal(report.Requests[1].Parameters, f.Market.Read().Report!.Requests[1].Parameters);
                });
        });
        yield return ("Search diagnostics retain only the function and never the search term or key", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Handler.Reply = _ => "{\"bestMatches\":[]}";
            f.Market.SearchAsync("private-search-term").GetAwaiter().GetResult();
            Equal(new InvestmentRequestParameters("SYMBOL_SEARCH"), f.Market.Read().Report!.Requests.Single().Parameters);
            var json = File.ReadAllText(Path.Combine(directory, "market-data", "state.json"));
            True(!json.Contains("private-search-term")); True(!json.Contains(MarketFixture.Secret)); True(!json.Contains("apikey="));
        }));
        yield return ("An unspecified provider limit suggests a wait without locking manual rechecks or retrying automatically", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var store = new InvestmentMarketStore(directory); var handler = new FakeHandler(_ => "{\"Information\":\"25 requests per day\"}");
            using var client = new AlphaVantageClient(handler); using var market = new InvestmentMarketService(store, client, coordinator);
            market.SaveSettingsAsync(new() { Enabled = true }, "fictionalkey12345", false).GetAwaiter().GetResult();
            ExpectMarketError("ProviderLimit", () => market.TestAsync().GetAwaiter().GetResult());
            var limit = market.Read().ProviderLimit!;
            Equal(1, handler.Calls); Equal(1, market.Read().RequestsToday);
            True(!limit.ProviderSuppliedRetryTime); Equal(TimeSpan.FromMinutes(1), limit.RetryUtc - limit.ReportedUtc);
            Equal(limit, new InvestmentMarketStore(directory).Snapshot(DateTime.UtcNow).ProviderLimit);
            store.Update(s => s.NextRefreshUtc = DateTime.UtcNow.AddMinutes(-1));
            market.RefreshAsync(scheduled: true).GetAwaiter().GetResult();
            Equal(1, handler.Calls); Equal("Test", market.Read().Report!.Operation);
            handler.Reply = _ => QuoteJson();
            market.TestAsync().GetAwaiter().GetResult();
            Equal(2, handler.Calls); Equal(2, market.Read().RequestsToday);
            Equal<InvestmentProviderLimit?>(null, market.Read().ProviderLimit); Equal("ConnectionOk", market.Read().Status);
        }));
        yield return ("Explicit Retry-After seconds and dates persist, distinguish local waits and expire without extending", () =>
        {
            foreach (var status in new[] { HttpStatusCode.OK, HttpStatusCode.TooManyRequests })
            foreach (var header in new[] { "120", DateTime.UtcNow.AddMinutes(3).ToString("R", CultureInfo.InvariantCulture) })
                InDirectory(directory =>
                {
                    using var f = new MarketFixture(directory);
                    f.Handler.StatusCode = status; f.Handler.RetryAfter = header;
                    f.Handler.Reply = _ => "{\"Note\":\"API call frequency exceeded\"}";
                    ExpectMarketError("ProviderLimit", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
                    var limit = f.Market.Read().ProviderLimit!;
                    True(limit.ProviderSuppliedRetryTime); True(limit.RetryUtc > DateTime.UtcNow.AddMinutes(1));
                    Equal(limit, new InvestmentMarketStore(directory).Snapshot(DateTime.UtcNow).ProviderLimit);
                    Equal<int?>((int)status, f.Market.Read().Report!.Requests[0].HttpStatusCode);
                    f.ClearCooldown();
                    ExpectMarketError("ProviderRetryWait", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
                    var report = f.Market.Read().Report!;
                    Equal("ProviderRetryWait", report.ErrorCode); Equal("ProviderRetryWait", report.Requests[0].Status);
                    Equal<int?>(null, report.Requests[0].HttpStatusCode); Equal("NotRequested", report.Requests[1].Status);
                    Equal(1, f.Handler.Calls); Equal(1, f.Market.Read().RequestsToday); Equal(limit, f.Market.Read().ProviderLimit);
                    f.Store.Update(s => s.ProviderLimit = limit with { RetryUtc = DateTime.UtcNow.AddSeconds(-1) });
                    f.Handler.StatusCode = HttpStatusCode.OK; f.Handler.RetryAfter = null;
                    f.Handler.Reply = uri => uri.Query.Contains("CURRENCY_EXCHANGE_RATE") ? FxJson() : QuoteJson(symbol: "AMZN");
                    f.Market.RefreshAsync().GetAwaiter().GetResult();
                    Equal(3, f.Handler.Calls); Equal(3, f.Market.Read().RequestsToday);
                    Equal<InvestmentProviderLimit?>(null, f.Market.Read().ProviderLimit); Equal("Updated", f.Market.Read().Status);
                });
        });
        yield return ("Missing and invalid Retry-After headers do not create an enforced provider pause", () =>
        {
            foreach (var header in new string?[] { null, "not-a-date-" + MarketFixture.Secret, "-1" })
                InDirectory(directory =>
                {
                    using var f = new MarketFixture(directory);
                    f.Handler.StatusCode = HttpStatusCode.TooManyRequests; f.Handler.RetryAfter = header;
                    ExpectMarketError("ProviderLimit", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
                    True(!f.Market.Read().ProviderLimit!.ProviderSuppliedRetryTime); Equal(1, f.Handler.Calls);
                    True(!JsonSerializer.Serialize(f.Market.Read()).Contains(MarketFixture.Secret));
                    f.ClearCooldown(); f.Handler.StatusCode = HttpStatusCode.OK; f.Handler.RetryAfter = null;
                    f.Market.RefreshAsync().GetAwaiter().GetResult();
                    Equal(3, f.Handler.Calls); Equal("Updated", f.Market.Read().Status);
                });
        });
        yield return ("Zero and past Retry-After times permit a new attempt without an invented extra wait", () =>
        {
            foreach (var header in new[] { "0", DateTime.UtcNow.AddMinutes(-1).ToString("R", CultureInfo.InvariantCulture) })
                InDirectory(directory =>
                {
                    using var f = new MarketFixture(directory);
                    f.Handler.StatusCode = HttpStatusCode.TooManyRequests; f.Handler.RetryAfter = header;
                    ExpectMarketError("ProviderLimit", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
                    var limit = f.Market.Read().ProviderLimit!;
                    True(limit.ProviderSuppliedRetryTime); True(limit.RetryUtc <= DateTime.UtcNow);
                    f.ClearCooldown(); f.Handler.StatusCode = HttpStatusCode.OK; f.Handler.RetryAfter = null;
                    f.Market.RefreshAsync().GetAwaiter().GetResult();
                    Equal(3, f.Handler.Calls); Equal("Updated", f.Market.Read().Status);
                });
        });
        yield return ("A provider limit preserves cached prices and FX and a successful manual retry clears its notice", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Market.RefreshAsync().GetAwaiter().GetResult(); var before = f.Market.Read(); f.ClearCooldown();
            f.Handler.StatusCode = HttpStatusCode.TooManyRequests;
            ExpectMarketError("ProviderLimit", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
            var limited = f.Market.Read();
            Equal(before.Quotes.Single(), limited.Quotes.Single()); Equal(before.Rates.Single(), limited.Rates.Single());
            Equal(before.LastSuccessUtc, limited.LastSuccessUtc); Equal(3, f.Handler.Calls);
            True(limited.Report!.Requests.All(r => r.DataState == InvestmentDataState.Retained));
            f.ClearCooldown(); f.Handler.StatusCode = HttpStatusCode.OK;
            f.Market.RefreshAsync().GetAwaiter().GetResult();
            Equal(5, f.Handler.Calls); Equal(5, f.Market.Read().RequestsToday);
            Equal<InvestmentProviderLimit?>(null, f.Market.Read().ProviderLimit); Equal("Updated", f.Market.Read().Status);
        }));
        yield return ("Upgrade ignores a legacy 24-hour block while retaining the key, request budget and cached rates", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Market.RefreshAsync().GetAwaiter().GetResult(); f.ClearCooldown();
            var before = f.Market.Read();
            var path = Path.Combine(directory, "market-data", "state.json");
            var legacy = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            legacy.Remove("ProviderLimit"); legacy["BlockedUntilUtc"] = DateTime.UtcNow.AddHours(24); legacy["Status"] = "ProviderLimit";
            File.WriteAllText(path, legacy.ToJsonString());
            var upgraded = new InvestmentMarketStore(directory); var after = upgraded.Snapshot(DateTime.UtcNow);
            Equal(before.RequestsToday, after.RequestsToday); Equal(MarketFixture.Secret, upgraded.Key());
            Equal(before.Quotes.Single(), after.Quotes.Single()); Equal(before.Rates.Single(), after.Rates.Single());
            True(!after.ProviderLimit!.ProviderSuppliedRetryTime);
            using var client = new AlphaVantageClient(new FakeHandler(uri => uri.Query.Contains("CURRENCY_EXCHANGE_RATE") ? FxJson() : QuoteJson(symbol: "AMZN")));
            using var market = new InvestmentMarketService(upgraded, client, f.Coordinator);
            market.RefreshAsync().GetAwaiter().GetResult();
            Equal(before.RequestsToday + 2, market.Read().RequestsToday); Equal("Updated", market.Read().Status);
            True(!File.ReadAllText(path).Contains("BlockedUntilUtc"));
        }));
        yield return ("Disabled integration makes no outbound requests", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var store = new InvestmentMarketStore(directory); var handler = new FakeHandler(_ => QuoteJson());
            using var client = new AlphaVantageClient(handler); using var market = new InvestmentMarketService(store, client, coordinator);
            market.SaveSettingsAsync(new(), "fictionalkey12345", false).GetAwaiter().GetResult();
            try { market.TestAsync().GetAwaiter().GetResult(); } catch (MarketDataException ex) { Equal("Disabled", ex.Code); }
            market.RefreshAsync(scheduled: true).GetAwaiter().GetResult(); Equal(0, handler.Calls);
        }));
        yield return ("Same listing across accounts shares one quote and one FX request", () => InDirectory(directory =>
        {
            var f = new Portfolio("USD"); f.Instrument.ProviderSymbol = "IBM"; f.Instrument.MappingConfirmed = true; f.Buy(1, 100);
            var account2 = f.Account with { Id = Guid.NewGuid(), Name = "Second broker" }; f.Data.Accounts.Add(account2);
            var instrument2 = f.Instrument with { Id = Guid.NewGuid() }; f.Data.Instruments.Add(instrument2);
            f.Data.InvestmentTrades.Add(f.Data.InvestmentTrades[0] with { Id = Guid.NewGuid(), AccountId = account2.Id, InstrumentId = instrument2.Id });
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            coordinator.Update(coordinator.Read().Revision, d => { d.Owners = f.Data.Owners; d.Accounts = f.Data.Accounts; d.Instruments = f.Data.Instruments; d.InvestmentTrades = f.Data.InvestmentTrades; });
            var store = new InvestmentMarketStore(directory); var handler = new FakeHandler(u => u.Query.Contains("CURRENCY_EXCHANGE_RATE") ? FxJson() : QuoteJson());
            using var client = new AlphaVantageClient(handler); using var market = new InvestmentMarketService(store, client, coordinator);
            market.SaveSettingsAsync(new() { Enabled = true }, "fictionalkey12345", false).GetAwaiter().GetResult();
            market.RefreshAsync().GetAwaiter().GetResult();
            Equal(2, handler.Calls); Equal(2, market.Read().Quotes.Count); Equal(1, market.Read().Rates.Count);
        }));
        yield return ("Refresh reports successful AMZN and FX requests and persists safe diagnostics", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Market.RefreshAsync().GetAwaiter().GetResult();
            var snapshot = f.Market.Read(); var report = snapshot.Report!;
            Equal("Refresh", report.Operation); Equal("Updated", report.Status); True(report.CompletedUtc >= report.StartedUtc);
            Equal(2, report.Requests.Count); Equal(2, f.Handler.Calls); Equal(2, snapshot.RequestsToday);
            True(report.Requests.All(r => r.Status == "Updated" && r.DataState == InvestmentDataState.Updated));
            Equal("USD→EUR", report.Requests[0].Target); Equal("AMZN", report.Requests[1].Target);
            Equal<DateOnly?>(Portfolio.Today, report.Requests[1].PriceDate);
            Equal("Updated", new InvestmentMarketStore(directory).Snapshot(DateTime.UtcNow).Report!.Status);
            True(!JsonSerializer.Serialize(snapshot).Contains(MarketFixture.Secret));
        }));
        yield return ("Unsupported FX access is reported separately and does not block a native AMZN quote", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Handler.Reply = uri => uri.Query.Contains("CURRENCY_EXCHANGE_RATE")
                ? "{\"Information\":\"premium access required; " + MarketFixture.Secret + " https://private.invalid/?apikey=" + MarketFixture.Secret + "\"}"
                : QuoteJson(symbol: "AMZN");
            f.Market.RefreshAsync().GetAwaiter().GetResult();
            var snapshot = f.Market.Read(); var report = snapshot.Report!;
            Equal("PartialUpdate", report.Status); Equal("PartialUpdate", snapshot.Status); Equal(2, f.Handler.Calls);
            Equal("EntitlementRequired", report.Requests[0].Status); Equal<int?>(200, report.Requests[0].HttpStatusCode);
            Equal(InvestmentDataState.Missing, report.Requests[0].DataState);
            Equal("Updated", report.Requests[1].Status); Equal(110m, snapshot.Quotes.Single().Price); Equal(0, snapshot.Rates.Count);
            Equal<DateTime?>(null, snapshot.LastSuccessUtc);
            var json = JsonSerializer.Serialize(snapshot);
            True(!json.Contains(MarketFixture.Secret)); True(!json.Contains("private.invalid")); True(!json.Contains("premium access required"));
        }));
        yield return ("Failed refresh retains dated cached quotes and FX and does not claim a partial success", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Market.RefreshAsync().GetAwaiter().GetResult(); var before = f.Market.Read();
            f.ClearCooldown();
            f.Handler.Reply = uri => uri.Query.Contains("CURRENCY_EXCHANGE_RATE") ? "{}" : "{\"Global Quote\":{}}";
            f.Market.RefreshAsync().GetAwaiter().GetResult();
            var after = f.Market.Read(); var report = after.Report!;
            Equal("UpdateFailed", report.Status); Equal("UpdateFailed", after.Status); Equal(before.LastSuccessUtc, after.LastSuccessUtc);
            Equal(before.Quotes.Single(), after.Quotes.Single()); Equal(before.Rates.Single(), after.Rates.Single());
            True(report.Requests.All(r => r.DataState == InvestmentDataState.Retained));
            Equal("InvalidResponse", report.Requests[0].Status); Equal("EmptyQuote", report.Requests[1].Status);
            Equal(before.Quotes.Single().PriceDate, report.Requests[1].PriceDate!.Value);
            Equal(before.Rates.Single().RetrievedUtc, report.Requests[0].DataUtc!.Value);
        }));
        yield return ("Provider limits identify the failed endpoint and the unrequested quote without retrying", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Handler.Reply = _ => "{\"Note\":\"25 requests per day; " + MarketFixture.Secret + "\"}";
            ExpectMarketError("ProviderLimit", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
            var snapshot = f.Market.Read(); var report = snapshot.Report!;
            Equal(1, f.Handler.Calls); Equal(1, snapshot.RequestsToday); Equal("ProviderLimit", report.ErrorCode);
            Equal("UpdateFailed", report.Status); True(report.CompletedUtc != null);
            Equal("ProviderLimit", report.Requests[0].Status); Equal<int?>(200, report.Requests[0].HttpStatusCode);
            Equal("NotRequested", report.Requests[1].Status); Equal(InvestmentDataState.Missing, report.Requests[1].DataState);
            True(f.Market.Read().ProviderLimit is { ProviderSuppliedRetryTime: false });
        }));
        yield return ("A local budget failure is visible without inventing an HTTP response or sending a request", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Store.Update(s => { s.Settings.DailyLimit = 1; s.BudgetDayUtc = DateOnly.FromDateTime(DateTime.UtcNow); s.RequestsToday = 1; });
            ExpectMarketError("DailyLimit", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
            var report = f.Market.Read().Report!;
            Equal(0, f.Handler.Calls); Equal("DailyLimit", report.ErrorCode);
            Equal("DailyLimit", report.Requests[0].Status); Equal<int?>(null, report.Requests[0].HttpStatusCode);
            Equal("NotRequested", report.Requests[1].Status);
            var now = DateTime.UtcNow;
            Equal<DateTime?>(now.Date.AddDays(1), f.Store.Snapshot(now).DailyBudgetResetUtc);
            var tomorrow = f.Store.Snapshot(now.Date.AddDays(1));
            Equal(0, tomorrow.RequestsToday); Equal<DateTime?>(null, tomorrow.DailyBudgetResetUtc);
            Equal<InvestmentProviderLimit?>(null, tomorrow.ProviderLimit);
        }));
        yield return ("HTTP, timeout, transport and malformed responses produce safe actionable endpoint diagnostics", () =>
        {
            foreach (var code in new[] { "HttpError", "Timeout", "ConnectionFailed", "InvalidResponse", "EmptyQuote", "SymbolMismatch" })
                InDirectory(directory =>
                {
                    using var f = new MarketFixture(directory, "EUR");
                    f.Handler.StatusCode = code == "HttpError" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
                    f.Handler.Reply = _ => code switch
                    {
                        "Timeout" => throw new TaskCanceledException(MarketFixture.Secret),
                        "ConnectionFailed" => throw new HttpRequestException("https://private.invalid/?apikey=" + MarketFixture.Secret),
                        "InvalidResponse" => "not JSON " + MarketFixture.Secret,
                        "EmptyQuote" => "{\"Global Quote\":{}}",
                        "SymbolMismatch" => QuoteJson(symbol: "OTHER"),
                        _ => MarketFixture.Secret
                    };
                    try { f.Market.RefreshAsync().GetAwaiter().GetResult(); }
                    catch (MarketDataException ex) { Equal(code, ex.Code); True(!ex.ToString().Contains(MarketFixture.Secret)); }
                    var report = f.Market.Read().Report!; var request = report.Requests.Single();
                    Equal("UpdateFailed", report.Status); Equal(code, request.Status); Equal("AMZN", request.Target);
                    Equal(InvestmentDataState.Missing, request.DataState); Equal(1, f.Handler.Calls);
                    if (code == "HttpError") Equal<int?>(503, request.HttpStatusCode);
                    if (code == "InvalidResponse") Equal<int?>(200, request.HttpStatusCode);
                    True(!JsonSerializer.Serialize(report).Contains(MarketFixture.Secret));
                    True(!File.ReadAllText(Path.Combine(directory, "market-data", "state.json")).Contains(MarketFixture.Secret));
                });
        });
        yield return ("Older provider data is diagnosed and never overwrites newer stored prices", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory, "EUR");
            f.Market.RefreshAsync().GetAwaiter().GetResult();
            f.Store.Update(s => s.Quotes[0] = s.Quotes[0] with { PriceDate = Portfolio.Today.AddDays(1), Price = 120 });
            f.ClearCooldown(); f.Market.RefreshAsync().GetAwaiter().GetResult();
            var snapshot = f.Market.Read(); var result = snapshot.Report!.Requests.Single();
            Equal("OlderData", result.Status); Equal(InvestmentDataState.Retained, result.DataState); Equal(120m, snapshot.Quotes.Single().Price);
        }));
        yield return ("Cooldown after a connection test explains why every refresh request was skipped", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Handler.Reply = _ => QuoteJson();
            f.Market.TestAsync().GetAwaiter().GetResult();
            Equal("Test", f.Market.Read().Report!.Operation); Equal("ConnectionOk", f.Market.Read().Report!.Status);
            ExpectMarketError("RefreshCooldown", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
            var report = f.Market.Read().Report!;
            Equal("RefreshCooldown", report.ErrorCode); True(report.Requests.All(r => r.Status == "NotRequested")); Equal(1, f.Handler.Calls);
        }));
        yield return ("Connection-test and search failures have safe reports without persisting search terms", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Handler.Reply = _ => "{\"Global Quote\":{}}";
            ExpectMarketError("EmptyQuote", () => f.Market.TestAsync().GetAwaiter().GetResult());
            Equal("Test", f.Market.Read().Report!.Operation); Equal("IBM", f.Market.Read().Report!.Requests.Single().Target);
            f.Handler.Reply = _ => "{\"Information\":\"invalid key " + MarketFixture.Secret + "\"}";
            ExpectMarketError("KeyOrAccessInvalid", () => f.Market.SearchAsync("private-search-term").GetAwaiter().GetResult());
            var report = f.Market.Read().Report!;
            Equal("Search", report.Operation); Equal("", report.Requests.Single().Target);
            True(!JsonSerializer.Serialize(report).Contains("private-search-term"));
        }));
        yield return ("Existing market state without diagnostics remains compatible and interrupted reports finish safely", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            var path = Path.Combine(directory, "market-data", "state.json");
            var legacy = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); legacy.Remove("Report"); File.WriteAllText(path, legacy.ToJsonString());
            var upgraded = new InvestmentMarketStore(directory);
            True(upgraded.Key() == MarketFixture.Secret); Equal<InvestmentMarketReport?>(null, upgraded.Snapshot(DateTime.UtcNow).Report);
            upgraded.Update(s => s.Report = new("Refresh", DateTime.UtcNow, null, "Requesting",
                [new("Quote", "AMZN", "Requesting", InvestmentDataState.Missing)]));
            var restarted = new InvestmentMarketStore(directory).Snapshot(DateTime.UtcNow).Report!;
            Equal("Interrupted", restarted.Status); True(restarted.CompletedUtc != null); Equal("NotRequested", restarted.Requests.Single().Status);
            upgraded.Update(s => s.Report = new("Refresh", DateTime.UtcNow, DateTime.UtcNow, "UpdateFailed",
                [new("Quote", "AMZN", "EntitlementRequired", InvestmentDataState.Missing, HttpStatusCode: 200)]));
            var oldReport = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var oldRequest = oldReport["Report"]!["Requests"]![0]!.AsObject(); oldRequest.Remove("Parameters"); oldRequest.Remove("ProviderReason");
            File.WriteAllText(path, oldReport.ToJsonString());
            var preserved = new InvestmentMarketStore(directory).Snapshot(DateTime.UtcNow).Report!.Requests.Single();
            Equal("EntitlementRequired", preserved.Status); Equal<int?>(200, preserved.HttpStatusCode);
            Equal<InvestmentRequestParameters?>(null, preserved.Parameters); Equal<InvestmentProviderReason?>(null, preserved.ProviderReason);
        }));
        yield return ("Refreshing without open holdings explicitly reports no symbols without spending quota", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Coordinator.Update(f.Coordinator.Read().Revision, d => d.InvestmentTrades.Clear());
            f.Market.RefreshAsync().GetAwaiter().GetResult();
            Equal("NoSymbols", f.Market.Read().Report!.Status); Equal(0, f.Handler.Calls);
        }));
        yield return ("Progress identifies the current request and busy actions cannot overwrite the running report", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            var observed = 0;
            f.Handler.Reply = uri =>
            {
                var report = f.Market.Read().Report!;
                Equal("Requesting", report.Status); Equal<DateTime?>(null, report.CompletedUtc);
                if (observed++ == 0)
                {
                    Equal("Requesting", report.Requests[0].Status); Equal("Pending", report.Requests[1].Status);
                    ExpectMarketError("Busy", () => f.Market.TestAsync().GetAwaiter().GetResult());
                    ExpectMarketError("Busy", () => f.Market.TestAmznAsync().GetAwaiter().GetResult());
                    Equal("Refresh", f.Market.Read().Report!.Operation);
                }
                else { Equal("Updated", report.Requests[0].Status); Equal("Requesting", report.Requests[1].Status); }
                return uri.Query.Contains("CURRENCY_EXCHANGE_RATE") ? FxJson() : QuoteJson(symbol: "AMZN");
            };
            f.Market.RefreshAsync().GetAwaiter().GetResult();
            Equal(2, observed); Equal("Updated", f.Market.Read().Report!.Status);
        }));
        yield return ("Cancellation completes diagnostics and leaves remaining requests unrequested", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory); using var stop = new CancellationTokenSource();
            f.Handler.Reply = _ => { stop.Cancel(); throw new OperationCanceledException(stop.Token); };
            try { f.Market.RefreshAsync(cancellationToken: stop.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
            catch (OperationCanceledException) { }
            var snapshot = f.Market.Read(); var report = snapshot.Report!;
            Equal("Interrupted", snapshot.Status); Equal("Interrupted", report.ErrorCode); True(report.CompletedUtc != null);
            True(report.Requests.All(r => r.Status == "NotRequested")); Equal(1, f.Handler.Calls);
        }));
        yield return ("A budget exhausted after FX success reports a partial update and the exact stopped quote", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Store.Update(s => s.Settings.DailyLimit = 1);
            ExpectMarketError("DailyLimit", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
            var report = f.Market.Read().Report!;
            Equal("PartialUpdate", report.Status); Equal("DailyLimit", report.ErrorCode);
            Equal("Updated", report.Requests[0].Status); Equal("DailyLimit", report.Requests[1].Status); Equal(1, f.Handler.Calls);
        }));
        yield return ("HTTP 429 without a retry time records the status without guessing a day-long block", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory);
            f.Handler.StatusCode = HttpStatusCode.TooManyRequests;
            ExpectMarketError("ProviderLimit", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
            Equal<int?>(429, f.Market.Read().Report!.Requests[0].HttpStatusCode);
            Equal(1, f.Handler.Calls); True(f.Market.Read().ProviderLimit is { ProviderSuppliedRetryTime: false });
        }));
        yield return ("Provider explanations are classified without mistaking subscription advice for a limit or exposing raw text", () =>
        {
            foreach (var (text, expected) in new[]
            {
                ("This is a premium endpoint. Check your request frequency.", "EntitlementRequired"),
                ("Our standard API rate limit is 25 requests per day. Subscribe to premium.", "ProviderLimit"),
                ("API call frequency exceeded. Subscribe for premium access.", "ProviderLimit"),
                ("Too many requests. Upgrade to a premium endpoint.", "ProviderLimit"),
                ("The API key is invalid.", "KeyOrAccessInvalid"),
                ("The API key is invalid. Subscribe for premium access.", "KeyOrAccessInvalid"),
                ("Upgrade to premium for more data.", "ProviderRejected"),
                ("Premium access and entitlement options are available on our website.", "ProviderRejected"),
                ("Upgrade to access premium-only features.", "ProviderRejected"),
                ("Premium access is required for this request.", "EntitlementRequired"),
                ("Your key does not have the required entitlement.", "EntitlementRequired"),
                ("Unrecognized provider notice.", "ProviderRejected")
            })
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { Information = text + " " + MarketFixture.Secret }));
                ExpectMarketError(expected, () => AlphaVantageClient.Check(json.RootElement));
            }
        });
        yield return ("Safe provider classification reasons survive HTTP wrapping and persistence without raw notices", () =>
        {
            foreach (var (notice, code, reason) in new[]
            {
                ("The API key is invalid. Upgrade to premium.", "KeyOrAccessInvalid", InvestmentProviderReason.InvalidApiKey),
                ("25 requests per day. Consider premium access.", "ProviderLimit", InvestmentProviderReason.RequestLimit),
                ("This is a premium endpoint.", "EntitlementRequired", InvestmentProviderReason.PremiumEndpoint),
                ("Premium access required.", "EntitlementRequired", InvestmentProviderReason.MissingEntitlement),
                ("Consider premium access.", "ProviderRejected", InvestmentProviderReason.UnrecognizedNotice)
            })
            foreach (var field in new[] { "Note", "Information" })
                InDirectory(directory =>
                {
                    using var f = new MarketFixture(directory, "EUR");
                    f.Handler.Reply = _ => JsonSerializer.Serialize(new Dictionary<string, string> { [field] = notice + " " + MarketFixture.Secret + " https://private.invalid/?apikey=" + MarketFixture.Secret });
                    try { f.Market.RefreshAsync().GetAwaiter().GetResult(); } catch (MarketDataException ex) { Equal(code, ex.Code); }
                    var request = f.Market.Read().Report!.Requests.Single();
                    Equal(code, request.Status); Equal<InvestmentProviderReason?>(reason, request.ProviderReason); Equal<int?>(200, request.HttpStatusCode);
                    Equal(new InvestmentRequestParameters("GLOBAL_QUOTE", "AMZN"), request.Parameters);
                    Equal(request, new InvestmentMarketStore(directory).Snapshot(DateTime.UtcNow).Report!.Requests.Single());
                    var stored = File.ReadAllText(Path.Combine(directory, "market-data", "state.json"));
                    True(!stored.Contains(MarketFixture.Secret)); True(!stored.Contains(notice)); True(!stored.Contains("private.invalid"));
                    Equal(1, f.Handler.Calls);
                });
        });
        yield return ("Concrete errors in either provider notice field take precedence over generic premium advice", () =>
        {
            foreach (var field in new[] { "Note", "Information" })
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string>
                { [field] = "Upgrade to premium.", [field == "Note" ? "Information" : "Note"] = "The API key is invalid." }));
                ExpectMarketError("KeyOrAccessInvalid", () => AlphaVantageClient.Check(json.RootElement));
            }
        });
        yield return ("Invalid quote data retains its actual successful HTTP status instead of guessing HTTP 200", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory, "EUR");
            f.Handler.StatusCode = HttpStatusCode.Created; f.Handler.Reply = _ => "{\"Global Quote\":{}}";
            f.Market.RefreshAsync().GetAwaiter().GetResult();
            var result = f.Market.Read().Report!.Requests.Single();
            Equal("EmptyQuote", result.Status); Equal<int?>(201, result.HttpStatusCode);
        }));
        yield return ("The response body receives a deadline token and a stalled read is diagnosed as a timeout", () => InDirectory(directory =>
        {
            using var f = new MarketFixture(directory, "EUR");
            using var stream = new TimeoutReadStream();
            f.Handler.Content = new StreamContent(stream);
            ExpectMarketError("Timeout", () => f.Market.RefreshAsync().GetAwaiter().GetResult());
            True(stream.CancellableRead); Equal("Timeout", f.Market.Read().Report!.Requests.Single().Status); Equal(1, f.Handler.Calls);
        }));
        yield return ("Market cache corruption fails closed without blocking household records", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            _ = new InvestmentMarketStore(directory);
            var path = Path.Combine(directory, "market-data", "state.json"); File.WriteAllText(path, "{broken");
            var damaged = new InvestmentMarketStore(directory); var snapshot = damaged.Snapshot(DateTime.UtcNow);
            Equal("ConfigurationInvalid", snapshot.Status); True(!snapshot.Settings.Enabled); Equal(25, snapshot.RequestsToday);
            True(snapshot.DailyBudgetResetUtc > DateTime.UtcNow); Equal<InvestmentProviderLimit?>(null, snapshot.ProviderLimit);
            damaged.Update(s => s.Status = "Disabled");
            Equal("{broken", File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(path)!, "state.invalid-*.json").Single()));
            True(coordinator.Read().Data != null);
        }));
        yield return ("Authenticated investment adapter rejects revoked and unauthenticated sessions", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var store = new InvestmentMarketStore(directory); using var client = new AlphaVantageClient(new FakeHandler(_ => QuoteJson()));
            using var market = new InvestmentMarketService(store, client, coordinator);
            var sessions = new WebsiteSessions(new() { Enabled = true }, new() { DataDirectory = directory, EditorToken = "test-editor", ReaderToken = "test-reader" });
            var authentication = new TestAuthentication();
            var adapter = new InvestmentAdministration(market, authentication, sessions);
            try { adapter.ReadAsync().GetAwaiter().GetResult(); throw new Exception("Expected authentication failure"); } catch (UnauthorizedAccessException) { }
            try { adapter.TestAmznAsync().GetAwaiter().GetResult(); throw new Exception("Expected authentication failure"); } catch (UnauthorizedAccessException) { }
            authentication.Principal = sessions.Create(false);
            True(!adapter.ReadAsync().GetAwaiter().GetResult().HasKey);
            sessions.Revoke(authentication.Principal);
            try { adapter.TestAmznAsync().GetAwaiter().GetResult(); throw new Exception("Expected revoked session failure"); } catch (UnauthorizedAccessException) { }
            try { adapter.SaveSettingsAsync(new(), "fictionalkey12345").GetAwaiter().GetResult(); throw new Exception("Expected revoked session failure"); } catch (UnauthorizedAccessException) { }
            True(!market.Read().HasKey);
        }));
        yield return ("Investment cards and grouped settings render in both languages for every schedule without secrets", () => InDirectory(directory =>
        {
            RenderPages(directory).GetAwaiter().GetResult();
        }));
    }

    private static async Task RenderPages(string directory)
    {
        var config = LocalPreviewBootstrap.Prepare(directory);
        await using var app = ServiceApplication.Build(["--config", config, "--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning"], localPreview: true);
        var coordinator = app.Services.GetRequiredService<AdministrationCoordinator>();
        var f = new Portfolio(); f.Buy(10, 100); f.Buy(5, 120); f.Instrument.ManualPrice = 110; f.Instrument.ManualPreviousClose = 100;
        coordinator.Update(coordinator.Read().Revision, d =>
        { d.Owners.Add(f.Owner); d.Accounts.Add(f.Account); d.Instruments.Add(f.Instrument); d.InvestmentTrades.AddRange(f.Data.InvestmentTrades); });
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new(address) };
            var market = app.Services.GetRequiredService<InvestmentMarketService>();
            foreach (var language in new[] { "nl-NL", "en-GB" })
            foreach (var frequency in Enum.GetValues<QuoteFrequency>())
            {
                await market.SaveSettingsAsync(new()
                {
                    Frequency = frequency, Paid = frequency == QuoteFrequency.Realtime,
                    Freshness = frequency == QuoteFrequency.Realtime ? QuoteFreshness.Realtime : QuoteFreshness.EndOfDay
                }, null, removeKey: false);
                using var request = new HttpRequestMessage(HttpMethod.Get, "/beleggingen");
                request.Headers.Add("Cookie", ".AspNetCore.Culture=" + Uri.EscapeDataString("c=" + language + "|uic=" + language));
                using var response = await client.SendAsync(request);
                var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
                True(response.IsSuccessStatusCode);
                True(html.Contains("Fictional stock"));
                True(html.Contains(language == "nl-NL" ? "+3,13%" : "+3.13%"));
                True(html.Contains(language == "nl-NL" ? "sinds vorige handelsafsluiting" : "since previous trading close"));
                True(!html.Contains("<canvas")); True(!html.Contains("InvestmentSinceBuys"));
                True(!html.Contains("ProtectedKey")); True(!html.Contains("fictionalkey12345"));
                True(!html.Contains("marketActionError"));
                True(html.Contains(language == "nl-NL" ? "AMZN-basistest (1 aanvraag)" : "AMZN baseline test (1 request)"));
                True(html.Contains("class=\"investment-settings-grid\""));
                True(html.Contains("<legend>" + (language == "nl-NL" ? "Verbinding" : "Connection") + "</legend>"));
                True(html.Contains("<legend>" + (language == "nl-NL" ? "Updateschema" : "Update schedule") + "</legend>"));
                True(html.Contains("<legend>" + (language == "nl-NL" ? "API-limieten" : "API limits") + "</legend>"));
                True(html.Contains("aria-describedby=\"investment-key-help\""));
                True(html.Contains("id=\"investment-key-help\""));
                True(html.Contains(language == "nl-NL" ? "Laat leeg om de opgeslagen sleutel te behouden." : "Leave blank to keep the stored key."));
                True(html.Contains("aria-describedby=\"investment-budget-help\""));
                True(html.Contains("id=\"investment-budget-help\""));
                Equal(frequency == QuoteFrequency.Daily, html.Contains("id=\"investment-daily-hour\""));
                Equal(frequency == QuoteFrequency.SeveralPerDay, html.Contains("id=\"investment-updates-per-day\""));
                Equal(frequency == QuoteFrequency.Realtime, html.Contains("id=\"investment-poll-seconds\""));
                Equal(frequency == QuoteFrequency.Daily, html.Contains("id=\"investment-hour-help\""));
                Equal(frequency == QuoteFrequency.Realtime, html.Contains("id=\"investment-poll-help\""));
            }
            var home = await client.GetStringAsync("/");
            True(home.Contains("investment-holding")); True(home.Contains("Fictional stock"));
            Equal(0, market.Read().RequestsToday);
            var store = app.Services.GetRequiredService<InvestmentMarketStore>();
            foreach (var language in new[] { "nl-NL", "en-GB" })
            foreach (var status in new[] { "PartialUpdate", "UpdateFailed", "Updated", "Requesting", "Interrupted", "NoSymbols", "ConnectionOk", "SearchOk" })
            {
                store.Update(s => s.Report = new("Refresh", DateTime.UtcNow, status == "Requesting" ? null : DateTime.UtcNow, status,
                    [new("Fx", "USD→EUR", "EntitlementRequired", InvestmentDataState.Missing, HttpStatusCode: 200,
                        Parameters: new("CURRENCY_EXCHANGE_RATE", FromCurrency: "USD", ToCurrency: "EUR"), ProviderReason: InvestmentProviderReason.MissingEntitlement),
                     new("Quote", "AMZN", "Updated", InvestmentDataState.Updated, PriceDate: Portfolio.Today, Parameters: new("GLOBAL_QUOTE", "AMZN")),
                     new("Quote", "IBM", "EmptyQuote", InvestmentDataState.Retained, PriceDate: Portfolio.Today)]));
                using var request = new HttpRequestMessage(HttpMethod.Get, "/beleggingen");
                request.Headers.Add("Cookie", ".AspNetCore.Culture=" + Uri.EscapeDataString("c=" + language + "|uic=" + language));
                using var response = await client.SendAsync(request);
                var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
                True(response.IsSuccessStatusCode);
                True(html.Contains(language == "nl-NL" ? "Resultaat laatste API-actie" : "Latest API action result"));
                True(html.Contains(language == "nl-NL" ? "aanvullende betaalde gegevensrechten" : "additional paid data entitlements"));
                True(html.Contains(language == "nl-NL" ? "geen koers terug voor dit symbool" : "no price for this symbol"));
                True(html.Contains(language == "nl-NL" ? "Laatst opgeslagen gegevens behouden" : "Previously stored data retained"));
                True(html.Contains("AMZN")); True(html.Contains("USD→EUR")); True(html.Contains("HTTP 200"));
                True(html.Contains("GLOBAL_QUOTE")); True(html.Contains("CURRENCY_EXCHANGE_RATE"));
                True(html.Contains("function=CURRENCY_EXCHANGE_RATE")); True(html.Contains("from_currency=USD")); True(html.Contains("to_currency=EUR"));
                True(html.Contains("function=GLOBAL_QUOTE")); True(html.Contains("symbol=AMZN"));
                True(html.Contains(language == "nl-NL" ? "entitlement=weggelaten" : "entitlement=omitted"));
                True(html.Contains(language == "nl-NL" ? "Herkenningsreden:" : "Classification reason:"));
                True(html.Contains(language == "nl-NL" ? "Geen verzendparameters vastgelegd" : "No send parameters recorded"));
                True(!html.Contains("InvestmentStatus")); True(!html.Contains("InvestmentDataState"));
                True(!html.Contains(MarketFixture.Secret)); True(!html.Contains("apikey="));
                if (status == "UpdateFailed") True(html.Contains(language == "nl-NL" ? "Geen gegevens bijgewerkt." : "No data updated."));
                if (status == "Requesting") True(html.Contains(language == "nl-NL" ? "API-actie wordt uitgevoerd" : "API action is running"));
            }
            foreach (var language in new[] { "nl-NL", "en-GB" })
            foreach (var reason in Enum.GetValues<InvestmentProviderReason>())
            {
                var entitlement = (int)reason % 3 == 0 ? null : (int)reason % 3 == 1 ? "delayed" : "realtime";
                store.Update(s => s.Report = new("TestAmzn", DateTime.UtcNow, DateTime.UtcNow, "UpdateFailed",
                    [new("Test", "AMZN", "ProviderRejected", InvestmentDataState.NotApplicable, HttpStatusCode: 200,
                        Parameters: new("GLOBAL_QUOTE", "AMZN", Entitlement: entitlement), ProviderReason: reason)]));
                using var request = new HttpRequestMessage(HttpMethod.Get, "/beleggingen");
                request.Headers.Add("Cookie", ".AspNetCore.Culture=" + Uri.EscapeDataString("c=" + language + "|uic=" + language));
                using var response = await client.SendAsync(request);
                var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
                True(response.IsSuccessStatusCode); True(html.Contains("symbol=AMZN")); True(html.Contains("HTTP 200"));
                if (entitlement != null) True(html.Contains("entitlement=" + entitlement));
                True(html.Contains(language == "nl-NL" ? "Dit bewijst niet dat betaalde toegang nodig is" : "This does not prove paid access is required"));
                True(!html.Contains("InvestmentProviderReason")); True(!html.Contains("InvestmentOperation"));
                True(!html.Contains(MarketFixture.Secret)); True(!html.Contains("apikey="));
            }
            foreach (var language in new[] { "nl-NL", "en-GB" })
            foreach (var supplied in new[] { false, true })
            foreach (var expired in new[] { false, true })
            {
                var now = DateTime.UtcNow;
                var limit = new InvestmentProviderLimit(now.AddMinutes(-3), expired ? now.AddMinutes(-1) : now.AddMinutes(2), supplied);
                store.Update(s =>
                {
                    s.ProviderLimit = limit; s.BudgetDayUtc = DateOnly.FromDateTime(now); s.RequestsToday = 25; s.Settings.DailyLimit = 25;
                    s.Status = supplied ? "ProviderRetryWait" : "ProviderLimit";
                    s.Report = new("Refresh", now, now, "UpdateFailed",
                        [new("Fx", "USD→EUR", s.Status, InvestmentDataState.Retained, HttpStatusCode: supplied ? null : 200)], ErrorCode: s.Status);
                });
                using var request = new HttpRequestMessage(HttpMethod.Get, "/beleggingen");
                request.Headers.Add("Cookie", ".AspNetCore.Culture=" + Uri.EscapeDataString("c=" + language + "|uic=" + language));
                using var response = await client.SendAsync(request);
                var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
                True(response.IsSuccessStatusCode);
                True(html.Contains(language == "nl-NL" ? "API-providerlimiet gemeld" : "API provider limit reported"));
                True(html.Contains(language == "nl-NL" ? "Lokaal API-dagbudget bereikt" : "Local daily API budget reached"));
                True(html.Contains("00:00 UTC"));
                Equal(expired ? 1 : 2, html.Split("role=\"timer\"").Length - 1);
                if (expired) True(html.Contains(language == "nl-NL" ? "De wachttijd is voorbij" : "The waiting time has elapsed"));
                if (supplied)
                {
                    True(html.Contains("Retry-After"));
                    True(html.Contains(language == "nl-NL" ? "Er is geen nieuwe aanvraag verstuurd" : "No new request was sent"));
                }
                else
                {
                    True(html.Contains(language == "nl-NL" ? "geen bevestigde reset" : "not a confirmed provider limit reset"));
                    True(html.Contains(language == "nl-NL" ? "Handmatig opnieuw proberen blijft mogelijk" : "Manual retries remain available"));
                }
                True(!html.Contains("InvestmentProvider")); True(!html.Contains("InvestmentLocalBudget"));
                True(!html.Contains("uit voorzorg 24 uur")); True(!html.Contains("paused for 24 hours"));
                True(!html.Contains(MarketFixture.Secret)); True(!html.Contains("apikey="));
            }
            await using var scope = app.Services.CreateAsyncScope();
            await using var renderer = new HtmlRenderer(scope.ServiceProvider, app.Services.GetRequiredService<ILoggerFactory>());
            var feedback = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await renderer.RenderComponentAsync<Huishoudboek.Web.Components.Shared.InvestmentMarketFeedback>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { ["Running"] = true, ["Error"] = "Safe local failure <details>" }));
                return output.ToHtmlString();
            });
            True(feedback.Contains("role=\"alert\"")); True(feedback.Contains("role=\"status\""));
            True(feedback.Contains("Safe local failure &lt;details&gt;")); True(!feedback.Contains("<details>"));
            var elapsedBudget = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await renderer.RenderComponentAsync<Huishoudboek.Web.Components.Shared.InvestmentRequestLimits>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { ["DailyBudgetResetUtc"] = DateTime.UtcNow.AddSeconds(-1) }));
                return output.ToHtmlString();
            });
            True(!elapsedBudget.Contains("role=\"timer\"")); True(!elapsedBudget.Contains("InvestmentLocalBudget"));
            True(elapsedBudget.Contains("role=\"status\""));
        }
        finally { await app.StopAsync(); }
    }

    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected validation failure."); }
    private static void ExpectMarketError(string code, Action action)
    {
        try { action(); } catch (MarketDataException ex) { Equal(code, ex.Code); return; }
        throw new Exception("Expected market-data failure: " + code);
    }
    private static void InDirectory(Action<string> test)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "investment-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { test(path); } finally { Directory.Delete(path, recursive: true); }
    }
    private static string QuoteJson(string price = "110", string symbol = "IBM") => JsonSerializer.Serialize(new Dictionary<string, object>
    { ["Global Quote"] = new Dictionary<string, string> { ["01. symbol"] = symbol, ["05. price"] = price, ["07. latest trading day"] = "2026-11-16", ["08. previous close"] = "100" } });
    private static string FxJson() => """{"Realtime Currency Exchange Rate":{"1. From_Currency Code":"USD","3. To_Currency Code":"EUR","5. Exchange Rate":"0.9","6. Last Refreshed":"2026-11-16 21:00:00","7. Time Zone":"UTC"}}""";

    private sealed class FakeHandler(Func<Uri, string> reply) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<Uri> Uris { get; } = [];
        public Func<Uri, string> Reply { get; set; } = reply;
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string? RetryAfter { get; set; }
        public HttpContent? Content { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Uris.Add(request.RequestUri!);
            var response = new HttpResponseMessage(StatusCode) { Content = Content ?? new StringContent(Reply(request.RequestUri!)) };
            if (RetryAfter != null) response.Headers.TryAddWithoutValidation("Retry-After", RetryAfter);
            return Task.FromResult(response);
        }
    }
    private sealed class TimeoutReadStream : MemoryStream
    {
        public bool CancellableRead { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            CancellableRead = cancellationToken.CanBeCanceled;
            throw new OperationCanceledException(cancellationToken);
        }
    }
    private sealed class MarketFixture : IDisposable
    {
        public const string Secret = "fictionalkey12345";
        public AdministrationCoordinator Coordinator { get; }
        public InvestmentMarketStore Store { get; }
        public FakeHandler Handler { get; }
        private readonly AlphaVantageClient _client;
        public InvestmentMarketService Market { get; }
        public MarketFixture(string directory, string currency = "USD")
        {
            var f = new Portfolio(currency); f.Instrument.ProviderSymbol = "AMZN"; f.Instrument.MappingConfirmed = true; f.Buy(1, 100);
            Coordinator = new(directory, initialize: true);
            Coordinator.Update(Coordinator.Read().Revision, d =>
            { d.Owners = f.Data.Owners; d.Accounts = f.Data.Accounts; d.Instruments = f.Data.Instruments; d.InvestmentTrades = f.Data.InvestmentTrades; });
            Store = new(directory);
            Handler = new(uri => uri.Query.Contains("CURRENCY_EXCHANGE_RATE") ? FxJson() : QuoteJson(symbol: "AMZN"));
            _client = new(Handler); Market = new(Store, _client, Coordinator);
            Market.SaveSettingsAsync(new() { Enabled = true, Frequency = QuoteFrequency.Manual }, Secret, false).GetAwaiter().GetResult();
        }
        public void ClearCooldown() => Store.Update(s => s.LastAttemptUtc = DateTime.UtcNow.AddMinutes(-2));
        public void Dispose() { Market.Dispose(); _client.Dispose(); Coordinator.Dispose(); }
    }
    private sealed class TestAuthentication : AuthenticationStateProvider
    {
        public ClaimsPrincipal Principal { get; set; } = new(new ClaimsIdentity());
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(Principal));
    }
    private sealed class Portfolio
    {
        public static readonly DateOnly Today = new(2026, 11, 16);
        public HouseholdData Data { get; } = HouseholdData.CreateEmpty();
        public Owner Owner { get; } = new() { Name = "Investor" };
        public Account Account { get; }
        public InvestmentInstrument Instrument { get; }
        public Portfolio(string currency = "EUR")
        {
            Account = new() { Name = "Broker", Kind = AccountKind.Investment, OwnerIds = [Owner.Id], OpeningDate = Today.AddDays(-30) };
            Instrument = new() { Name = "Fictional stock", Exchange = "Example", Currency = currency, ManualPriceDate = Today };
            Data.Owners.Add(Owner); Data.Accounts.Add(Account); Data.Instruments.Add(Instrument);
        }
        public InvestmentTrade Buy(decimal quantity, decimal price) => Trade(InvestmentTradeKind.Buy, quantity, price);
        public InvestmentTrade Trade(InvestmentTradeKind kind, decimal quantity, decimal? price)
        {
            var t = new InvestmentTrade { AccountId = Account.Id, InstrumentId = Instrument.Id, Kind = kind, Quantity = quantity,
                UnitPrice = price, Currency = Instrument.Currency, Date = Today.AddDays(-10), Sequence = Data.InvestmentTrades.Count + 1 };
            Data.InvestmentTrades.Add(t); return t;
        }
        public InvestmentPosition Position(IReadOnlyList<InvestmentFxRate>? rates = null)
        { Validation.Validate(Data); return Investments.Positions(Data, [], rates ?? [], Today).Single(); }
    }
}
