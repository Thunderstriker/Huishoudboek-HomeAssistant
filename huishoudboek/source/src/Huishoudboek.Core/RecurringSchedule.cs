using System.Text;
using System.Text.RegularExpressions;

namespace Huishoudboek.Core;

public enum RecurringPaymentStatus { Expected, AwaitingBankData, Recognized, NeedsReview, NotFound }

public sealed record RecurringCandidate(RecurringPayment Payment, Transaction Transaction, DateOnly DueDate, bool RequiresReview = false)
{
    public decimal Difference => Transaction.Amount - Payment.Amount;
}

/// <summary>Occurrence-specific links replace estimates, never the original bank amount or date.</summary>
public static class RecurringSchedule
{
    public static DateOnly? Occurrence(RecurringPayment item, int year, int month)
    {
        if (item.Frequency == PaymentFrequency.Once)
            return item.StartDate.Year == year && item.StartDate.Month == month ? item.StartDate : null;
        var offset = (year - item.StartDate.Year) * 12 + month - item.StartDate.Month;
        var interval = item.Frequency switch { PaymentFrequency.Quarterly => 3, PaymentFrequency.Yearly => 12, _ => 1 };
        if (offset < 0 || offset % interval != 0) return null;
        var date = new DateOnly(year, month, Math.Min(item.DayOfMonth, DateTime.DaysInMonth(year, month)));
        return date < item.StartDate || item.EndDate is { } end && date > end ? null : date;
    }

    public static IEnumerable<DateOnly> Occurrences(RecurringPayment item, DateOnly from, DateOnly through)
    {
        if (through < from) yield break;
        var month = new DateOnly(from.Year, from.Month, 1);
        var lastMonth = new DateOnly(through.Year, through.Month, 1);
        while (month <= lastMonth)
        {
            if (Occurrence(item, month.Year, month.Month) is { } date && date >= from && date <= through) yield return date;
            if (month == lastMonth) break;
            month = month.AddMonths(1);
        }
    }

    public static DateOnly LinkedDate(RecurringPayment item, Transaction transaction) =>
        transaction.RecurringOccurrenceDate ?? Occurrence(item, transaction.Date.Year, transaction.Date.Month) ??
        // Old databases linked by bank month, even if the schedule was subsequently edited.
        new DateOnly(transaction.Date.Year, transaction.Date.Month,
            Math.Min(item.DayOfMonth, DateTime.DaysInMonth(transaction.Date.Year, transaction.Date.Month)));

    public static bool IsLinked(HouseholdData data, RecurringPayment item, DateOnly due) =>
        data.Transactions.Any(t => t.RecurringPaymentId == item.Id && LinkedDate(item, t) == due);

    public static DateOnly? LatestDue(RecurringPayment item, DateOnly today) =>
        Occurrences(item, today.AddMonths(-12), today).Select(d => (DateOnly?)d).LastOrDefault() ??
        (item.Frequency == PaymentFrequency.Once && item.StartDate <= today ? item.StartDate : null);

    public static RecurringPaymentStatus Status(HouseholdData data, RecurringPayment item, DateOnly due, DateOnly today,
        IReadOnlyList<RecurringCandidate>? candidates = null)
    {
        if (IsLinked(data, item, due)) return RecurringPaymentStatus.Recognized;
        if ((candidates ?? Candidates(data, today)).Any(c => c.Payment.Id == item.Id && c.DueDate == due))
            return RecurringPaymentStatus.NeedsReview;
        if (due > today) return RecurringPaymentStatus.Expected;
        var account = data.Accounts.Single(a => a.Id == item.AccountId);
        var from = due.AddDays(-item.MatchWindowDays);
        if (from < account.OpeningDate) from = account.OpeningDate;
        var through = due.AddDays(item.MatchWindowDays);
        return through <= today && BankImportCoverage.Covers(data, item.AccountId, from, through)
            ? RecurringPaymentStatus.NotFound : RecurringPaymentStatus.AwaitingBankData;
    }

    // Keep digits and words intact: changing references are not safe to silently strip.
    private static string Normalize(string text) => string.Join(" ", Regex.Matches(text.Normalize(NormalizationForm.FormKC),
        @"[\p{L}\p{N}]+").Select(m => m.Value)).ToUpperInvariant();

    private static bool Recognizes(string description, string text) => !string.IsNullOrWhiteSpace(text) &&
        (description.Contains(text, StringComparison.OrdinalIgnoreCase) ||
         Normalize(text) is { Length: > 0 } normalized && (" " + Normalize(description) + " ").Contains(" " + normalized + " ", StringComparison.Ordinal));

