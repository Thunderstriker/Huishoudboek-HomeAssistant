using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Huishoudboek.Web;
using Huishoudboek.Web.Components.Pages;
using Huishoudboek.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using CalendarPage = Huishoudboek.Web.Components.Pages.Calendar;

internal static class FinancialCalendarTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);
    public static IEnumerable<(string Name, Action Run)> All()
    {
        yield return ("Purchase timestamps recover weekend dates without changing bank data", () =>
        {
            foreach (var separator in new[] { "-", "/", "." })
            {
                var t = new Transaction { Date = Today, Amount = -50, Description = $"TEST SUPERMARKET 14{separator}11{separator}2026 12:49 CITY" };
                var before = JsonSerializer.Serialize(t);
                Equal(new PurchaseDateEvidence(new(2026, 11, 14), PurchaseDateSource.Description), SpendingLearning.DateFromDescription(t));
                Equal(before, JsonSerializer.Serialize(t));
            }
            var boundary = new Transaction { Date = new(2027, 1, 4), Description = "SHOP 31-12-2026 14:01", Amount = -10 };
            Equal<DateOnly?>(new(2026, 12, 31), SpendingLearning.DateFromDescription(boundary).Date);
        });
        yield return ("Timestamp inference rejects ambiguous, invalid, future, stale and invoice dates", () =>
        {
            foreach (var description in new[] { "SHOP 14-11-2026", "SHOP 14-11-2026 24:00", "SHOP 31-11-2026 12:00",
                "SHOP 17-11-2026 12:00", "SHOP 01-11-2026 12:00", "Invoice 14-11-2026 12:00",
                "SHOP 14-11-2026 12:00 and 15-11-2026 12:00", "SHOP 14-11-2026 12:99", "SHOP 14-11-2026 12:49:99", "REFERENCE 141120261200" })
                Equal<DateOnly?>(null, SpendingLearning.DateFromDescription(new() { Date = Today, Description = description }).Date);
        });
        yield return ("Reviewed receipts and manual purchase-date decisions take precedence and can be reset", () =>
        {
            var f = new History(); var t = f.Data.Transactions.Last();
            f.Data.PurchaseReceipts.Add(new() { TransactionId = t.Id, PurchaseDate = t.Date.AddDays(-1), IsReviewed = false });
            Equal(PurchaseDateSource.Description, SpendingLearning.PurchaseDate(f.Data, t).Source);
            f.Data.PurchaseReceipts[0].IsReviewed = true;
            Equal(PurchaseDateSource.Receipt, SpendingLearning.PurchaseDate(f.Data, t).Source);
            SpendingLearning.SetPurchaseDate(f.Data, t.Id, t.Date);
            Equal(PurchaseDateSource.Manual, SpendingLearning.PurchaseDate(f.Data, t).Source);
            SpendingLearning.SetPurchaseDate(f.Data, t.Id, null);
            Equal(PurchaseDateSource.Excluded, SpendingLearning.PurchaseDate(f.Data, t).Source);
            f.Data.PurchaseDateDecisions.Clear();
            Equal(PurchaseDateSource.Receipt, SpendingLearning.PurchaseDate(f.Data, t).Source);
            Throws(() => SpendingLearning.SetPurchaseDate(f.Data, t.Id, t.Date.AddDays(1)));
            Throws(() => SpendingLearning.SetPurchaseDate(f.Data, t.Id, t.Date.AddDays(-32)));
        });
        yield return ("Learning aggregates shopping-day purchases and includes confirmed quiet weeks", () =>
        {
            var f = new History(); var p = f.Pattern();
            Equal(12, p.CoveredWeeks); True(p.Ready); True(p.Strong);
            Equal(12, p.Weekdays.Single(w => w.Day == DayOfWeek.Wednesday).ActiveWeeks);
            Equal(50m, p.Weekdays.Single(w => w.Day == DayOfWeek.Wednesday).AverageWhenActive);
            Equal(100m, p.Weekdays.Single(w => w.Day == DayOfWeek.Saturday).AverageWhenActive);
            Equal(0m, p.Weekdays.Single(w => w.Day == DayOfWeek.Monday).ExpectedAmount);
            var skipped = History.Start.AddDays(2 + 5 * 7);
            f.Data.Transactions.RemoveAll(t => SpendingLearning.PurchaseDate(f.Data, t).Date == skipped);
            p = f.Pattern();
            Equal(12, p.CoveredWeeks); Equal(11, p.Weekdays.Single(w => w.Day == DayOfWeek.Wednesday).ActiveWeeks);
            True(p.Weekdays.Single(w => w.Day == DayOfWeek.Wednesday).ExpectedAmount < 50);
        });
        yield return ("Absent coverage, coverage gaps and the bank-posting tail are not zero-spending weeks", () =>
        {
            var f = new History(); f.Data.ImportBatches[0].CoverageFrom = null; f.Data.ImportBatches[0].CoverageThrough = null;
            Equal(0, f.Pattern().CoveredWeeks); True(!f.Pattern().Ready);
            f.Data.ImportBatches[0].CoverageFrom = History.Start.AddDays(28); f.Data.ImportBatches[0].CoverageThrough = Today;
            Equal(8, f.Pattern().CoveredWeeks);
            f.Data.ImportBatches[0].CoverageFrom = History.Start;
            f.Data.ImportBatches[0].CoverageThrough = Today.AddDays(-7);
            Equal(11, f.Pattern().CoveredWeeks);
        });
        yield return ("Suggestions and paused habits leave the original forecast unchanged", () =>
        {
            var f = new History(); var before = f.Forecast();
            True(before.Learning.Patterns.Single().Ready); Equal(0, before.ExpectedSpending.Count);
            f.Accept(false); var paused = f.Forecast();
            Equal(before.DailyVariableEstimate, paused.DailyVariableEstimate);
            True(before.Days.SequenceEqual(paused.Days)); Equal(before.EstimatedSpendable, paused.EstimatedSpendable);
        });
        yield return ("Accepted habits redistribute rather than duplicate the ordinary spending estimate", () =>
        {
            var f = new History(); var before = f.Forecast(); var bank = JsonSerializer.Serialize(f.Data.Transactions);
            f.Accept(); var after = f.Forecast();
            True(after.ExpectedSpending.Any(e => e.Date > Today && e.Date.DayOfWeek == DayOfWeek.Saturday));
            Equal(0m, after.DailyUnscheduledEstimate);
            Near(before.Days.Take(7).Sum(d => d.VariableSpending), after.Days.Take(7).Sum(d => d.VariableSpending), .05m);
            Equal(before.CurrentBalance, after.CurrentBalance); Equal(bank, JsonSerializer.Serialize(f.Data.Transactions));
            Equal(0m, after.PendingHabitReserve);
        });
        yield return ("Unknown purchase dates stay in the daily remainder and cannot teach Monday shopping", () =>
        {
            var f = new History();
            foreach (var t in f.Data.Transactions.Where((_, i) => i % 10 == 0)) t.Description = "SHOP WITHOUT DATE";
            f.Accept(); var forecast = f.Forecast();
            True(forecast.Learning.Patterns.Single().Active); True(forecast.DailyUnscheduledEstimate > 0);
            Equal(0m, forecast.Learning.Patterns.Single().Weekdays.Single(w => w.Day == DayOfWeek.Monday).ExpectedAmount);
            foreach (var t in f.Data.Transactions) t.Description = "SHOP WITHOUT DATE";
            forecast = f.Forecast(); True(!forecast.Learning.Patterns.Single().Active);
            Equal(forecast.DailyVariableEstimate, forecast.DailyUnscheduledEstimate);
        });
        yield return ("Learning uses cash-funded allocations and excludes fixed links, transfers and exceptional spending", () =>
        {
            var f = new History(); var t = f.Data.Transactions[0]; var other = f.Data.Categories.Single(c => c.Name == "Verjaardagen & cadeaus"); other.IsExceptional = true;
            f.Data.TransactionAllocations.Add(new() { TransactionId = t.Id, Amount = -15, CategoryId = f.Food.Id });
            f.Data.TransactionAllocations.Add(new() { TransactionId = t.Id, Amount = -10, CategoryId = other.Id });
            Equal(15m, f.Pattern().Evidence.Single(e => e.Transaction.Id == t.Id).Amount);
            var fixedEntry = f.Data.Transactions[1]; fixedEntry.RecurringPaymentId = Guid.NewGuid();
            var transfer = f.Data.Transactions[2]; transfer.Kind = TransactionKind.Transfer;
            True(!SpendingLearning.VariableLines(f.Data, f.Account.Id).Any(l => l.Transaction.Id == fixedEntry.Id || l.Transaction.Id == transfer.Id));
            var receipt = new Fixture(); receipt.Save();
            var lines = SpendingLearning.VariableLines(receipt.Data, receipt.Account.Id);
            Equal(40m, lines.Sum(l => l.TypicalSpending)); Equal(0, lines.Count(l => l.Transaction.Id == receipt.Receipt.Id));
        });
        yield return ("A fixed payment and payday remain separate and conservative alongside shopping", () =>
        {
            var f = new History(); f.Accept();
            f.Data.RecurringPayments.Add(new() { AccountId = f.Account.Id, Name = "Rent", Amount = -100, Frequency = PaymentFrequency.Once, StartDate = Today.AddDays(2), DayOfMonth = 18 });
            f.Data.RecurringPayments.Add(new() { AccountId = f.Account.Id, Name = "Salary", Amount = 1000, Frequency = PaymentFrequency.Once, StartDate = Today.AddDays(2), DayOfMonth = 18 });
            var forecast = f.Forecast(); var day = forecast.Days.Single(d => d.Date == Today.AddDays(2));
            Equal(100m, day.Payments); Equal(1000m, day.Income); Equal(day.OpeningBalance - day.Payments - day.VariableSpending, day.LowestBalance);
            Equal(day.LowestBalance + 1000, day.ClosingBalance);
        });
        yield return ("Known future weekend purchases replace estimates before their Monday bank booking", () =>
        {
            var f = new History(); f.Accept(); var future = f.Add(Today.AddDays(5), 130, 2);
            var forecast = f.Forecast(); var saturday = forecast.Days.Single(d => d.Date == Today.AddDays(5));
            Equal(130m, saturday.Payments); Equal(0m, saturday.VariableSpending);
            Equal(0m, forecast.Days.Single(d => d.Date == Today.AddDays(7)).Payments);
            var entry = forecast.Events.Single(e => e.TransactionId == future.Id);
            Equal(Today.AddDays(5), entry.Date); Equal(Today.AddDays(7), entry.DueDate); Equal(Today.AddDays(7), future.Date);
        });
        yield return ("Manual weekdays, weekly amounts and occurrence skips survive recalculation", () =>
        {
            var f = new History(); var habit = f.Accept();
            SpendingLearning.SaveHabit(f.Data, habit with { Weekdays = 1 << 4, WeeklyAmount = 77.77m });
            var forecast = f.Forecast(); var friday = Today.AddDays(4);
            Equal(77.77m, forecast.ExpectedSpending.Single(e => e.Date == friday).Amount);
            True(!forecast.ExpectedSpending.Any(e => e.Date == Today.AddDays(5)));
            SpendingLearning.SetAdjustment(f.Data, habit.Id, friday, 0);
            True(!f.Forecast().ExpectedSpending.Any(e => e.Date == friday));
            SpendingLearning.SetAdjustment(f.Data, habit.Id, friday, 12.34m);
            Equal(12.34m, f.Forecast().ExpectedSpending.Single(e => e.Date == friday).Amount);
            f.Data.SpendingAdjustments.Clear(); Equal(77.77m, f.Forecast().ExpectedSpending.Single(e => e.Date == friday).Amount);
        });
        yield return ("A smaller recorded future purchase replaces its whole shopping-day estimate", () =>
        {
            var f = new History(); f.Accept(); f.Add(Today.AddDays(5), 10, 2);
            var forecast = f.Forecast(); var saturday = forecast.Days.Single(d => d.Date == Today.AddDays(5));
            Equal(10m, saturday.Payments); Equal(0m, saturday.VariableSpending);
            True(!forecast.ExpectedSpending.Any(e => e.Date == saturday.Date));
        });
        yield return ("Known already-made purchases do not clear a different future shopping day", () =>
        {
            var f = new History(); var habit = f.Accept();
            SpendingLearning.SaveHabit(f.Data, habit with { Weekdays = 1 << 1, WeeklyAmount = 100 });
            var purchase = f.Add(Today.AddDays(-1), 20, 3);
            var forecast = f.Forecast(); var tuesday = forecast.Days.Single(d => d.Date == Today.AddDays(1));
            Equal(20m, tuesday.Payments - forecast.PendingHabitReserve);
            Equal(100m, tuesday.VariableSpending);
            Equal(Today.AddDays(-1), forecast.Events.Single(e => e.TransactionId == purchase.Id).PurchaseDate!.Value);
            var upcoming = HomeOverview.Create(f.Data, 2026, 11, Today).Upcoming.Single(e => e.TransactionId == purchase.Id);
            Equal(purchase.Date, upcoming.Date);
        });
        yield return ("Undated future payments occupy the nearby category budget without invented purchase dates", () =>
        {
            var f = new History(); f.Accept(); var before = f.Forecast();
            var purchase = f.Add(Today.AddDays(7), 150, 0); purchase.Description = "FUTURE SHOP WITHOUT PURCHASE DATE";
            var after = f.Forecast();
            Near(before.Days.Take(7).Sum(d => d.VariableSpending), after.Days.Take(7).Sum(d => d.VariableSpending + d.Payments), .01m);
            Equal<DateOnly?>(null, after.Events.Single(e => e.TransactionId == purchase.Id).PurchaseDate);
            Equal(Today.AddDays(7), purchase.Date);
        });
        yield return ("Manually selecting a new weekday assigns it a share without changing the weekly total", () =>
        {
            var f = new History(); var habit = f.Accept();
            SpendingLearning.SaveHabit(f.Data, habit with { Weekdays = (1 << 2) | (1 << 4), WeeklyAmount = 100 });
            var pattern = f.Pattern();
            True(pattern.Weekdays.Single(w => w.Day == DayOfWeek.Friday).ExpectedAmount > 0);
            Equal(100m, pattern.WeeklyAmount);
        });
        yield return ("Recent unconfirmed shopping is a dismissible reserve, never accumulated historical debt", () =>
        {
            var f = new History(); var habit = f.Accept(); var saturday = Today.AddDays(-2);
            f.Data.Transactions.RemoveAll(t => SpendingLearning.PurchaseDate(f.Data, t).Date == saturday);
            var forecast = f.Forecast(); True(forecast.PendingHabitReserve > 0);
            True(forecast.ExpectedSpending.Where(e => e.IsPending).All(e => e.Date >= Today.AddDays(-6)));
            Equal(0, forecast.Events.Count(e => e.IsUncertain));
            SpendingLearning.SetAdjustment(f.Data, habit.Id, saturday, 0);
            Equal(0m, f.Forecast().PendingHabitReserve);
        });
        yield return ("Undated already posted spending offsets pending reserves without linking bank rows", () =>
        {
            var f = new History(); f.Accept();
            var saturday = f.Data.Transactions.Last(); var before = JsonSerializer.Serialize(saturday);
            saturday.Description = "UNDATED POSTED SUPERMARKET";
            var count = f.Data.Transactions.Count;
            Equal(0m, f.Forecast().PendingHabitReserve); Equal(count, f.Data.Transactions.Count);
            Equal<Guid?>(null, saturday.RecurringPaymentId); Equal(0, f.Data.TransferMatches.Count);
            True(before != JsonSerializer.Serialize(saturday));
        });
        yield return ("Financial-day risk prioritizes intraday shortfalls, buffers and payment count", () =>
        {
            var account = new Account { MinimumCashBuffer = 100, EarmarkedAmount = 50 };
            var day = new DailyBalance(Today, 200, 500, 300, 10, -110, 390);
            Equal(FinancialDayRisk.Shortfall, FinancialCalendar.Risk(day, account, 10, 4));
            Equal(FinancialDayRisk.BufferRisk, FinancialCalendar.Risk(day with { LowestBalance = 149 }, account, 10, 0));
            Equal(FinancialDayRisk.BufferRisk, FinancialCalendar.Risk(day with { LowestBalance = 200 }, account, 10, 3));
            Equal(FinancialDayRisk.Busy, FinancialCalendar.Risk(day with { LowestBalance = 200 }, account, 10, 1));
            Equal(FinancialDayRisk.Comfortable, FinancialCalendar.Risk(day with { LowestBalance = 200, Payments = 15, VariableSpending = 5 }, account, 10, 1));
        });
        yield return ("Cash-calendar forecasts remain account-local and honor owner scope", () =>
        {
            var f = new History(); f.Accept(); var owner = new Owner { Name = "Other owner" }; f.Data.Owners.Add(owner);
            var other = new Account { Name = "Other bank", OwnerIds = [owner.Id], OpeningDate = History.Start, OpeningBalance = 100000 };
            f.Data.Accounts.Add(other); f.Account.OpeningBalance = 1;
            var all = CashFlow.Project(f.Data, Today); Equal(2, all.Accounts.Count);
            True(all.Accounts.Single(a => a.Account.Id == f.Account.Id).LowestBalance < 0);
            var scoped = CashFlow.Project(f.Data, Today, new HashSet<Guid> { other.Id }); Equal(1, scoped.Accounts.Count);
            Equal(0, scoped.Accounts.Single().ExpectedSpending.Count);
        });
        yield return ("Date decisions and pattern adjustments are validated and cleaned on category deletion and import undo", () =>
        {
            var f = new History(); var habit = f.Accept(); var t = f.Data.Transactions[0];
            SpendingLearning.SetPurchaseDate(f.Data, t.Id, t.Date); SpendingLearning.SetAdjustment(f.Data, habit.Id, Today.AddDays(2), 0);
            Validation.Validate(f.Data);
            Categorization.DeleteCategory(f.Data, f.Food.Id); Validation.Validate(f.Data);
            Equal(0, f.Data.SpendingHabits.Count); Equal(0, f.Data.SpendingAdjustments.Count);
            HouseholdEdits.UndoImport(f.Data, f.Data.ImportBatches.Single().Id);
            Equal(0, f.Data.PurchaseDateDecisions.Count); Validation.Validate(f.Data);
            f = new History(); t = f.Data.Transactions[0]; SpendingLearning.SetPurchaseDate(f.Data, t.Id, t.Date);
            HouseholdEdits.SaveTransaction(f.Data, t with { Description = "Corrected original text" }); Equal(0, f.Data.PurchaseDateDecisions.Count);
        });
        yield return ("Schema 16 upgrade keeps bank history and creates a single pre-upgrade backup", () => InDirectory(directory =>
        {
            var f = new History(); var before = JsonSerializer.Serialize(f.Data.Transactions); var path = Path.Combine(directory, "legacy.db");
            new SqliteHouseholdStore(path).Save(f.Data);
            Sql(path, RecognitionTests.RemoveSchema17Tables + "PRAGMA user_version=16;");
            var store = new SqliteHouseholdStore(path, allowCreate: false); var loaded = store.Load();
            Equal(before, JsonSerializer.Serialize(loaded.Transactions)); Equal(0, loaded.SpendingHabits.Count);
            Equal(17L, Scalar(path, "PRAGMA user_version"));
            var backup = Directory.GetFiles(directory, "*.voor-versie-17-*.db").Single(); Equal(16L, Scalar(backup, "PRAGMA user_version"));
            _ = new SqliteHouseholdStore(path, allowCreate: false); Equal(1, Directory.GetFiles(directory, "*.voor-versie-17-*.db").Length);
        }));
        yield return ("Database backup and restore retain learning settings and older restores reset them safely", () => InDirectory(directory =>
        {
            var f = new History(); var habit = f.Accept(); var t = f.Data.Transactions[0];
            SpendingLearning.SetPurchaseDate(f.Data, t.Id, t.Date); SpendingLearning.SetAdjustment(f.Data, habit.Id, Today.AddDays(2), 12.34m);
            var store = new SqliteHouseholdStore(Path.Combine(directory, "source.db")); store.Save(f.Data);
            var backup = Path.Combine(directory, "backup.db"); store.Backup(backup);
            var target = new SqliteHouseholdStore(Path.Combine(directory, "target.db")); target.Restore(backup);
            var loaded = target.Load(); Equal(habit, loaded.SpendingHabits.Single()); Equal(f.Data.PurchaseDateDecisions.Single(), loaded.PurchaseDateDecisions.Single());
            Equal(f.Data.SpendingAdjustments.Single(), loaded.SpendingAdjustments.Single());
            var oldPath = Path.Combine(directory, "old.db"); new SqliteHouseholdStore(oldPath).Save(new History().Data);
            Sql(oldPath, RecognitionTests.RemoveSchema17Tables + "PRAGMA user_version=16;"); target.Restore(oldPath);
            Equal(0, target.Load().SpendingHabits.Count); Equal(0, target.Load().PurchaseDateDecisions.Count);
        }));
        yield return ("Learning settings retain coordinator conflict protection", () => InDirectory(directory =>
        {
            using var coordinator = new AdministrationCoordinator(directory, initialize: true);
            var f = new History(); var initial = coordinator.Read();
            var current = coordinator.Update(initial.Revision, d => { d.Owners = f.Data.Owners; d.Accounts = f.Data.Accounts; d.Categories = f.Data.Categories; d.Transactions = f.Data.Transactions; d.ImportBatches = f.Data.ImportBatches; });
            var habit = new SpendingHabit { AccountId = f.Account.Id, CategoryId = f.Food.Id };
            coordinator.Update(current.Revision, d => SpendingLearning.SaveHabit(d, habit));
            Throws(() => coordinator.Update(current.Revision, d => SpendingLearning.SaveHabit(d, habit with { Enabled = false })));
            True(coordinator.Read().Data.SpendingHabits.Single().Enabled);
        }));
        yield return ("Calendar and learning render labelled controls, evidence, estimates and warnings in both languages", () => WithCultures(() =>
        {
            var now = DateOnly.FromDateTime(DateTime.Today); var f = new History(now); f.Accept();
            var state = State(f.Data); var forecast = CashFlow.Project(f.Data, now, minimumDays: 90).Accounts.Single();
            var html = Render<CashCalendar>(state, new Dictionary<string, object?> { [nameof(CashCalendar.Forecast)] = forecast });
            True(WebUtility.HtmlDecode(html).Contains(UiCulture.Text("CalendarTitle")));
            Equal(42, Regex.Matches(html, "class=\"calendar-day ").Count);
            True(html.Contains("aria-current=\"date\"")); True(html.Contains("aria-pressed=\"true\""));
            foreach (var risk in Enum.GetValues<FinancialDayRisk>()) True(WebUtility.HtmlDecode(html).Contains(UiCulture.Text("CalendarRisk" + risk)));
            var learning = Render<SpendingHabits>(state, new Dictionary<string, object?> { [nameof(SpendingHabits.Analysis)] = forecast.Learning });
            True(WebUtility.HtmlDecode(learning).Contains(UiCulture.Text("HabitSourceDescription")));
            True(learning.Contains("type=\"date\"")); True(WebUtility.HtmlDecode(learning).Contains(UiCulture.Text("HabitPause")));
            True(!html.Contains("CalendarRiskShortfall</")); True(!learning.Contains("HabitSourceDescription</"));
        }));
        yield return ("Calendar shows only the applied daily remainder, including empty days and recorded-spending offsets", () => WithCultures(() =>
        {
            var now = DateOnly.FromDateTime(DateTime.Today); var f = new History(now); var date = now.AddDays(1);
            f.Add(date, 5, 0); f.Add(date.AddDays(2), 100, 0);
            var forecast = CashFlow.Project(f.Data, now, minimumDays: 90).Accounts.Single(); var state = State(f.Data);
            var before = JsonSerializer.Serialize(f.Data); var projection = JsonSerializer.Serialize(forecast);
            var day = forecast.Days.Single(d => d.Date == date);
            Equal(forecast.DailyUnscheduledEstimate - 5, day.VariableSpending);
            var html = RenderCalendar(state, forecast, date); var tile = DayHtml(html, date); var detail = DayDetail(html);
            True(tile.Contains(UiCulture.Text("CalendarEverydayName"))); True(tile.Contains("≈"));
            True(HasAmount(tile, -day.VariableSpending));
            True(Regex.IsMatch(tile, $"<span class=\"day-balance\"[^>]*>{Regex.Escape(WebState.Money(day.ClosingBalance))}</span>"));
            True(!tile.Contains(UiCulture.Text("CalendarLearned"))); Equal(1, Regex.Matches(detail, "class=\"estimate-item\"").Count);
            True(detail.Contains(UiCulture.Text("CalendarEveryday"))); True(!detail.Contains(UiCulture.Text("CalendarNoItems")));
            // A day with no bank/scheduled entries still explains the full daily deduction.
            var empty = RenderCalendar(state, forecast, date.AddDays(1));
            True(HasAmount(DayHtml(empty, date.AddDays(1)), -forecast.DailyUnscheduledEstimate));
            True(!DayDetail(empty).Contains(UiCulture.Text("CalendarNoItems")));
            Equal(0m, forecast.Days.Single(d => d.Date == date.AddDays(2)).VariableSpending);
            True(!DayHtml(RenderCalendar(state, forecast, date.AddDays(2)), date.AddDays(2)).Contains("estimate-chip"));
            True(!DayHtml(RenderCalendar(state, forecast, now), now).Contains("estimate-chip"));
            Equal(before, JsonSerializer.Serialize(f.Data));
            Equal(projection, JsonSerializer.Serialize(CashFlow.Project(f.Data, now, minimumDays: 90).Accounts.Single()));
        }));
        yield return ("Calendar keeps learned totals and daily remainders visible beside payment overflow, with individual day details", () => WithCultures(() =>
        {
            var now = DateOnly.FromDateTime(DateTime.Today); var date = now.AddDays(1); var f = new History(now);
            var other = new Category { Name = "Synthetic transport" }; f.Data.Categories.Add(other);
            foreach (var t in f.Data.Transactions.ToList())
                f.Data.Transactions.Add(t with { Id = Guid.NewGuid(), CategoryId = other.Id, Amount = t.Amount / 2 });
            foreach (var t in f.Data.Transactions.Where(t => t.CategoryId == f.Food.Id).Where((_, i) => i % 10 == 0))
                t.Description = "SYNTHETIC UNKNOWN PURCHASE DATE";
            var weekdays = 1 << (((int)date.DayOfWeek + 6) % 7); var habit = f.Accept();
            SpendingLearning.SaveHabit(f.Data, habit with { Weekdays = weekdays, WeeklyAmount = 70 });
            SpendingLearning.SaveHabit(f.Data, new() { AccountId = f.Account.Id, CategoryId = other.Id, Weekdays = weekdays, WeeklyAmount = 35 });
            foreach (var amount in new[] { -1m, -2m, 100m })
                f.Data.RecurringPayments.Add(new() { AccountId = f.Account.Id, Name = "Synthetic scheduled " + amount,
                    Amount = amount, Frequency = PaymentFrequency.Once, StartDate = date, DayOfMonth = date.Day });
            var forecast = CashFlow.Project(f.Data, now, minimumDays: 90).Accounts.Single();
            var expected = forecast.ExpectedSpending.Where(e => !e.IsPending && e.Date == date).ToList(); Equal(2, expected.Count);
            var learned = expected.Sum(e => e.Amount); var day = forecast.Days.Single(d => d.Date == date);
            var residual = day.VariableSpending - learned; True(residual > 0);
            var html = RenderCalendar(State(f.Data), forecast, date); var tile = DayHtml(html, date); var detail = DayDetail(html);
            Equal(2, Regex.Matches(tile, "class=\"day-chip estimate-chip\"").Count);
            True(tile.Contains(UiCulture.Text("CalendarMore", 1)));
            True(tile.IndexOf("class=\"day-estimates\"", StringComparison.Ordinal) > tile.IndexOf("class=\"day-more\"", StringComparison.Ordinal));
            True(tile.Contains(UiCulture.Text("CalendarLearnedGroup"))); True(tile.Contains("<svg")); True(tile.Contains("≈"));
            True(HasAmount(tile, -learned)); True(HasAmount(tile, -residual));
            True(tile.Contains($"{f.Food.Name}: {WebState.Money(-70)}")); True(tile.Contains($"{other.Name}: {WebState.Money(-35)}"));
            True(tile.Contains(UiCulture.Text("CalendarLearned"))); True(tile.Contains(UiCulture.Text("CalendarEveryday")));
            True(html.Contains($"aria-label=\"{UiCulture.Text("CalendarEstimateLegend")}\""));
            Equal(3, Regex.Matches(detail, "class=\"estimate-item\"").Count);
            foreach (var estimate in expected) True(detail.Contains(WebState.Money(-estimate.Amount)));
            True(detail.Contains(WebState.Money(-residual))); True(detail.Contains("<svg")); True(detail.Contains("≈"));
        }));
        yield return ("Displaying the daily remainder leaves payment-count risks and reasons unchanged", () => WithCultures(() =>
        {
            var now = DateOnly.FromDateTime(DateTime.Today); var date = now.AddDays(1); var f = new History(now);
            foreach (var amount in new[] { -1m, -2m })
                f.Data.RecurringPayments.Add(new() { AccountId = f.Account.Id, Name = "Synthetic bill " + amount,
                    Amount = amount, Frequency = PaymentFrequency.Once, StartDate = date, DayOfMonth = date.Day });
            var forecast = CashFlow.Project(f.Data, now, minimumDays: 90).Accounts.Single();
            var day = forecast.Days.Single(d => d.Date == date);
            Equal(FinancialDayRisk.Comfortable, FinancialCalendar.Risk(day, f.Account, forecast.DailyVariableEstimate, 2));
            var html = RenderCalendar(State(f.Data), forecast, date);
            True(DayHtml(html, date).Contains("calendar-day risk-green"));
            True(DayDetail(html).Contains(UiCulture.Text("CalendarReasonComfortable")));
            True(!DayDetail(html).Contains(UiCulture.Text("CalendarReasonMultiple")));
            // Learned dated occurrences still count, unlike the generic daily remainder.
            var habit = f.Accept(); SpendingLearning.SaveHabit(f.Data, habit with { Weekdays = 1 << (((int)date.DayOfWeek + 6) % 7), WeeklyAmount = 1 });
            forecast = CashFlow.Project(f.Data, now, minimumDays: 90).Accounts.Single();
            html = RenderCalendar(State(f.Data), forecast, date);
            True(DayHtml(html, date).Contains("calendar-day risk-orange"));
            True(DayDetail(html).Contains(UiCulture.Text("CalendarReasonMultiple")));
        }));
        yield return ("Recorded purchases and skipped learned occurrences remove their forecast chips without a guessed remainder", () => WithCultures(() =>
        {
            var now = DateOnly.FromDateTime(DateTime.Today); var date = now.AddDays(1); var f = new History(now); var habit = f.Accept();
            SpendingLearning.SaveHabit(f.Data, habit with { Weekdays = 1 << (((int)date.DayOfWeek + 6) % 7), WeeklyAmount = 70 });
            f.Add(date, 10, 2); SpendingLearning.SetAdjustment(f.Data, habit.Id, date.AddDays(7), 0);
            var forecast = CashFlow.Project(f.Data, now, minimumDays: 90).Accounts.Single();
            var html = RenderCalendar(State(f.Data), forecast, date); var tile = DayHtml(html, date);
            True(HasAmount(tile, -10)); True(!tile.Contains("estimate-chip"));
            True(!DayDetail(html).Contains("estimate-item"));
            var skipped = RenderCalendar(State(f.Data), forecast, date.AddDays(7));
            True(!DayHtml(skipped, date.AddDays(7)).Contains("estimate-chip"));
            True(DayDetail(skipped).Contains(UiCulture.Text("CalendarNoItems")));
        }));
        yield return ("Past days never show forecast chips and pending reserves stay separate from tomorrow's entries", () => WithCultures(() =>
        {
            var now = DateOnly.FromDateTime(DateTime.Today); var f = new History(now); var habit = f.Accept();
            SpendingLearning.SaveHabit(f.Data, habit with { Weekdays = 127, WeeklyAmount = 70 });
            f.Data.Transactions.RemoveAll(t => SpendingLearning.PurchaseDate(f.Data, t).Date >= now.AddDays(-6));
            var forecast = CashFlow.Project(f.Data, now, minimumDays: 90).Accounts.Single(); True(forecast.PendingHabitReserve > 0);
            var html = RenderCalendar(State(f.Data), forecast, now);
            foreach (var past in new[] { now, now.AddDays(-1) })
            {
                var pastHtml = RenderCalendar(State(f.Data), forecast, past);
                True(!DayHtml(pastHtml, past).Contains("estimate-chip")); True(!DayDetail(pastHtml).Contains("estimate-item"));
            }
            True(html.Contains(UiCulture.Text("CalendarPendingReserve", WebState.Money(forecast.PendingHabitReserve))));
            var date = now.AddDays(1); var future = DayHtml(RenderCalendar(State(f.Data), forecast, date), date);
            var applied = forecast.ExpectedSpending.Single(e => !e.IsPending && e.Date == date).Amount;
            True(HasAmount(future, -applied));
            True(!future.Contains(UiCulture.Text("CalendarPendingReserve", WebState.Money(forecast.PendingHabitReserve))));
        }));
        yield return ("The separate calendar page owns the calendar and learning while outlook keeps spending checks in both languages", () => WithCultures(() =>
        {
            var f = new History(DateOnly.FromDateTime(DateTime.Today)); f.Accept(); var state = State(f.Data);
            True(Attribute.GetCustomAttributes(typeof(CalendarPage), typeof(RouteAttribute)).Cast<RouteAttribute>().Any(a => a.Template == "/kalender"));
            True(Attribute.IsDefined(typeof(CalendarPage), typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute)));
            var calendar = WebUtility.HtmlDecode(Render<CalendarPage>(state));
            True(calendar.Contains($"<h1>{UiCulture.Text("CalendarTitle")}</h1>"));
            True(calendar.Contains("class=\"calendar-grid\"")); True(calendar.Contains("id=\"spending-habits-title\""));
            True(calendar.Contains("href=\"vooruitblik\"")); True(calendar.Contains(f.Account.Name));
            True(!calendar.Contains(UiCulture.Text("PurchaseAmountQuestion")));
            var outlook = WebUtility.HtmlDecode(Render<Outlook>(state));
            True(!outlook.Contains("class=\"calendar-grid\"")); True(!outlook.Contains("id=\"spending-habits-title\""));
            True(outlook.Contains("href=\"kalender\"")); True(outlook.Contains(UiCulture.Text("PurchaseAmountQuestion")));
            True(outlook.Contains(UiCulture.Text("DailyBalanceForecast")));
        }));
        yield return ("The calendar page handles empty accounts and limits account choices and learning to the owner filter", () => WithCultures(() =>
        {
            var empty = WebUtility.HtmlDecode(Render<CalendarPage>(State(HouseholdData.CreateEmpty())));
            True(empty.Contains(UiCulture.Text("AddCurrentAccountNote"))); True(!empty.Contains("class=\"calendar-grid\""));
            var f = new History(DateOnly.FromDateTime(DateTime.Today)); f.Accept();
            var owner = new Owner { Name = "Other synthetic owner" }; f.Data.Owners.Add(owner);
            var other = f.Account with { Id = Guid.NewGuid(), OwnerIds = [owner.Id], Name = "Other synthetic bank" }; f.Data.Accounts.Add(other);
            var state = State(f.Data);
            var all = WebUtility.HtmlDecode(Render<CalendarPage>(state)); True(all.Contains(f.Account.Name)); True(all.Contains(other.Name));
            state.OwnerId = owner.Id;
            var scoped = WebUtility.HtmlDecode(Render<CalendarPage>(state));
            True(scoped.Contains(other.Name)); True(!scoped.Contains(f.Account.Name)); True(!scoped.Contains("SYNTHETIC SHOP"));
            True(scoped.Contains(UiCulture.Text("HabitNoPatterns"))); True(!scoped.Contains(UiCulture.Text("HabitPause")));
            state.OwnerId = Guid.NewGuid();
            True(WebUtility.HtmlDecode(Render<CalendarPage>(state)).Contains(UiCulture.Text("AddCurrentAccountNote")));
        }));
        yield return ("Calendar navigation is active and expanded under Overviews rather than Planning in both languages", () => WithCultures(() =>
        {
            var html = WebUtility.HtmlDecode(Render<MainNavigation>(State(HouseholdData.CreateEmpty())));
            var overviews = Regex.Match(html, "<ul id=\"nav-overviews\"(?<attributes>[^>]*)>(?<entries>.*?)</ul>", RegexOptions.Singleline);
            True(overviews.Success); True(!overviews.Groups["attributes"].Value.Contains("hidden"));
            var entries = overviews.Groups["entries"].Value;
            True(entries.Contains("href=\"kalender\"")); True(entries.Contains(UiCulture.Text("CalendarTitle")));
            True(Regex.IsMatch(entries, "<a(?=[^>]*href=\"kalender\")(?=[^>]*aria-current=\"page\")[^>]*>"));
            var planning = Regex.Match(html, "<ul id=\"nav-planning\"[^>]*>(?<entries>.*?)</ul>", RegexOptions.Singleline);
            True(planning.Success); True(!planning.Groups["entries"].Value.Contains("href=\"kalender\""));
            True(planning.Groups["entries"].Value.Contains("href=\"vooruitblik\""));
        }));
    }

    private sealed class History
    {
        public static readonly DateOnly Start = new(2026, 8, 17);
        public HouseholdData Data { get; } = HouseholdData.CreateEmpty();
        public Account Account { get; }
        public Category Food { get; }
        private readonly DateOnly today, start;
        public History(DateOnly? asOf = null)
        {
            today = asOf ?? Today;
            // Thirteen shopping weeks, ending with the most recent complete bank-posted weekend.
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            start = monday.AddDays(-91);
            var owner = new Owner { Name = "Synthetic household" }; Data.Owners.Add(owner);
            Account = new() { Name = "Synthetic current account", OwnerIds = [owner.Id], OpeningDate = start.AddDays(-7), OpeningBalance = 10000, MinimumCashBuffer = 100 };
            Data.Accounts.Add(Account); Food = Data.Categories.Single(c => c.Name == "Voeding");
            var batch = new ImportBatch { AccountId = Account.Id, ImportedAt = today.ToDateTime(TimeOnly.MinValue), CoverageFrom = start, CoverageThrough = today };
            Data.ImportBatches.Add(batch);
            for (var week = 0; week < 13; week++)
            {
                Add(start.AddDays(7 * week + 2), 25, 0);
                Add(start.AddDays(7 * week + 2), 25, 0);
                Add(start.AddDays(7 * week + 5), 100, 2);
            }
            foreach (var t in Data.Transactions) t.ImportBatchId = batch.Id;
            batch.RowCount = Data.Transactions.Count;
        }
        public Transaction Add(DateOnly purchase, decimal amount, int lag)
        {
            var t = new Transaction { AccountId = Account.Id, Date = purchase.AddDays(lag), Amount = -amount,
                CategoryId = Food.Id, Description = $"SYNTHETIC SHOP {purchase:dd-MM-yyyy} 12:49 CITY" };
            Data.Transactions.Add(t); return t;
        }
        public SpendingHabit Accept(bool enabled = true)
        {
            var h = new SpendingHabit { AccountId = Account.Id, CategoryId = Food.Id, Enabled = enabled };
            SpendingLearning.SaveHabit(Data, h); return h;
        }
        public AccountCashFlow Forecast() => CashFlow.Project(Data, today).Accounts.Single();
        public SpendingPattern Pattern() => Forecast().Learning.Patterns.Single();
    }
    private static WebState State(HouseholdData data)
    {
        var state = new WebState(null!); typeof(WebState).GetProperty(nameof(WebState.Data))!.SetValue(state, data); return state;
    }
    private static string RenderCalendar(WebState state, AccountCashFlow forecast, DateOnly date) =>
        WebUtility.HtmlDecode(Render<CalendarAtDate>(state, new Dictionary<string, object?> { [nameof(CashCalendar.Forecast)] = forecast, [nameof(CalendarAtDate.Date)] = date }));
    private static string DayHtml(string html, DateOnly date) => Regex.Matches(html, "<button[^>]*class=\"calendar-day [^>]*>.*?</button>", RegexOptions.Singleline)
        .Cast<Match>().Single(m => m.Value.Contains($"datetime=\"{date:yyyy-MM-dd}\"")).Value;
    private static string DayDetail(string html) => Regex.Match(html, "<section class=\"calendar-detail\".*?</section>", RegexOptions.Singleline).Value;
    private static bool HasAmount(string html, decimal amount) => Regex.IsMatch(html, $"<b\\b[^>]*>{Regex.Escape(WebState.Money(amount))}</b>");
    // Static HTML rendering has no click dispatcher. Select a day after normal setup,
    // exercising the production grid/details without exposing test-only UI parameters.
    private sealed class CalendarAtDate : CashCalendar
    {
        [Parameter] public DateOnly Date { get; set; }
        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            typeof(CashCalendar).GetField("month", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(this, new DateOnly(Date.Year, Date.Month, 1));
            typeof(CashCalendar).GetField("selectedDate", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(this, Date);
        }
    }
    private static string Render<T>(WebState state, IDictionary<string, object?>? parameters = null) where T : IComponent
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddLocalization(); services.AddSingleton(state); services.AddSingleton<IJSRuntime>(new NoJs());
        services.AddSingleton<NavigationManager>(new CalendarNavigation()); services.AddSingleton<UiPreferences>();
        using var provider = services.BuildServiceProvider(); return Run().GetAwaiter().GetResult();
        async Task<string> Run()
        {
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()))).ToHtmlString());
        }
    }
    private sealed class CalendarNavigation : NavigationManager
    {
        public CalendarNavigation() => Initialize("http://localhost/", "http://localhost/kalender");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
    private static void Sql(string path, string sql)
    { using var c = new SqliteConnection($"Data Source={path};Pooling=False"); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    private static long Scalar(string path, string sql)
    { using var c = new SqliteConnection($"Data Source={path};Pooling=False"); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); }
    private static void InDirectory(Action<string> run)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", "calendar-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try { run(directory); } finally { Directory.Delete(directory, recursive: true); }
    }
    private static void WithCultures(Action run)
    {
        var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture;
        try { foreach (var name in new[] { "nl-NL", "en-GB" }) { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name); run(); } }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }
    }
    private static void Near(decimal expected, decimal actual, decimal tolerance) { if (Math.Abs(expected - actual) > tolerance) throw new Exception($"Expected approximately {expected}, got {actual}."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
    private static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected validation/conflict failure."); }
}
