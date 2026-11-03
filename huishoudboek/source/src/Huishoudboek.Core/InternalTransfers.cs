namespace Huishoudboek.Core;

public sealed record TransferRecognition(Guid? CounterAccountId, string Note, bool RequiresReview = false);

public static class InternalTransfers
{
    public static string NormalizeReference(string reference) =>
        string.Concat(reference.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public static TransferRecognition Recognize(HouseholdData data, Guid sourceAccountId, string reference)
    {
        var normalized = NormalizeReference(reference);
        if (normalized.Length == 0) return new(null, "");
        var matches = data.Accounts.Where(a => NormalizeReference(a.BankReference) == normalized).ToList();
        if (matches.Count > 1)
            return new(null, "Tegenrekening hoort bij meerdere eigen rekeningen; kies de juiste rekening of controleer de rekeningnummers.", true);
        if (matches.Count == 0) return new(null, "");
        if (matches[0].Id == sourceAccountId)
            return new(null, "Tegenrekening is gelijk aan de importrekening; controleer deze regel.", true);
        return new(matches[0].Id, "Interne overboeking herkend via rekening tegenpartij.");
    }

    // Keep a single negative, sender-oriented transfer, even when importing its receipt.
    public static void SetCounterAccount(Transaction transaction, Guid sourceAccountId, decimal sourceAmount, Guid? counterAccountId)
    {
        transaction.Kind = counterAccountId == null ? TransactionKind.Normal : TransactionKind.Transfer;
        transaction.AccountId = counterAccountId != null && sourceAmount > 0 ? counterAccountId.Value : sourceAccountId;
        transaction.CounterAccountId = counterAccountId == null ? null : sourceAmount > 0 ? sourceAccountId : counterAccountId;
        transaction.Amount = counterAccountId == null ? sourceAmount : -Math.Abs(sourceAmount);
        transaction.PrincipalComponent = 0;
        transaction.RecurringPaymentId = null;
    }
}