    public static IReadOnlyList<RecurringCandidate> Candidates(HouseholdData data, DateOnly today)
    {
        if (data.RecurringPayments.Count == 0) return [];
        var transfers = TransferMatching.ConfirmedTransactionIds(data);
        var known = KnownDescriptions(data);
        var linked = LinkedOccurrences(data);
        return data.Transactions.Where(t => t.Date <= today && t.Kind != TransactionKind.Transfer &&
                t.RecurringPaymentId == null && !transfers.Contains(t.Id))
            .SelectMany(t => Matches(data, t, linked, known)).OrderByDescending(c => c.Transaction.Date)
            .ThenBy(c => Math.Abs(c.Difference)).ToList();
    }

    public static IEnumerable<RecurringCandidate> Matches(HouseholdData data, Transaction transaction)
        => Matches(data, transaction, null, KnownDescriptions(data));

    private static Dictionary<Guid, HashSet<string>> KnownDescriptions(HouseholdData data) => data.Transactions
        .Where(t => t.RecurringPaymentId != null).GroupBy(t => t.RecurringPaymentId!.Value)
        .ToDictionary(g => g.Key, g => g.Select(t => Normalize(t.Description)).ToHashSet());

    private static HashSet<(Guid PaymentId, DateOnly Due)> LinkedOccurrences(HouseholdData data)
    {
        var payments = data.RecurringPayments.ToDictionary(r => r.Id);
        return data.Transactions.Where(t => t.RecurringPaymentId != null)
            .Select(t => (t.RecurringPaymentId!.Value, LinkedDate(payments[t.RecurringPaymentId.Value], t))).ToHashSet();
    }

    private static IEnumerable<RecurringCandidate> Matches(HouseholdData data, Transaction transaction,
        IReadOnlySet<(Guid PaymentId, DateOnly Due)>? linked, IReadOnlyDictionary<Guid, HashSet<string>> known)
    {
        var description = Normalize(transaction.Description);
        foreach (var item in data.RecurringPayments.Where(r => r.AccountId == transaction.AccountId &&
            CompatiblePurpose(data, transaction, r) &&
            Math.Sign(r.Amount) == Math.Sign(transaction.Amount) &&
            (Recognizes(transaction.Description, r.MatchText) || description.Length > 0 && known.TryGetValue(r.Id, out var examples) && examples.Contains(description))))
        {
            var review = item.AmountMode == PaymentAmountMode.Fixed && Math.Abs(item.Amount - transaction.Amount) > item.MatchTolerance;
            var dates = Occurrences(item, transaction.Date.AddDays(-item.MatchWindowDays), transaction.Date.AddDays(item.MatchWindowDays)).ToList();
            if (dates.Count == 0)
            {
                // Nearby late/early bookings are useful suggestions, never permission to auto-link outside the configured window.
                var suggestionWindow = Math.Min(31, item.MatchWindowDays + 7);
                dates = Occurrences(item, transaction.Date.AddDays(-suggestionWindow), transaction.Date.AddDays(suggestionWindow)).ToList();
                review = true;
            }
            foreach (var due in dates)
                if (!(linked?.Contains((item.Id, due)) ?? IsLinked(data, item, due))) yield return new(item, transaction, due, review);
        }
    }

    private static bool CompatiblePurpose(HouseholdData data, Transaction transaction, RecurringPayment payment) =>
        transaction.CategoryId == null ||
        (data.Categories.FirstOrDefault(c => c.Id == transaction.CategoryId)?.Purpose ?? CategoryPurpose.Normal) ==
        (data.Categories.FirstOrDefault(c => c.Id == payment.CategoryId)?.Purpose ?? CategoryPurpose.Normal);

    public static IReadOnlyList<RecurringCandidate> BatchCandidates(HouseholdData data, Guid paymentId, DateOnly through)
    {
        var payment = data.RecurringPayments.Single(r => r.Id == paymentId);
        return UnambiguousCandidates(data, data.Transactions.Where(t => t.AccountId == payment.AccountId && t.Date <= through))
            .Where(c => c.Payment.Id == paymentId).OrderBy(c => c.Transaction.Date).ToList();
    }

    public static IReadOnlyList<RecurringCandidate> BatchForReview(HouseholdData data, DateOnly today) =>
        UnambiguousCandidates(data, data.Transactions.Where(t => t.Date <= today));

