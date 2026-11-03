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
        item = item with { Description = item.Description.Trim(), UserDescription = item.UserDescription.Trim() };
        if (item.Kind != TransactionKind.Transfer) item.CounterAccountId = null;
        else
        {
            item.RecurringPaymentId = null;
            if (useTransferDefault) { item.CategoryId = null; TransferCategories.ApplyDefault(data, item); }
        }
        if (item.Kind != TransactionKind.Mortgage) item.PrincipalComponent = 0;
        Put(data.Transactions, item, x => x.Id);
    }

    public static void SaveRecurring(HouseholdData data, RecurringPayment item) => Put(data.RecurringPayments,
        item with { Name = item.Name.Trim(), MatchText = item.MatchText.Trim() }, x => x.Id);
    public static void DeleteRecurring(HouseholdData data, Guid id)
    {
        data.RecurringPayments.RemoveAll(r => r.Id == id);
        foreach (var t in data.Transactions.Where(t => t.RecurringPaymentId == id)) t.RecurringPaymentId = null;
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
        foreach (var t in data.Transactions.Where(t => t.Kind != TransactionKind.Transfer)) Finance.ApplySuggestions(data, t);
    }
    public static void UndoImport(HouseholdData data, Guid id)
    {
        var batch = data.ImportBatches.FirstOrDefault(b => b.Id == id && !b.Undone)
            ?? throw new InvalidOperationException("Deze import bestaat niet of is al teruggedraaid.");
        data.Transactions.RemoveAll(t => t.ImportBatchId == id);
        batch.Undone = true;
    }
}
