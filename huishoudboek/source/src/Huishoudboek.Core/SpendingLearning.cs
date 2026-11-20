using System.Globalization;
using System.Text.RegularExpressions;

namespace Huishoudboek.Core;

public sealed record SpendingHabit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid CategoryId { get; set; }
    public bool Enabled { get; set; } = true;
    // Zero follows the learned distribution; otherwise bit 0 = Monday, bit 6 = Sunday.
    public int Weekdays { get; set; }
    public decimal? WeeklyAmount { get; set; }
}

public sealed record PurchaseDateDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    // Null explicitly excludes an inferred date; removing the decision restores inference.
    public DateOnly? PurchaseDate { get; set; }
}

public sealed record SpendingAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HabitId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
}

public enum PurchaseDateSource { Unknown, Description, Receipt, Manual, Excluded }
public sealed record PurchaseDateEvidence(DateOnly? Date, PurchaseDateSource Source);
public sealed record SpendingEvidence(Transaction Transaction, decimal Amount, PurchaseDateEvidence Purchase);
public sealed record WeekdaySpending(DayOfWeek Day, int ActiveWeeks, int ObservedWeeks,
    decimal AverageWhenActive, decimal ExpectedAmount);
public sealed record SpendingPattern(Guid AccountId, Guid CategoryId, SpendingHabit? Preference,
    int CoveredWeeks, DateOnly? From, DateOnly? Through, decimal DatedShare, bool Ready, bool Strong,
    decimal BaselineDaily, IReadOnlyList<WeekdaySpending> Weekdays, IReadOnlyList<SpendingEvidence> Evidence)
{
    public IReadOnlyList<DayOfWeek> DetectedWeekdays { get; init; } = [];
    public int RequiredActiveWeeks { get; init; }
    public bool Active => Ready && Preference?.Enabled == true;
    public decimal WeeklyAmount => Weekdays.Sum(w => w.ExpectedAmount);
    public decimal AmountOn(DateOnly date) => Weekdays.Single(w => w.Day == date.DayOfWeek).ExpectedAmount;
}
public sealed record SpendingAnalysis(int CoveredWeeks, IReadOnlyList<SpendingPattern> Patterns);
public sealed record ExpectedSpending(Guid HabitId, Guid CategoryId, DateOnly Date, decimal Amount, bool IsPending = false);

/// <summary>Explainable, local weekday learning. Only accepted patterns change cash planning.</summary>
public static partial class SpendingLearning
{
    public const int HistoryWeeks = 12;
    public const int MinimumWeeks = 6;
    public const int BookingWindowDays = 7;
    public const decimal MinimumDatedShare = .7m;