    public static IReadOnlyList<RecurringCandidate> UnambiguousCandidates(HouseholdData data, IEnumerable<Transaction> transactions)
    {
        if (data.RecurringPayments.Count == 0) return [];
        var transfers = TransferMatching.ConfirmedTransactionIds(data);
        var known = KnownDescriptions(data);
        var linked = LinkedOccurrences(data);
        // Snapshot both sides before linking: configured margins never authorize choosing
        // an arbitrary entry for an occurrence, or a plan for an entry.
        var candidates = transactions.Where(t => t.Kind != TransactionKind.Transfer &&
                t.RecurringPaymentId == null && !transfers.Contains(t.Id))
            .SelectMany(t => Matches(data, t, linked, known)).ToList();
        var byTransaction = candidates.ToLookup(c => c.Transaction.Id);
        var byOccurrence = candidates.ToLookup(c => (c.Payment.Id, c.DueDate));
        return candidates.Where(c => !c.RequiresReview && byTransaction[c.Transaction.Id].Count() == 1 &&
            byOccurrence[(c.Payment.Id, c.DueDate)].Count() == 1 &&
            !TransactionAccounting.HasDetails(data, c.Transaction.Id)).ToList();
    }

    public static void LinkBatch(HouseholdData data, IEnumerable<(Guid TransactionId, Guid PaymentId, DateOnly Due)> selections)
    {
        var rows = selections.ToList();
        if (rows.Select(r => r.TransactionId).Distinct().Count() != rows.Count ||
            rows.Select(r => (r.PaymentId, r.Due)).Distinct().Count() != rows.Count)
            throw new InvalidOperationException("Selecteer per boeking en geplande betaling hoogstens één koppeling.");
        // Validate every selection before applying any link, including direct callers outside the coordinator.
        foreach (var row in rows) ValidateLink(data, row.TransactionId, row.PaymentId, row.Due);
        foreach (var row in rows) Link(data, row.TransactionId, row.PaymentId, row.Due);
    }

    public static void Link(HouseholdData data, Guid transactionId, Guid paymentId, DateOnly due)
    {
        ValidateLink(data, transactionId, paymentId, due);
        var transaction = data.Transactions.Single(t => t.Id == transactionId);
        var item = data.RecurringPayments.Single(r => r.Id == paymentId);
        HouseholdEdits.SaveTransaction(data, transaction with { RecurringPaymentId = item.Id, RecurringOccurrenceDate = due,
            Kind = item.IsMortgage ? TransactionKind.Mortgage : transaction.Kind,
            // Ordinary historical links preserve even an empty category. A savings/
            // own-account plan must carry its explicit reporting purpose onto the receipt.
            CategoryId = transaction.CategoryId ?? (data.Categories.Any(c => c.Id == item.CategoryId && c.Purpose != CategoryPurpose.Normal)
                ? item.CategoryId : null) });
    }

    private static void ValidateLink(HouseholdData data, Guid transactionId, Guid paymentId, DateOnly due)
    {
        var transaction = data.Transactions.Single(t => t.Id == transactionId);
        var item = data.RecurringPayments.Single(r => r.Id == paymentId);
        if (transaction.Kind == TransactionKind.Transfer || TransferMatching.ConfirmedTransactionIds(data).Contains(transactionId) ||
            transaction.RecurringPaymentId != null || transaction.AccountId != item.AccountId || Math.Sign(transaction.Amount) != Math.Sign(item.Amount) ||
            !CompatiblePurpose(data, transaction, item))
            throw new InvalidOperationException("Deze boeking kan niet aan deze vaste post worden gekoppeld. Verwijder eerst een bestaande koppeling.");
        if (Occurrence(item, due.Year, due.Month) != due || IsLinked(data, item, due))
            throw new InvalidOperationException("Deze geplande betaling bestaat niet of is al gekoppeld.");
        if (item.IsMortgage && transaction.Kind != TransactionKind.Mortgage && TransactionAccounting.HasDetails(data, transactionId))
            throw new InvalidOperationException("Verwijder eerst de expliciete transactieverwerking vóór het wijzigen van het type.");
    }

    public static void Unlink(HouseholdData data, Guid transactionId)
    {
        var transaction = data.Transactions.Single(t => t.Id == transactionId);
        HouseholdEdits.SaveTransaction(data, transaction with { RecurringPaymentId = null, RecurringOccurrenceDate = null });
    }
}
