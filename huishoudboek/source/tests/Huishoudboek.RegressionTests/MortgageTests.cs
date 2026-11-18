using System.Globalization;
using System.Net;
using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class MortgageTests
{
    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("Legacy mortgages preserve nominal annual interest after loading", () =>
        {
            var mortgage = JsonSerializer.Deserialize<Mortgage>("""
                {"ReferencePrincipal":312000,"ReferenceDate":"2026-05-01","AnnualInterestPercent":3.34,
                 "MonthlyPayment":1527.64,"RemainingMonths":300,"FixedRateUntil":"2051-05-31"}
                """)!;
            Equal(MortgageRateConvention.NominalAnnual, mortgage.RateConvention);
            var rows = Finance.MortgageSchedule(mortgage, mortgage.ReferencePrincipal, mortgage.ReferenceDate);
            Equal(new DateOnly(2026, 6, 30), rows[0].Date);
            Equal(868.40m, rows[0].Interest); Equal(659.24m, rows[0].Principal);
            Equal(311340.76m, rows[0].Remaining); Equal(308685.41m, rows[4].Remaining);
        });
        yield return ("Effective annual interest converts by compounding rather than dividing by twelve", () =>
        {
            var m = Loan() with { RateConvention = MortgageRateConvention.EffectiveAnnual };
            var rows = Finance.MortgageSchedule(m, m.ReferencePrincipal, m.ReferenceDate, 5);
            Equal(.274161m, decimal.Round(Finance.MortgageMonthlyRate(m) * 100, 6));
            Equal(855.38m, rows[0].Interest); Equal(672.26m, rows[0].Principal);
            Equal(311327.74m, rows[0].Remaining); Equal(308620.23m, rows[4].Remaining);
            Equal(5, rows.Count);
            foreach (var row in rows) Equal(row.Payment, row.Interest + row.Principal);
        });
        yield return ("Exact periodic interest retains precision and takes precedence over annual rate", () =>
        {
            var m = Loan() with { RateConvention = MortgageRateConvention.Monthly, MonthlyInterestPercent = .27123456789m };
            Equal(m.MonthlyInterestPercent!.Value / 100, Finance.MortgageMonthlyRate(m));
            var rows = Finance.MortgageSchedule(m, m.ReferencePrincipal, m.ReferenceDate, 5);
            Equal(846.25m, rows[0].Interest);
            Equal(rows[4].Remaining, Finance.MortgageSchedule(m with { AnnualInterestPercent = 10 }, m.ReferencePrincipal, m.ReferenceDate, 5)[4].Remaining);
            m.MonthlyInterestPercent = .27m;
            Equal(842.40m, Finance.MortgageSchedule(m, m.ReferencePrincipal, m.ReferenceDate, 1)[0].Interest);
        });
        yield return ("Zero interest, final partial payment and non-amortizing warnings remain supported", () =>
        {
            foreach (var convention in Enum.GetValues<MortgageRateConvention>())
            {
                var m = Loan() with { RateConvention = convention, AnnualInterestPercent = 0, MonthlyInterestPercent = 0, MonthlyPayment = 70 };
                var rows = Finance.MortgageSchedule(m, 100, m.ReferenceDate);
                Equal(2, rows.Count); Equal(0m, rows[0].Interest); Equal(30m, rows[1].Payment); Equal(0m, rows[1].Remaining);
                m.MonthlyPayment = 1; m.AnnualInterestPercent = 100; m.MonthlyInterestPercent = 100;
                Equal(1, Finance.MortgageSchedule(m, 100, m.ReferenceDate).Count);
            }
        });
        yield return ("Reference-date schedule includes elapsed months without changing recorded debt", () =>
        {
            var data = Data(); var before = JsonSerializer.Serialize(data); var today = new DateOnly(2026, 11, 18);
            var full = DetailedReporting.Mortgage(data, today, fromReferenceDate: true);
            var future = DetailedReporting.Mortgage(data, today);
            Equal(312000m, full.Balance); Equal(full.Balance, future.Balance);
            Equal(new DateOnly(2026, 6, 30), full.Rows[0].Date);
            Equal(new DateOnly(2026, 12, 31), future.Rows[0].Date);
            Equal(300, full.Rows.Count); Equal(294, future.Rows.Count);
            Equal(before, JsonSerializer.Serialize(data));
        });
        yield return ("Mortgage schedules retain month ends, rate expiry and future reference boundaries", () =>
        {
            var m = Loan() with { ReferenceDate = new(2028, 1, 20), FixedRateUntil = new(2028, 2, 29) };
            var rows = Finance.MortgageSchedule(m, m.ReferencePrincipal, m.ReferenceDate, 2);
            Equal(new DateOnly(2028, 2, 29), rows[0].Date); Equal(new DateOnly(2028, 3, 31), rows[1].Date);
            Equal(false, rows[0].AssumesUnchangedRate); Equal(true, rows[1].AssumesUnchangedRate);
            var data = Data(); data.Mortgage = m with { OwnerIds = data.Mortgage!.OwnerIds };
            var today = new DateOnly(2026, 11, 18);
            Equal(0m, DetailedReporting.Mortgage(data, today, true).Balance);
            Equal(new DateOnly(2028, 2, 29), DetailedReporting.Mortgage(data, today, true).Rows[0].Date);
            Equal(0, DetailedReporting.Mortgage(data, today).Rows.Count);
            data.Mortgage = null;
            Equal(0, DetailedReporting.Mortgage(data, today, true).Rows.Count);
        });
        yield return ("An irregular first bank payment reduces debt only by recorded principal", () =>
        {
            var data = Data(); var owner = data.Owners[0];
            var account = new Account { Name = "Bank", OwnerIds = [owner.Id], OpeningDate = new(2026, 1, 1) };
            data.Accounts.Add(account);
            // Illustrative principal only: the actual lender's allocation must be entered, not guessed.
            data.Transactions.Add(new() { AccountId = account.Id, Kind = TransactionKind.Mortgage,
                Date = new(2026, 6, 30), Description = "First mortgage payment", Amount = -1831.51m, PrincipalComponent = 672.26m });
            Validation.Validate(data);
            var before = JsonSerializer.Serialize(data);
            var full = DetailedReporting.Mortgage(data, new(2026, 11, 18), fromReferenceDate: true);
            Equal(311327.74m, full.Balance); Equal(311340.76m, full.Rows[0].Remaining);
            Equal(full.Balance, Finance.MortgageBalance(data, new(2026, 11, 18)));
            Equal(1831.51m, Finance.Report(data, 2026, 6, new(2026, 11, 18)).Outflow);
            Equal(before, JsonSerializer.Serialize(data));
        });
        yield return ("Mortgage rate validation rejects missing, out-of-range and unknown conventions", () =>
        {
            var data = Data(); var m = data.Mortgage!;
            m.RateConvention = MortgageRateConvention.Monthly;
            Throws(() => Validation.Validate(data)); Throws(() => Finance.MortgageMonthlyRate(m));
            foreach (var invalid in new[] { -.001m, 100.001m })
            { m.MonthlyInterestPercent = invalid; Throws(() => Validation.Validate(data)); }
            foreach (var valid in new[] { 0m, .2741591690108m, 100m })
            { m.MonthlyInterestPercent = valid; Validation.Validate(data); }
            m.RateConvention = (MortgageRateConvention)99; Throws(() => Validation.Validate(data));
            Throws(() => Finance.MortgageMonthlyRate(m));
        });
        yield return ("Mortgage drafts round-trip exact rates in both display languages", () =>
        {
            WithCultures(() =>
            {
                var state = new WebState(null!); state.Data.Owners.Add(new() { Name = "Owner" });
                state.Data.Mortgage = Loan() with { OwnerIds = [state.Data.Owners[0].Id],
                    RateConvention = MortgageRateConvention.Monthly, MonthlyInterestPercent = .274159169010812345m,
                    AnnualInterestPercent = 3.3400123456789m };
                var draft = EditorDraft.Mortgage(state);
                var field = draft.Fields.Single(f => f.Key == "monthlyRate");
                Equal(true, draft.IsFieldVisible(field)); draft.Apply(state.Data); Validation.Validate(state.Data);
                Equal(.274159169010812345m, state.Data.Mortgage.MonthlyInterestPercent!.Value);
                Equal(3.3400123456789m, state.Data.Mortgage.AnnualInterestPercent);
                draft.Fields.Single(f => f.Key == "rateConvention").Value = "EffectiveAnnual";
                Equal(false, draft.IsFieldVisible(field)); field.Value = "invalid hidden draft";
                draft.Apply(state.Data); Validation.Validate(state.Data);
                Equal(MortgageRateConvention.EffectiveAnnual, state.Data.Mortgage.RateConvention);
            });
        });
        yield return ("Mortgage conventions and exact rates survive SQLite save and restore", () =>
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "mortgage-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                var data = Data(); data.Mortgage!.RateConvention = MortgageRateConvention.Monthly;
                data.Mortgage.MonthlyInterestPercent = .274159169010812345m;
                var store = new SqliteHouseholdStore(Path.Combine(path, "mortgage.db")); store.Save(data);
                var saved = store.Load();
                Equal(JsonSerializer.Serialize(data.Mortgage), JsonSerializer.Serialize(saved.Mortgage));
                var backup = Path.Combine(path, "backup.db"); store.Backup(backup);
                var restored = new SqliteHouseholdStore(Path.Combine(path, "restored.db")); restored.Restore(backup);
                Equal(JsonSerializer.Serialize(data.Mortgage), JsonSerializer.Serialize(restored.Load().Mortgage));
            }
            finally { Directory.Delete(path, recursive: true); }
        });
        yield return ("Mortgage page labels the full theoretical schedule and exact monthly rate in both languages", () =>
        {
            WithCultures(() => RenderPage().GetAwaiter().GetResult());
        });
    }

    private static async Task RenderPage()
    {
        var state = new WebState(null!); state.Data.Mortgage = Loan() with { RateConvention = MortgageRateConvention.EffectiveAnnual };
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization(); services.AddSingleton(state);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<MortgagePage>()).ToHtmlString()));
        Equal(true, html.Contains("30-06-2026")); Equal(true, html.Contains("mortgage-schedule-basis"));
        Equal(true, html.Contains(CultureInfo.CurrentUICulture.Name == "nl-NL" ? "Theoretisch schema vanaf peildatum" : "Theoretical schedule from reference date"));
        Equal(true, html.Contains(CultureInfo.CurrentUICulture.Name == "nl-NL" ? "Gebruikte maandrente: 0,274161" : "Monthly rate used: 0.274161"));
        Equal(false, html.Contains("MortgageReferenceScheduleNote")); Equal(false, html.Contains("MortgageFirstPaymentNote"));
    }

    private static Mortgage Loan() => new() { ReferencePrincipal = 312000, ReferenceDate = new(2026, 5, 1),
        AnnualInterestPercent = 3.34m, MonthlyPayment = 1527.64m, RemainingMonths = 300, FixedRateUntil = new(2051, 5, 31) };
    private static HouseholdData Data()
    {
        var data = HouseholdData.CreateEmpty(); var owner = new Owner { Name = "Owner" }; data.Owners.Add(owner);
        data.Mortgage = Loan() with { OwnerIds = [owner.Id] }; return data;
    }
    private static void WithCultures(Action run)
    {
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var name in new[] { "nl-NL", "en-GB" })
            { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name); run(); }
        }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
    }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void Throws(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected validation failure."); }
}