    [GeneratedRegex(@"(?<!\d)(?<date>\d{2}[-/.]\d{2}[-/.]\d{4})\s+(?<hour>\d{2}):(?<minute>\d{2})(?::(?<second>\d{2}))?(?![\d:])", RegexOptions.CultureInvariant)]
    private static partial Regex Timestamp();
    [GeneratedRegex(@"\b(invoice|factuur|vervaldatum|due\s+date)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonPurchaseDate();

    public static PurchaseDateEvidence DateFromDescription(Transaction transaction)
    {
        if (NonPurchaseDate().IsMatch(transaction.Description)) return new(null, PurchaseDateSource.Unknown);
        var matches = Timestamp().Matches(transaction.Description);
        if (matches.Count == 0) return new(null, PurchaseDateSource.Unknown);
        var dates = new HashSet<DateOnly>();
        foreach (Match match in matches)
        {
            if (!DateOnly.TryParseExact(match.Groups["date"].Value, ["dd-MM-yyyy", "dd/MM/yyyy", "dd.MM.yyyy"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture) > 23 ||
                int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture) > 59 ||
                match.Groups["second"].Success && int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture) > 59 ||
                date > transaction.Date || transaction.Date.DayNumber - date.DayNumber > BookingWindowDays)
                return new(null, PurchaseDateSource.Unknown);
            dates.Add(date);
        }
        return dates.Count == 1 ? new(dates.Single(), PurchaseDateSource.Description) : new(null, PurchaseDateSource.Unknown);
    }

    public static PurchaseDateEvidence PurchaseDate(HouseholdData data, Transaction transaction)
    {
        if (data.PurchaseDateDecisions.FirstOrDefault(d => d.TransactionId == transaction.Id) is { } decision)
            return new(decision.PurchaseDate, decision.PurchaseDate == null ? PurchaseDateSource.Excluded : PurchaseDateSource.Manual);
        var receipts = data.PurchaseReceipts.Where(r => r.TransactionId == transaction.Id && r.IsReviewed && r.PurchaseDate != null)
            .Select(r => r.PurchaseDate!.Value).Distinct().ToList();
        if (receipts.Count > 0)
            return receipts.Count == 1 && receipts[0] <= transaction.Date && transaction.Date.DayNumber - receipts[0].DayNumber <= 31
                ? new(receipts[0], PurchaseDateSource.Receipt) : new(null, PurchaseDateSource.Unknown);
        return DateFromDescription(transaction);
    }

    public static IReadOnlyList<AccountingLine> VariableLines(HouseholdData data, Guid accountId)
    {
        var selected = SpendingForecast.CategoryIds(data);
        return TransactionAccounting.Lines(data, data.Transactions.Where(t => t.AccountId == accountId && t.Amount < 0 &&
            t.Kind == TransactionKind.Normal && t.RecurringPaymentId == null))
            .Where(l => l.Treatment == AllocationTreatment.Ordinary && l.TypicalSpending > 0 &&
                l.CategoryId is { } category && selected.Contains(category)).ToList();
    }

    public static SpendingAnalysis Analyze(HouseholdData data, Account account, DateOnly today,
        DateOnly historyStart, DateOnly historyEnd)
    {
        var cutoff = historyEnd < today ? historyEnd : today;
        cutoff = cutoff.AddDays(-BookingWindowDays);
        var lastSunday = cutoff.AddDays(-((int)cutoff.DayOfWeek % 7));
        var weeks = Enumerable.Range(0, HistoryWeeks).Select(i => lastSunday.AddDays(-7 * i - 6)).Reverse()
            .Where(from => from >= account.OpeningDate && BankImportCoverage.Covers(data, account.Id, from, from.AddDays(6 + BookingWindowDays)))
            .ToList();
        var firstDate = weeks.Count > 0 && weeks[0] < historyStart ? weeks[0] : historyStart;
        var lines = VariableLines(data, account.Id).Where(l => l.Transaction.Date >= firstDate && l.Transaction.Date <= historyEnd).ToList();
        var dated = lines.GroupBy(l => l.Transaction.Id).ToDictionary(g => g.Key, g => PurchaseDate(data, g.First().Transaction));
        var selected = SpendingForecast.CategoryIds(data);
        var categories = lines.Where(l => l.Transaction.Date >= historyStart && l.Transaction.Date <= historyEnd && l.CategoryId != null)
            .Select(l => l.CategoryId!.Value).Concat(data.SpendingHabits.Where(h => h.AccountId == account.Id).Select(h => h.CategoryId))
            .Distinct().Where(selected.Contains);
        var patterns = new List<SpendingPattern>();
        var observedDays = Math.Max(1, historyEnd.DayNumber - historyStart.DayNumber + 1);
        foreach (var categoryId in categories)
        {
            var category = lines.Where(l => l.CategoryId == categoryId).ToList();
            var historical = category.Where(l => l.Transaction.Date >= historyStart && l.Transaction.Date <= historyEnd).ToList();
            var knownAmount = historical.Where(l => dated[l.Transaction.Id].Date != null).Sum(l => l.TypicalSpending);
            var total = historical.Sum(l => l.TypicalSpending);
            var share = total > 0 ? knownAmount / total : 0;
            var preference = data.SpendingHabits.SingleOrDefault(h => h.AccountId == account.Id && h.CategoryId == categoryId);
            var byDate = category.Where(l => l.Transaction.Date <= historyEnd && dated[l.Transaction.Id].Date != null)
                .GroupBy(l => dated[l.Transaction.Id].Date!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.TypicalSpending));
            var active = new int[7]; var averages = new decimal[7]; var weights = new decimal[7];
            for (var day = 0; day < 7; day++)
            {
                var values = weeks.Select(w => byDate.GetValueOrDefault(w.AddDays(day))).ToList();
                active[day] = values.Count(v => v > 0);
                averages[day] = active[day] > 0 ? Round(values.Sum() / active[day]) : 0;
                var positive = values.Where(v => v > 0).Order().ToList();
                var median = positive.Count > 0 ? positive[positive.Count / 2] : 0;
                // Limit a single unusual trip's influence on weekday timing, not the cash total.
                weights[day] = values.Select((v, i) => (positive.Count >= 3 ? Math.Min(v, median * 3) : v) * (i + 1)).Sum();
            }
            // A one-off purchase is not a weekly habit. Require at least four occurrences and half the covered weeks.
            var requiredActiveWeeks = Math.Max(4, (weeks.Count + 1) / 2);
            var recurringDays = Enumerable.Range(0, 7).Where(i => active[i] >= requiredActiveWeeks).ToList();
            var detected = recurringDays.Select(i => (DayOfWeek)((i + 1) % 7)).ToList();
            var automatic = preference?.Weekdays is null or 0;
            if (automatic)
                for (var i = 0; i < 7; i++) if (!recurringDays.Contains(i)) weights[i] = 0;
            // Irregular dated purchases stay in the selected categories' unscheduled reserve, not invented weekly trips.
            var scheduledAmount = automatic ? historical.Where(l => dated[l.Transaction.Id].Date is { } date &&
                detected.Contains(date.DayOfWeek)).Sum(l => l.TypicalSpending) : knownAmount;
            var daily = scheduledAmount / observedDays;
            var ready = weeks.Count >= MinimumWeeks && share >= MinimumDatedShare && detected.Count > 0 && daily > 0;
            if (preference?.Weekdays > 0)
            {
                for (var i = 0; i < 7; i++) if ((preference.Weekdays & (1 << i)) == 0) weights[i] = 0;
                var existingWeights = weights.Where(w => w > 0).ToList();
                var fallback = existingWeights.Count > 0 ? existingWeights.Average() : 1;
                for (var i = 0; i < 7; i++)
                    if ((preference.Weekdays & (1 << i)) != 0 && weights[i] == 0) weights[i] = fallback;
            }
            var amounts = Distribute(preference?.WeeklyAmount ?? Round(daily * 7), weights);
            var weekdays = Enumerable.Range(0, 7).Select(i => new WeekdaySpending((DayOfWeek)((i + 1) % 7),
                active[i], weeks.Count, averages[i], amounts[i])).ToList();
            var evidence = historical.GroupBy(l => l.Transaction.Id).Select(g => new SpendingEvidence(g.First().Transaction,
                g.Sum(l => l.TypicalSpending), dated[g.Key])).OrderByDescending(e => e.Transaction.Date).ToList();
            var totals = weeks.Select(w => Enumerable.Range(0, 7).Sum(i => byDate.GetValueOrDefault(w.AddDays(i)))).ToList();
            var mean = totals.Count > 0 ? totals.Average() : 0;
            var stableAmounts = mean > 0 && totals.Average(v => Math.Abs(v - mean)) <= mean * .75m;
            patterns.Add(new(account.Id, categoryId, preference, weeks.Count, weeks.Count > 0 ? weeks[0] : null,
                weeks.Count > 0 ? weeks[^1].AddDays(6) : null, share, ready,
                ready && weeks.Count >= 8 && share >= .9m && active.Max() >= weeks.Count * .6m && stableAmounts,
                daily, weekdays, evidence) { DetectedWeekdays = detected, RequiredActiveWeeks = requiredActiveWeeks });
        }
        return new(weeks.Count, patterns.OrderByDescending(p => p.Active).ThenByDescending(p => p.WeeklyAmount).ToList());
    }

