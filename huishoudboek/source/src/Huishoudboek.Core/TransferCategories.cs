namespace Huishoudboek.Core;

public static class TransferCategories
{
    public static string? DefaultName(HouseholdData data, Transaction transaction)
    {
        if (transaction.Kind != TransactionKind.Transfer) return null;
        return data.Accounts.FirstOrDefault(a => a.Id == transaction.CounterAccountId)?.Kind switch
        {
            AccountKind.Savings => "Sparen",
            AccountKind.Investment => "Investeringen",
            _ => null
        };
    }

    public static void ApplyDefault(HouseholdData data, Transaction transaction)
    {
        if (transaction.CategoryId != null || DefaultName(data, transaction) is not { } name) return;
        var category = data.Categories.FirstOrDefault(c => string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (category == null)
        {
            category = new Category { Name = name };
            data.Categories.Add(category);
        }
        transaction.CategoryId = category.Id;
    }
}
