namespace Huishoudboek.Core;

public sealed record TransferCandidate(Transaction Debit, Transaction Credit, int DaysApart,
    bool HasAccountReference, bool HasAlternatives);

/// <summary>Explicit, reversible classification of existing EUR entries, independent of bank imports.</summary>
public static class TransferMatching
{
    public const int SuggestionWindowDays = 5;

    public static bool IsEligible(Transaction transaction) => transaction.Kind == TransactionKind.Normal &&
        transaction.CounterAccountId == null && transaction.RecurringPaymentId == null && transaction.PrincipalComponent == 0;

    public static HashSet<Guid> ConfirmedTransactionIds(HouseholdData data) => data.TransferMatches
        .Where(m => m.IsConfirmed).SelectMany(m => new[] { m.DebitTransactionId, m.CreditTransactionId }).ToHashSet();

    public static bool IsInternal(HouseholdData data, Transaction transaction) =>
        transaction.Kind == TransactionKind.Transfer || data.TransferMatches.Any(m => m.IsConfirmed &&
            (m.DebitTransactionId == transaction.Id || m.CreditTransactionId == transaction.Id));

    public static IEnumerable<TransferCandidate> Candidates(HouseholdData data, DateOnly today)
    {
        var linked = ConfirmedTransactionIds(data);
        var entries = data.Transactions.Where(t => IsEligible(t) && t.Date <= today && !linked.Contains(t.Id)).ToList();
        var debits = entries.Where(t => t.Amount < 0).OrderByDescending(t => t.Date).ToList();
        var credits = entries.Where(t => t.Amount > 0).ToList();
        var debitIndex = debits.ToLookup(t => (-t.Amount, t.Date.DayNumber));
        var creditIndex = credits.ToLookup(t => (t.Amount, t.Date.DayNumber));
        var dismissed = data.TransferMatches.Where(m => !m.IsConfirmed)
            .Select(m => (m.DebitTransactionId, m.CreditTransactionId)).ToHashSet();
        var accounts = data.Accounts.ToDictionary(a => a.Id);

        // Date/amount buckets keep the search bounded; callers can page/limit the lazy results.
        IEnumerable<Transaction> Counterparts(Transaction entry, bool debit)
        {
            for (var offset = -SuggestionWindowDays; offset <= SuggestionWindowDays; offset++)
                foreach (var other in (debit ? creditIndex : debitIndex)[(Math.Abs(entry.Amount), entry.Date.DayNumber + offset)])
                    if (other.AccountId != entry.AccountId && !dismissed.Contains(debit ? (entry.Id, other.Id) : (other.Id, entry.Id)))
                        yield return other;
        }

        foreach (var debit in debits)
        {
            var alternatives = Counterparts(debit, true).Take(2).Count() > 1;
            foreach (var credit in Counterparts(debit, true))
            {
                bool References(Transaction entry, Account account)
                {
                    var reference = BankReferences.Normalize(account.BankReference);
                    return reference.Length >= 8 && BankReferences.Normalize(entry.Description).Contains(reference, StringComparison.Ordinal);
                }
                yield return new(debit, credit, Math.Abs(debit.Date.DayNumber - credit.Date.DayNumber),
                    References(debit, accounts[credit.AccountId]) || References(credit, accounts[debit.AccountId]),
                    alternatives || Counterparts(credit, false).Take(2).Count() > 1);
            }
        }
    }

    public static void Confirm(HouseholdData data, Guid debitId, Guid creditId, DateOnly today) =>
        Decide(data, debitId, creditId, today, confirmed: true);

    public static void Reject(HouseholdData data, Guid debitId, Guid creditId, DateOnly today) =>
        Decide(data, debitId, creditId, today, confirmed: false);

    private static void Decide(HouseholdData data, Guid debitId, Guid creditId, DateOnly today, bool confirmed)
    {
        var debit = data.Transactions.FirstOrDefault(t => t.Id == debitId);
        var credit = data.Transactions.FirstOrDefault(t => t.Id == creditId);
        if (debit == null || credit == null || !IsEligible(debit) || !IsEligible(credit) ||
            debit.AccountId == credit.AccountId || debit.Amount >= 0 || credit.Amount != -debit.Amount)
            throw new InvalidOperationException("Kies twee gewone, even grote af- en bijboekingen op verschillende eigen rekeningen, zonder vaste-postkoppeling.");
        if (debit.Date > today || credit.Date > today)
            throw new InvalidOperationException("Bevestig alleen werkelijke boekingen tot en met vandaag. Er wordt geen ontbrekende ontvangst aangemaakt.");
        var linked = ConfirmedTransactionIds(data);
        if (linked.Contains(debitId) || linked.Contains(creditId))
            throw new InvalidOperationException("Een boeking is al gekoppeld. Verwijder eerst de bestaande transferkoppeling.");
        data.TransferMatches.RemoveAll(m => m.DebitTransactionId == debitId && m.CreditTransactionId == creditId);
        data.TransferMatches.Add(new TransferMatch { DebitTransactionId = debitId, CreditTransactionId = creditId, IsConfirmed = confirmed });
    }

    public static void Remove(HouseholdData data, Guid matchId)
    {
        if (data.TransferMatches.RemoveAll(m => m.Id == matchId) == 0)
            throw new InvalidOperationException("Deze transferbeslissing bestaat niet meer. Vernieuw de administratie.");
    }

    public static void RemoveForTransactions(HouseholdData data, IReadOnlySet<Guid> ids) =>
        data.TransferMatches.RemoveAll(m => ids.Contains(m.DebitTransactionId) || ids.Contains(m.CreditTransactionId));
}