    private static decimal[] Distribute(decimal total, decimal[] weights)
    {
        var sum = weights.Sum(); var result = new decimal[7];
        if (sum == 0) return result;
        var exact = weights.Select(w => total * w / sum).ToArray();
        for (var i = 0; i < 7; i++) result[i] = decimal.Floor(exact[i] * 100) / 100;
        var cents = (int)decimal.Round((total - result.Sum()) * 100);
        foreach (var i in Enumerable.Range(0, 7).OrderByDescending(i => exact[i] - result[i]).Take(cents)) result[i] += .01m;
        return result;
    }

    public static IReadOnlyList<ExpectedSpending> Project(HouseholdData data, SpendingAnalysis analysis, DateOnly today, DateOnly end)
    {
        var result = new List<ExpectedSpending>();
        var selected = SpendingForecast.CategoryIds(data);
        foreach (var pattern in analysis.Patterns.Where(p => p.Active && selected.Contains(p.CategoryId)))
        {
            var habit = pattern.Preference!;
            var lines = VariableLines(data, pattern.AccountId).Where(l => l.CategoryId == pattern.CategoryId &&
                l.Transaction.Date >= today.AddDays(1 - BookingWindowDays) && l.Transaction.Date <= end.AddDays(31)).ToList();
            var evidence = lines.GroupBy(l => l.Transaction.Id).Select(g => new SpendingEvidence(g.First().Transaction,
                g.Sum(l => l.TypicalSpending), PurchaseDate(data, g.First().Transaction))).ToList();
            var unknown = evidence.Where(e => e.Purchase.Date == null && e.Transaction.Date <= today &&
                e.Transaction.Date >= today.AddDays(1 - BookingWindowDays)).ToDictionary(e => e.Transaction.Id, e => e.Amount);
            for (var date = today.AddDays(1 - BookingWindowDays); date <= end; date = date.AddDays(1))
            {
                var amount = data.SpendingAdjustments.SingleOrDefault(a => a.HabitId == habit.Id && a.Date == date)?.Amount ?? pattern.AmountOn(date);
                var pending = date <= today;
                if (pending)
                {
                    // A known shopping day is replaced by its actual spending, not an unpaid remainder.
                    if (evidence.Any(e => e.Purchase.Date == date)) continue;
                    // Unassigned posted spending offsets the aggregate reserve, without creating bank links.
                    foreach (var entry in evidence.Where(e => unknown.ContainsKey(e.Transaction.Id) && e.Transaction.Date >= date))
                    {
                        var used = Math.Min(amount, unknown[entry.Transaction.Id]);
                        amount -= used; unknown[entry.Transaction.Id] -= used;
                    }
                }
                if (amount > 0) result.Add(new(habit.Id, pattern.CategoryId, date, amount, pending));
            }
        }
        return result;
    }

