namespace Huishoudboek.Core;

/// <summary>Client-neutral edit operations. The caller validates and persists the whole change atomically.</summary>
public static class HouseholdEdits
{
    private static void Put<T>(List<T> items, T item, Func<T, Guid> key)
    {
        var index = items.FindIndex(x => key(x) == key(item));
        if (index < 0) items.Add(item); else items[index] = item;
    }

    public static void SaveOwner(HouseholdData data, Owner item) => Put(data.Owners, item with { Name = item.Name.Trim() }, x => x.Id);
    public static void SaveAccount(HouseholdData data, Account item) => Put(data.Accounts,
        item with { Name = item.Name.Trim(), BankReference = item.BankReference.Trim(), OwnerIds = [.. item.OwnerIds] }, x => x.Id);
    public static void SaveCategory(HouseholdData data, Category item)
    {
        if (string.IsNullOrWhiteSpace(item.Name) || data.Categories.Any(c => c.Id != item.Id && c.Name.Trim().Equals(item.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Vul een unieke categorienaam in.");
        Put(data.Categories, item with { Name = item.Name.Trim() }, x => x.Id);
    }

    public static void SetBudget(HouseholdData data, Guid categoryId, int year, int month, decimal amount)
    {
        var item = data.BudgetOverrides.FirstOrDefault(b => b.CategoryId == categoryId && b.Year == year && b.Month == month)
            ?? new BudgetOverride { CategoryId = categoryId, Year = year, Month = month };
        Put(data.BudgetOverrides, item with { Amount = amount }, x => x.Id);
    }

    public static void SaveTransaction(HouseholdData data, Transaction item, bool useTransferDefault = false)
    {
        var original = data.Transactions.FirstOrDefault(t => t.Id == item.Id);
        if (original != null && TransactionAccounting.HasDetails(data, item.Id) &&
            (original.AccountId != item.AccountId || original.Amount != item.Amount || original.Date != item.Date ||
             original.Description != item.Description.Trim() || original.Kind != item.Kind))
            throw new InvalidOperationException("Deze bankboeking heeft splitsingen of retour-/vergoedingskoppelingen. Verwijder deze eerst vóór het wijzigen van bankgegevens of type.");
        if (original != null && (original.AccountId != item.AccountId || original.Amount != item.Amount || original.Date != item.Date ||
            original.Kind != item.Kind || original.CounterAccountId != item.CounterAccountId ||
            original.RecurringPaymentId != item.RecurringPaymentId || original.PrincipalComponent != item.PrincipalComponent))
            TransferMatching.RemoveForTransactions(data, new HashSet<Guid> { item.Id });
        item = item with { Description = item.Description.Trim(), UserDescription = item.UserDescription.Trim() };
        if (item.Kind != TransactionKind.Transfer) item.CounterAccountId = null;
        else
        {
            item.RecurringPaymentId = null;
            if (useTransferDefault) { item.CategoryId = null; TransferCategories.ApplyDefault(data, item); }
        }
        if (item.Kind != TransactionKind.Mortgage) item.PrincipalComponent = 0;
        if (item.RecurringPaymentId == null) item.RecurringOccurrenceDate = null;
        else if (item.RecurringOccurrenceDate == null &&
            (original == null || original.RecurringPaymentId != item.RecurringPaymentId || original.Date != item.Date || original.RecurringOccurrenceDate != null) &&
            data.RecurringPayments.FirstOrDefault(r => r.Id == item.RecurringPaymentId) is { } recurring)
        {
            item.RecurringOccurrenceDate = RecurringSchedule.Occurrence(recurring, item.Date.Year, item.Date.Month)
                ?? throw new InvalidOperationException("Geen geplande betaling in deze bankmaand. Kies de geplande betaaldatum of koppel via Vaste posten.");
        }
        Put(data.Transactions, item, x => x.Id);
    }

    public static void DeleteTransaction(HouseholdData data, Guid id)
    {
        TransactionAccounting.RemoveForTransactions(data, new HashSet<Guid> { id });
        TransferMatching.RemoveForTransactions(data, new HashSet<Guid> { id });
        data.Transactions.RemoveAll(t => t.Id == id);
    }

    public static void SaveRecurring(HouseholdData data, RecurringPayment item) => Put(data.RecurringPayments,
        item with { Name = item.Name.Trim(), MatchText = item.MatchText.Trim() }, x => x.Id);
    public static void DeleteRecurring(HouseholdData data, Guid id)
    {
        data.RecurringPayments.RemoveAll(r => r.Id == id);
        foreach (var t in data.Transactions.Where(t => t.RecurringPaymentId == id))
        { t.RecurringPaymentId = null; t.RecurringOccurrenceDate = null; }
    }
    public static void SaveValuation(HouseholdData data, InvestmentValuation item) => Put(data.Valuations, item, x => x.Id);
    public static void SaveRule(HouseholdData data, CategorizationRule item)
    {
        if (data.Rules.Any(r => r.Id != item.Id && r.Contains.Trim().Equals(item.Contains.Trim(), StringComparison.OrdinalIgnoreCase) && r.CategoryId != item.CategoryId))
            throw new InvalidOperationException("Deze herkenningstekst heeft al een andere categorie.");
        Put(data.Rules, item with { Contains = item.Contains.Trim() }, x => x.Id);
    }
    public static void ApplyRules(HouseholdData data)
    {
        var linked = TransferMatching.ConfirmedTransactionIds(data);
        foreach (var t in data.Transactions.Where(t => t.Kind != TransactionKind.Transfer && !linked.Contains(t.Id))) Finance.ApplySuggestions(data, t);
    }
    public static void UndoImport(HouseholdData data, Guid id)
    {
        var batch = data.ImportBatches.FirstOrDefault(b => b.Id == id && !b.Undone)
            ?? throw new InvalidOperationException("Deze import bestaat niet of is al teruggedraaid.");
        var removed = data.Transactions.Where(t => t.ImportBatchId == id).Select(t => t.Id).ToHashSet();
        TransactionAccounting.RemoveForTransactions(data, removed);
        TransferMatching.RemoveForTransactions(data, removed);
        data.Transactions.RemoveAll(t => t.ImportBatchId == id);
        batch.Undone = true;
    }
}
