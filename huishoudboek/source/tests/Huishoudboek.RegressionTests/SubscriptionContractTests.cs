using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Huishoudboek.Web.Components.Shared;
using Microsoft.Data.Sqlite;

internal static class SubscriptionContractTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);
    private static SubscriptionContract Contract() => new() { Name = "Streaming", Provider = "Provider", Amount = 12.34m,
        RenewalDate = Today.AddDays(10), NoticePeriod = 0, Kind = ContractKind.Streaming };

    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("External contracts work without accounts and change no financial reports or forecasts", () =>
        {
            var state = new WebState(null!); var data = state.Data;
            var before = FinancialSnapshot(data);
            var draft = EditorDraft.Contract(state);
            Set(draft, "name", "Netflix"); Set(draft, "provider", "Netflix"); Set(draft, "contractKind", "Streaming");
            Set(draft, "contractAmount", "12,34"); Set(draft, "externalPayment", "Other account"); Set(draft, "country", "be");
            True(draft.ValidateFields()); Equal(0, data.Contracts.Count);
            draft.Apply(data); Validation.Validate(data);
            Equal("BE", data.Contracts.Single().CountryCode); Equal("Other account", data.Contracts.Single().ExternalPaymentLabel);
            Equal<Guid?>(null, data.Contracts.Single().AccountId); Equal(148.08m, SubscriptionContracts.Costs(data.Contracts, Today).ExternalAnnual);
            Equal(before, FinancialSnapshot(data));
            data.Contracts.Clear(); Equal(before, FinancialSnapshot(data));
        });
        yield return ("Contract deadlines use calendar months, explicit overrides and unknown terms", () =>
        {
            var c = Contract() with { RenewalDate = new(2028, 3, 31), NoticePeriod = 1, NoticeUnit = ContractNoticeUnit.Months };
            Equal<DateOnly?>(new(2028, 2, 29), SubscriptionContracts.Deadline(c));
            c.RenewalDate = new(2027, 3, 31); Equal<DateOnly?>(new(2027, 2, 28), SubscriptionContracts.Deadline(c));
            c.NoticeUnit = ContractNoticeUnit.Days; Equal<DateOnly?>(new(2027, 3, 30), SubscriptionContracts.Deadline(c));
            c.CancellationDeadline = new(2027, 1, 15); Equal(c.CancellationDeadline, SubscriptionContracts.Deadline(c));
            c.NoticePeriod = null; c.CancellationDeadline = null; Equal<DateOnly?>(null, SubscriptionContracts.Deadline(c));
            c.NoticePeriod = 0; Equal(c.RenewalDate, SubscriptionContracts.Deadline(c));
            c.RenewalDate = null; Equal<DateOnly?>(null, SubscriptionContracts.Deadline(c));
        });
        yield return ("Attention retains past deadlines without pretending renewal or cancellation happened", () =>
        {
            var c = Contract() with { RenewalDate = Today.AddDays(-5), NoticePeriod = 30, PromotionEndsOn = Today.AddDays(30), ReviewOn = Today.AddDays(31) };
            var before = JsonSerializer.Serialize(c); var points = SubscriptionContracts.Attention([c], Today);
            True(points.Any(a => a.Reason == "ContractRenewalDue")); True(points.Any(a => a.Reason == "ContractCancellationDue"));
            True(points.Any(a => a.Reason == "ContractPromotionDue")); True(!points.Any(a => a.Reason == "ContractReviewDue"));
            Equal(before, JsonSerializer.Serialize(c));
            Equal(0, SubscriptionContracts.Attention([c with { Status = ContractStatus.Cancelled }], Today).Count);
            var unknown = SubscriptionContracts.Attention([c with { RenewalDate = null, NoticePeriod = null, PromotionEndsOn = null }], Today);
            Equal(2, unknown.Count); True(unknown.All(a => a.Date == null));
            var pending = SubscriptionContracts.Attention([c with { Status = ContractStatus.CancellationRequested, PromotionEndsOn = null }], Today);
            Equal("ContractCancellationPending", pending.Single().Reason);
        });
        yield return ("Indefinite contracts skip only missing renewal and cancellation reminders", () =>
        {
            var c = Contract() with { IsIndefinite = true, RenewalDate = null, NoticePeriod = null };
            Equal(0, SubscriptionContracts.Attention([c], Today).Count);
            Equal(148.08m, SubscriptionContracts.Costs([c], Today).Annual);
            var dated = c with { Status = ContractStatus.Trial, TrialEndsOn = Today.AddDays(1), PromotionEndsOn = Today.AddDays(2),
                ReviewOn = Today.AddDays(3), EndDate = Today.AddDays(4), CancellationDeadline = Today.AddDays(5), RenewalDate = Today.AddDays(6) };
            var reasons = SubscriptionContracts.Attention([dated], Today).Select(a => a.Reason);
            True(reasons.SequenceEqual(["ContractTrialDue", "ContractPromotionDue", "ContractReviewDue", "ContractEndDue", "ContractCancellationDue", "ContractRenewalDue"]));
            Equal("ContractTrialUnknown", SubscriptionContracts.Attention([c with { Status = ContractStatus.Trial }], Today).Single().Reason);
            var pending = c with { Status = ContractStatus.CancellationRequested, EndDate = Today.AddDays(2) };
            True(SubscriptionContracts.Attention([pending], Today).Any(a => a.Reason == "ContractCancellationPending" && a.Date == pending.EndDate));
            Equal(0m, SubscriptionContracts.Costs([pending], Today.AddDays(3)).Annual);
        });
        yield return ("Contract run rates distinguish account and external costs, closed periods and unknown prices", () =>
        {
            var c = Contract() with { Amount = 10 };
            var contracts = new[] { c, c with { AccountId = Guid.NewGuid(), Amount = 100, BillingCycle = ContractBillingCycle.Yearly },
                c with { Amount = 30, BillingCycle = ContractBillingCycle.Quarterly }, c with { Amount = 60, BillingCycle = ContractBillingCycle.HalfYearly },
                c with { Amount = 2, BillingCycle = ContractBillingCycle.Weekly }, c with { Status = ContractStatus.Expired },
                c with { Status = ContractStatus.Cancelled }, c with { StartDate = Today.AddDays(1) }, c with { EndDate = Today.AddDays(-1) },
                c with { Status = ContractStatus.Trial, TrialEndsOn = Today.AddDays(-1) }, c with { PromotionEndsOn = Today.AddDays(-1) } };
            var costs = SubscriptionContracts.Costs(contracts, Today);
            Equal(100m, costs.TrackedAnnual); Equal(464m, costs.ExternalAnnual); Equal(2, costs.UnknownPriceCount);
            Equal<decimal?>(20, SubscriptionContracts.CurrentAmount(c with { PromotionEndsOn = Today.AddDays(-1), RegularAmount = 20 }, Today));
            Equal<decimal?>(10, SubscriptionContracts.CurrentAmount(c with { PromotionEndsOn = Today, RegularAmount = 20 }, Today));
            Equal(120m, SubscriptionContracts.Costs([c with { Status = ContractStatus.CancellationRequested, EndDate = Today }], Today).Annual);
        });
        yield return ("Owner filters follow explicit contract owners, then account ownership and shared external contracts", () =>
        {
            var data = HouseholdData.CreateEmpty(); var a = new Owner { Name = "A" }; var b = new Owner { Name = "B" };
            data.Owners.AddRange([a, b]); var account = new Account { Name = "A account", OwnerIds = [a.Id] }; data.Accounts.Add(account);
            data.Contracts.AddRange([Contract(), Contract() with { OwnerId = a.Id }, Contract() with { OwnerId = b.Id, AccountId = account.Id },
                Contract() with { AccountId = account.Id }]);
            Equal(4, SubscriptionContracts.Visible(data, null).Count()); Equal(3, SubscriptionContracts.Visible(data, a.Id).Count());
            Equal(2, SubscriptionContracts.Visible(data, b.Id).Count()); Validation.Validate(data);
        });
        yield return ("Contract links validate references and cannot silently duplicate or modify a payment plan", () =>
        {
            var state = Fixture(); var p = state.Data.RecurringPayments.Single(); var before = FinancialSnapshot(state.Data);
            var draft = EditorDraft.Contract(state, payment: p); True(draft.ValidateFields()); draft.Apply(state.Data); Validation.Validate(state.Data);
            Equal(before, FinancialSnapshot(state.Data)); Equal<Guid?>(p.Id, state.Data.Contracts.Single().RecurringPaymentId);
            var c = state.Data.Contracts.Single();
            Invalid(state.Data, c with { Id = Guid.NewGuid() });
            Invalid(state.Data, c with { AccountId = null });
            Invalid(state.Data, c with { AccountId = Guid.NewGuid() });
            Invalid(state.Data, c with { RecurringPaymentId = Guid.NewGuid() });
            p.Frequency = PaymentFrequency.Once; Throws(() => Validation.Validate(state.Data)); p.Frequency = PaymentFrequency.Monthly;
            HouseholdEdits.DeleteRecurring(state.Data, p.Id); Equal<Guid?>(null, c.RecurringPaymentId); Validation.Validate(state.Data);
            var category = state.Data.Categories.Single(x => x.Id == c.CategoryId); Categorization.DeleteCategory(state.Data, category.Id);
            Equal<Guid?>(null, c.CategoryId); Validation.Validate(state.Data);
        });
        yield return ("Contract draft visibility and validation preserve telecom details and clear incompatible links", () => WithCultures(() =>
        {
            var s = Fixture(); var draft = EditorDraft.Contract(s, payment: s.Data.RecurringPayments.Single());
            Set(draft, "contractKind", "Mobile"); Set(draft, "download", "300,5"); Set(draft, "upload", "30.25"); Set(draft, "dataLimit", "50");
            True(draft.ValidateFields()); True(draft.IsFieldVisible(Field(draft, "dataLimit"))); True(!draft.IsFieldVisible(Field(draft, "streams")));
            draft.Apply(s.Data); Validation.Validate(s.Data); Equal<decimal?>(300.5m, s.Data.Contracts.Single().DownloadMbps);
            Set(draft, "unlimited", "True"); True(!draft.IsFieldVisible(Field(draft, "dataLimit")));
            draft.Apply(s.Data); Equal<decimal?>(null, s.Data.Contracts.Single().DataLimitGb);
            Set(draft, "paymentAccount", ""); draft.FieldChanged(Field(draft, "paymentAccount")); Equal("", draft.Text("contractRecurring"));
            True(draft.ValidateFields()); draft.Apply(s.Data); Validation.Validate(s.Data); Equal<Guid?>(null, s.Data.Contracts.Single().RecurringPaymentId);
            Set(draft, "download", "-1"); True(!draft.ValidateFields()); True(Field(draft, "download").Error.Length > 0);
            Set(draft, "contractKind", "Streaming"); Set(draft, "streams", "2"); Set(draft, "ads", "False");
            True(draft.ValidateFields()); draft.Apply(s.Data); Validation.Validate(s.Data);
            Equal<decimal?>(null, s.Data.Contracts.Single().DownloadMbps); Equal<bool?>(false, s.Data.Contracts.Single().HasAds);
        }));
        yield return ("An indefinite telecom bundle saves without an end date, keeps its terms and counts its linked price once", () => WithCultures(() =>
        {
            var s = Fixture(); var payment = s.Data.RecurringPayments.Single(); var before = FinancialSnapshot(s.Data);
            var draft = EditorDraft.Contract(s, payment: payment);
            Equal("False", draft.Text("indefinite"));
            Set(draft, "contractKind", "TelecomBundle"); Set(draft, "indefinite", "True"); Set(draft, "download", "300");
            Set(draft, "dataLimit", "50"); Set(draft, "noticePeriod", "1"); Set(draft, "commitment", "2027-01-01");
            True(draft.ValidateFields()); draft.Apply(s.Data); Validation.Validate(s.Data);
            var c = s.Data.Contracts.Single(); True(c.IsIndefinite); Equal<DateOnly?>(null, c.EndDate);
            Equal(new DateOnly(2027, 1, 1), c.MinimumCommitmentUntil); Equal<int?>(1, c.NoticePeriod);
            Equal<decimal?>(300, c.DownloadMbps); Equal<decimal?>(50, c.DataLimitGb);
            Equal<Guid?>(payment.Id, c.RecurringPaymentId); Equal(360m, SubscriptionContracts.Costs(s.Data.Contracts, Today).TrackedAnnual);
            Equal(before, FinancialSnapshot(s.Data));
            var reloaded = EditorDraft.Contract(s, c); Equal("True", reloaded.Text("indefinite"));
            var form = WebUtility.HtmlDecode(UxTests.Render<Editor>(s, new Dictionary<string, object?> { [nameof(Editor.Draft)] = reloaded }));
            True(form.Contains("id=\"indefinite\" type=\"checkbox\" checked"));
            Equal(CultureInfo.CurrentUICulture.Name == "nl-NL" ? "Onbepaalde duur" : "Indefinite term", Field(reloaded, "indefinite").Label);
            var page = WebUtility.HtmlDecode(UxTests.Render<Contracts>(s));
            bool Shows(string tag, string key) => Regex.IsMatch(page, $@"<{tag}\b[^>]*>{Regex.Escape(UiCulture.Text(key))}</{tag}>");
            True(Shows("span", "ContractIndefinite"));
            True(!Shows("dt", "ContractRenewal") && !Shows("dt", "ContractDeadline"));
            Set(reloaded, "end", "2027-02-01"); True(reloaded.ValidateFields()); reloaded.Apply(s.Data); Validation.Validate(s.Data);
            True(s.Data.Contracts.Single().IsIndefinite); Equal(new DateOnly(2027, 2, 1), s.Data.Contracts.Single().EndDate);
            page = WebUtility.HtmlDecode(UxTests.Render<Contracts>(s));
            True(Shows("span", "ContractIndefinite") && Shows("dt", "ContractEnd"));
        }));
        yield return ("Contract validation rejects unsafe links, invalid units, duplicate IDs and incompatible data limits", () =>
        {
            var data = HouseholdData.CreateEmpty(); var c = Contract();
            foreach (var url in new[] { "javascript:alert(1)", "data:text/html,hi", "/relative", "https://user:password@example.com/", "https://example.com/\n" })
                Invalid(data, c with { CancellationUrl = url });
            foreach (var bad in new[] { c with { Amount = -1 }, c with { Amount = 1.001m }, c with { CountryCode = "BEL" },
                c with { NoticePeriod = -1 }, c with { DownloadMbps = 0 }, c with { UnlimitedData = true, DataLimitGb = 10 },
                c with { SimultaneousStreams = 0 }, c with { Status = (ContractStatus)99 }, c with { OwnerId = Guid.NewGuid() },
                c with { StartDate = Today, EndDate = Today.AddDays(-1) }, c with { Id = Guid.Empty },
                c with { RenewalDate = new(1, 1, 1), NoticePeriod = 1 } }) Invalid(data, bad);
            True(SubscriptionContracts.SafeLink("https://example.com/contract")); True(SubscriptionContracts.SafeLink(""));
            SubscriptionContracts.Save(data, c); data.Contracts.Add(c with { }); Throws(() => Validation.Validate(data));
        });
        yield return ("SQLite backups preserve all contract fields and schema 18 migration creates no fictional contracts", () => InDirectory(directory =>
        {
            var state = Fixture(); var c = Contract() with { Kind = ContractKind.Internet, AccountId = state.Data.Accounts.Single().Id,
                RecurringPaymentId = state.Data.RecurringPayments.Single().Id, OwnerId = state.Data.Owners.Single().Id,
                CategoryId = state.Data.RecurringPayments.Single().CategoryId, Plan = "Plan", CountryCode = "BE", Postcode = "9000",
                RegularAmount = 20, PromotionEndsOn = Today.AddDays(5), DownloadMbps = 300.5m, UploadMbps = 30, UnlimitedData = true,
                StartDate = Today.AddYears(-1), IsIndefinite = true, EndDate = Today.AddYears(1), NoticeUnit = ContractNoticeUnit.Months, NoticePeriod = 1,
                MinimumCommitmentUntil = Today, CancellationDeadline = Today.AddDays(1), ContractUrl = "https://example.com/contract",
                CancellationUrl = "https://example.com/cancel", ReviewOn = Today, NetworkAndConditions = "Fair use", Notes = "Notes" };
            SubscriptionContracts.Save(state.Data, c); var path = Path.Combine(directory, "source.db"); var store = new SqliteHouseholdStore(path); store.Save(state.Data);
            Equal(JsonSerializer.Serialize(state.Data.Contracts), JsonSerializer.Serialize(store.Load().Contracts));
            var backup = Path.Combine(directory, "backup.db"); store.Backup(backup); var target = new SqliteHouseholdStore(Path.Combine(directory, "target.db")); target.Restore(backup);
            Equal(JsonSerializer.Serialize(state.Data.Contracts), JsonSerializer.Serialize(target.Load().Contracts));
            Sql(path, RecognitionTests.RemoveSchema19Tables + "PRAGMA user_version=18;");
            var old = SqliteHouseholdStore.ReadBackup(path); Equal(0, old.Contracts.Count); Equal(18L, Scalar(path, "PRAGMA user_version"));
            var upgraded = new SqliteHouseholdStore(path, allowCreate: false); Equal(0, upgraded.Load().Contracts.Count);
            Equal((long)SqliteHouseholdStore.SchemaVersion, Scalar(path, "PRAGMA user_version")); Equal(1, Directory.GetFiles(directory, "*.voor-versie-*.db").Length);
            _ = new SqliteHouseholdStore(path, allowCreate: false); Equal(1, Directory.GetFiles(directory, "*.voor-versie-*.db").Length);
            target.Restore(Directory.GetFiles(directory, $"*.voor-versie-{SqliteHouseholdStore.SchemaVersion}-*.db").Single()); Equal(0, target.Load().Contracts.Count);
            Equal(FinancialSnapshot(old), FinancialSnapshot(upgraded.Load()));
        }));
        yield return ("Schema 20 upgrades and restores existing contracts without treating blank dates as indefinite", () => InDirectory(directory =>
        {
            var path = Path.Combine(directory, "schema20.db"); var state = Fixture();
            var c = Contract() with { RenewalDate = null, NoticePeriod = null };
            state.Data.Contracts.AddRange([c, Contract() with { EndDate = Today.AddDays(20) }]);
            new SqliteHouseholdStore(path).Save(state.Data);
            Sql(path, "ALTER TABLE [SubscriptionContract] DROP COLUMN [IsIndefinite]; PRAGMA user_version=20;");
            var expected = JsonSerializer.Serialize(state.Data.Contracts);
            Equal(expected, JsonSerializer.Serialize(SqliteHouseholdStore.ReadBackup(path).Contracts));
            Equal(20L, Scalar(path, "PRAGMA user_version"));
            var upgraded = new SqliteHouseholdStore(path, allowCreate: false);
            Equal((long)SqliteHouseholdStore.SchemaVersion, Scalar(path, "PRAGMA user_version"));
            Equal(expected, JsonSerializer.Serialize(upgraded.Load().Contracts));
            var old = Directory.GetFiles(directory, $"*.voor-versie-{SqliteHouseholdStore.SchemaVersion}-*.db").Single();
            Equal(20L, Scalar(old, "PRAGMA user_version"));
            var target = new SqliteHouseholdStore(Path.Combine(directory, "restored.db")); target.Restore(old);
            Equal(expected, JsonSerializer.Serialize(target.Load().Contracts));
            Equal(2, SubscriptionContracts.Attention([target.Load().Contracts.Single(x => x.Id == c.Id)], Today).Count);
        }));
        yield return ("Contract changes retain optimistic concurrency and validation rollback", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true); var start = coordinator.Read();
            var c = Contract(); var saved = coordinator.Update(start.Revision, d => SubscriptionContracts.Save(d, c));
            Throws(() => coordinator.Update(start.Revision, d => d.Contracts.Clear()));
            Throws(() => coordinator.Update(saved.Revision, d => SubscriptionContracts.Save(d, c with { Amount = -1 })));
            Equal(saved.Revision, coordinator.Read().Revision); Equal(12.34m, coordinator.Read().Data.Contracts.Single().Amount);
            var changed = coordinator.Update(saved.Revision, d => SubscriptionContracts.Save(d, c with { Status = ContractStatus.Cancelled }));
            Equal(ContractStatus.Cancelled, changed.Data.Contracts.Single().Status);
        }));
        yield return ("Contract page, reminders and editor render translated content, safe links and external accounts", () => WithCultures(() =>
        {
            var s = Fixture(); var c = Contract() with { Name = "<script>alert(1)</script>", ExternalPaymentLabel = "Other account", ContractUrl = "https://example.com/contract", HasAds = false, SimultaneousStreams = 2 };
            s.Data.Contracts.Add(c);
            var html = UxTests.Render<Contracts>(s); var decoded = WebUtility.HtmlDecode(html);
            True(decoded.Contains(UiCulture.Text("ContractsTitle"))); True(decoded.Contains(UiCulture.Text("ContractAlternativesDeferred")));
            True(decoded.Contains("Other account")); True(!html.Contains("<script>")); True(html.Contains("rel=\"noopener noreferrer\""));
            True(html.Contains("id=\"contract-" + c.Id)); True(decoded.Contains(UiCulture.Text("ContractWithoutAds")));
            var editor = EditorDraft.Contract(s, c); Set(editor, "cancelUrl", "javascript:alert(1)"); True(!editor.ValidateFields());
            var form = UxTests.Render<Editor>(s, new Dictionary<string, object?> { [nameof(Editor.Draft)] = editor });
            True(form.Contains("aria-describedby=\"cancelUrl-error\""));
            True(!decoded.Contains("ContractKindStreaming")); Equal(ToolbarPeriod.Today, UiNavigation.Period("abonnementen"));
            var weekly = WebUtility.HtmlDecode(UxTests.Render<WeeklyCheck>(new WebState(null!) { }));
            True(weekly.Contains(UiCulture.Text("WeeklyReviewTitle")));
        }));
        yield return ("Contract enum labels and form section headings exist in Dutch and English", () => WithCultures(() =>
        {
            foreach (var key in Enum.GetValues<ContractKind>().Select(k => "ContractKind" + k)
                .Concat(Enum.GetValues<ContractStatus>().Select(k => "ContractStatus" + k))
                .Concat(Enum.GetValues<ContractBillingCycle>().Select(k => "ContractBilling" + k)))
                True(UiCulture.Text(key) != key);
            var s = Fixture(); var draft = EditorDraft.Contract(s); Set(draft, "name", "Subscription"); Set(draft, "provider", "Provider");
            var form = WebUtility.HtmlDecode(UxTests.Render<Editor>(s, new Dictionary<string, object?> { [nameof(Editor.Draft)] = draft }));
            True(form.Contains(UiCulture.Text("ContractPaymentSection"))); True(form.Contains(UiCulture.Text("ContractDatesSection")));
        }));
        yield return ("Nullable streaming features and external labels survive backup round trips without bank accounts", () => InDirectory(directory =>
        {
            var data = HouseholdData.CreateEmpty();
            data.Contracts.AddRange(new bool?[] { null, false, true }.Select(ads => Contract() with { HasAds = ads, VideoQuality = "4K", SimultaneousStreams = 4,
                ExternalPaymentLabel = "Unregistered account", TrialEndsOn = Today.AddDays(5), BillingCycle = ContractBillingCycle.HalfYearly }));
            var store = new SqliteHouseholdStore(Path.Combine(directory, "contracts.db")); store.Save(data);
            Equal(JsonSerializer.Serialize(data.Contracts), JsonSerializer.Serialize(store.Load().Contracts));
            var backup = Path.Combine(directory, "backup.db"); store.Backup(backup); data.Contracts.Clear(); store.Save(data); store.Restore(backup);
            Equal(3, store.Load().Contracts.Count); Equal(0, store.Load().Accounts.Count);
        }));
    }

    private static WebState Fixture()
    {
        var s = new WebState(null!); var owner = new Owner { Name = "Owner" }; s.Data.Owners.Add(owner);
        var account = new Account { Name = "Current", OwnerIds = [owner.Id], OpeningBalance = 1000, OpeningDate = new(2026, 1, 1) }; s.Data.Accounts.Add(account);
        var category = s.Data.Categories.First(c => c.Purpose == CategoryPurpose.Normal);
        HouseholdEdits.SaveRecurring(s.Data, new() { Name = "Internet", Amount = -30, AccountId = account.Id, CategoryId = category.Id, StartDate = new(2026, 1, 1), DayOfMonth = 1 });
        Validation.Validate(s.Data); return s;
    }
    private static string FinancialSnapshot(HouseholdData data) => JsonSerializer.Serialize(new { data.Transactions, data.RecurringPayments,
        data.Accounts, data.Rules, Report = Finance.Report(data, 2026, 11, Today), Forecast = CashFlow.Project(data, Today) });
    private static EditorField Field(EditorDraft draft, string key) => draft.Fields.Single(f => f.Key == key);
    private static void Set(EditorDraft draft, string key, string value) => Field(draft, key).Value = value;
    private static void Invalid(HouseholdData data, SubscriptionContract contract)
    { var old = data.Contracts.ToList(); data.Contracts.RemoveAll(c => c.Id == contract.Id); data.Contracts.Add(contract); try { Throws(() => Validation.Validate(data)); } finally { data.Contracts = old; } }
    private static void Sql(string path, string sql)
    { using var c = new SqliteConnection($"Data Source={path};Pooling=False"); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    private static long Scalar(string path, string sql)
    { using var c = new SqliteConnection($"Data Source={path};Pooling=False"); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar()); }
    private static void InDirectory(Action<string> run)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "opencode", "contract-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); try { run(directory); } finally { Directory.Delete(directory, recursive: true); }
    }
    private static void WithCultures(Action run)
    {
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try { foreach (var name in new[] { "nl-NL", "en-GB" }) { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name); run(); } }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
    }
    private static void True(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void Throws(Action run) { try { run(); } catch (InvalidOperationException) { return; } throw new Exception("Expected validation/conflict failure."); }
}