    public static void SaveHabit(HouseholdData data, SpendingHabit habit)
    {
        var existing = data.SpendingHabits.SingleOrDefault(h => h.AccountId == habit.AccountId && h.CategoryId == habit.CategoryId);
        if (existing != null && existing.Id != habit.Id) throw new InvalidOperationException("Deze categorie heeft al een bestedingspatroon voor deze rekening.");
        data.SpendingHabits.RemoveAll(h => h.Id == habit.Id); data.SpendingHabits.Add(habit with { });
    }
    public static void SetPurchaseDate(HouseholdData data, Guid transactionId, DateOnly? date)
    {
        var transaction = data.Transactions.Single(t => t.Id == transactionId);
        if (date > transaction.Date || date < transaction.Date.AddDays(-31))
            throw new InvalidOperationException("Kies een aankoopdatum in de 31 dagen tot en met de bankdatum.");
        data.PurchaseDateDecisions.RemoveAll(d => d.TransactionId == transactionId);
        data.PurchaseDateDecisions.Add(new() { TransactionId = transactionId, PurchaseDate = date });
    }
    public static void SetAdjustment(HouseholdData data, Guid habitId, DateOnly date, decimal amount)
    {
        if (amount < 0 || amount > 1_000_000_000m || amount != Round(amount)) throw new InvalidOperationException("Vul een geldig niet-negatief bedrag in.");
        data.SpendingAdjustments.RemoveAll(a => a.HabitId == habitId && a.Date == date);
        data.SpendingAdjustments.Add(new() { HabitId = habitId, Date = date, Amount = amount });
    }
    public static void RemoveForCategories(HouseholdData data, Guid categoryId)
    {
        var ids = data.SpendingHabits.Where(h => h.CategoryId == categoryId).Select(h => h.Id).ToHashSet();
        data.SpendingAdjustments.RemoveAll(a => ids.Contains(a.HabitId));
        data.SpendingHabits.RemoveAll(h => ids.Contains(h.Id));
    }
    private static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
