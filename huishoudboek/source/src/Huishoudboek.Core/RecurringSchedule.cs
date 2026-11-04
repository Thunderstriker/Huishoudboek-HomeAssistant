namespace Huishoudboek.Core;

public sealed record RecurringCandidate(RecurringPayment Payment, Transaction Transaction, DateOnly DueDate)
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

    public static IReadOnlyList<RecurringCandidate> Candidates(HouseholdData data, DateOnly today)
    {
        var transfers = TransferMatching.ConfirmedTransactionIds(data);
        return data.Transactions.Where(t => t.Date <= today && t.Kind != TransactionKind.Transfer &&
                t.RecurringPaymentId == null && !transfers.Contains(t.Id))
            .SelectMany(t => Matches(data, t)).OrderByDescending(c => c.Transaction.Date)
            .ThenBy(c => Math.Abs(c.Difference)).ToList();
    }

    public static IEnumerable<RecurringCandidate> Matches(HouseholdData data, Transaction transaction)
        => Matches(data, transaction, null);

    private static IEnumerable<RecurringCandidate> Matches(HouseholdData data, Transaction transaction,
        IReadOnlySet<(Guid PaymentId, DateOnly Due)>? linked)
    {
        foreach (var item in data.RecurringPayments.Where(r => r.AccountId == transaction.AccountId &&
            Math.Sign(r.Amount) == Math.Sign(transaction.Amount) && Math.Abs(r.Amount - transaction.Amount) <= r.MatchTolerance &&
            !string.IsNullOrWhiteSpace(r.MatchText) && transaction.Description.Contains(r.MatchText, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var due in Occurrences(item, transaction.Date.AddDays(-item.MatchWindowDays), transaction.Date.AddDays(item.MatchWindowDays)))
                if (!(linked?.Contains((item.Id, due)) ?? IsLinked(data, item, due))) yield return new(item, transaction, due);
        }
    }

    public static IReadOnlyList<RecurringCandidate> BatchCandidates(HouseholdData data, Guid paymentId, DateOnly through)
    {
        var payment = data.RecurringPayments.Single(r => r.Id == paymentId);
        if (string.IsNullOrWhiteSpace(payment.MatchText)) return [];
        var transfers = TransferMatching.ConfirmedTransactionIds(data);
        var payments = data.RecurringPayments.ToDictionary(r => r.Id);
        var linked = data.Transactions.Where(t => t.RecurringPaymentId != null)
            .Select(t => (t.RecurringPaymentId!.Value, LinkedDate(payments[t.RecurringPaymentId.Value], t))).ToHashSet();
        // Snapshot both sides before linking: never choose an arbitrary entry for an occurrence,
        // or a plan for an entry. The user confirms the batch within their configured margins.
        var candidates = data.Transactions.Where(t => t.AccountId == payment.AccountId &&
                t.Date <= through && t.Kind != TransactionKind.Transfer && t.RecurringPaymentId == null && !transfers.Contains(t.Id))
            .SelectMany(t => Matches(data, t, linked)).ToList();
        var byTransaction = candidates.ToLookup(c => c.Transaction.Id);
        var byOccurrence = candidates.ToLookup(c => (c.Payment.Id, c.DueDate));
        return candidates.Where(c => c.Payment.Id == paymentId &&
            byTransaction[c.Transaction.Id].Count() == 1 && byOccurrence[(c.Payment.Id, c.DueDate)].Count() == 1 &&
            !TransactionAccounting.HasDetails(data, c.Transaction.Id))
            .OrderBy(c => c.Transaction.Date).ToList();
    }

    public static void Link(HouseholdData data, Guid transactionId, Guid paymentId, DateOnly due)
    {
        var transaction = data.Transactions.Single(t => t.Id == transactionId);
        var item = data.RecurringPayments.Single(r => r.Id == paymentId);
        if (transaction.Kind == TransactionKind.Transfer || TransferMatching.ConfirmedTransactionIds(data).Contains(transactionId) ||
            transaction.RecurringPaymentId != null || transaction.AccountId != item.AccountId || Math.Sign(transaction.Amount) != Math.Sign(item.Amount))
            throw new InvalidOperationException("Deze boeking kan niet aan deze vaste post worden gekoppeld. Verwijder eerst een bestaande koppeling.");
        if (Occurrence(item, due.Year, due.Month) != due || IsLinked(data, item, due))
            throw new InvalidOperationException("Deze geplande betaling bestaat niet of is al gekoppeld.");
        HouseholdEdits.SaveTransaction(data, transaction with { RecurringPaymentId = item.Id, RecurringOccurrenceDate = due,
            Kind = item.IsMortgage ? TransactionKind.Mortgage : transaction.Kind });
    }

    public static void Unlink(HouseholdData data, Guid transactionId)
    {
        var transaction = data.Transactions.Single(t => t.Id == transactionId);
        HouseholdEdits.SaveTransaction(data, transaction with { RecurringPaymentId = null, RecurringOccurrenceDate = null });
    }
}
