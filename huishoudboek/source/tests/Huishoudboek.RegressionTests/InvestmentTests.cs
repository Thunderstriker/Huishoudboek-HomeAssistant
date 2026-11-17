using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

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
            { c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DROP TABLE InvestmentTrade; DROP TABLE InvestmentInstrument; PRAGMA user_version=14;"; cmd.ExecuteNonQuery(); }
            var old = SqliteHouseholdStore.ReadBackup(path); Equal(0, old.Instruments.Count);
            var upgraded = new SqliteHouseholdStore(path, allowCreate: false); Equal(0, upgraded.Load().InvestmentTrades.Count);
            var backup = Directory.GetFiles(directory, "*.voor-versie-15-*.db").Single();
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
        yield return ("Provider limits persist a cooldown and do not retry", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var store = new InvestmentMarketStore(directory); var handler = new FakeHandler(_ => "{\"Information\":\"25 requests per day\"}");
            using var client = new AlphaVantageClient(handler); using var market = new InvestmentMarketService(store, client, coordinator);
            market.SaveSettingsAsync(new() { Enabled = true }, "fictionalkey12345", false).GetAwaiter().GetResult();
            try { market.TestAsync().GetAwaiter().GetResult(); } catch (MarketDataException) { }
            var until = store.Read(s => s.BlockedUntilUtc);
            try { market.TestAsync().GetAwaiter().GetResult(); } catch (MarketDataException) { }
            Equal(until, store.Read(s => s.BlockedUntilUtc));
            Equal(1, handler.Calls); Equal(1, market.Read().RequestsToday);
            True(store.Read(s => s.BlockedUntilUtc) > DateTime.UtcNow);
            Equal("ProviderLimit", new InvestmentMarketStore(directory).Snapshot(DateTime.UtcNow).Status);
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
        yield return ("Market cache corruption fails closed without blocking household records", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            _ = new InvestmentMarketStore(directory);
            var path = Path.Combine(directory, "market-data", "state.json"); File.WriteAllText(path, "{broken");
            var damaged = new InvestmentMarketStore(directory); var snapshot = damaged.Snapshot(DateTime.UtcNow);
            Equal("ConfigurationInvalid", snapshot.Status); True(!snapshot.Settings.Enabled); Equal(25, snapshot.RequestsToday);
            True(damaged.Read(s => s.BlockedUntilUtc) > DateTime.UtcNow);
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
            authentication.Principal = sessions.Create(false);
            True(!adapter.ReadAsync().GetAwaiter().GetResult().HasKey);
            sessions.Revoke(authentication.Principal);
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
        }
        finally { await app.StopAsync(); }
    }

    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected validation failure."); }
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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Uris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Reply(request.RequestUri!)) });
        }
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
