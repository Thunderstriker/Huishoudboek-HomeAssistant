namespace Huishoudboek.Core;

/// <summary>One shared household register. An opening balance is not newly earned income.</summary>
public sealed record MealVoucherCredit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int Quantity { get; set; } = 1;
    public decimal UnitValue { get; set; }
    public bool IsOpeningBalance { get; set; }
    public string Note { get; set; } = "";
}

public static class MealVouchers
{
    public static decimal Value(MealVoucherCredit credit) => credit.Quantity * credit.UnitValue;

    public static decimal Balance(HouseholdData data, DateOnly asOf) =>
        data.MealVoucherCredits.Where(c => c.Date <= asOf).Sum(Value) -
        data.PurchaseReceipts.Where(r => r.IsApplied && r.PurchaseDate <= asOf).Sum(r => r.MealVoucherAmount);

    public static void SaveCredit(HouseholdData data, MealVoucherCredit credit)
    {
        var copy = credit with { Note = credit.Note.Trim() };
        var index = data.MealVoucherCredits.FindIndex(c => c.Id == copy.Id);
        if (index < 0) data.MealVoucherCredits.Add(copy); else data.MealVoucherCredits[index] = copy;
    }

    public static void Validate(HouseholdData data)
    {
        if (data.MealVoucherCredits.Any(c => c.Date.Year is < 1900 or > 2200 || c.Quantity is < 1 or > 100_000 ||
            c.UnitValue is <= 0 or > 1_000_000 || decimal.Round(c.UnitValue, 2) != c.UnitValue ||
            Value(c) > 1_000_000_000m || c.Note == null || c.Note.Length > 2000))
            throw new InvalidOperationException("Vul voor maaltijdcheques een geldige datum, een positief geheel aantal en een positieve waarde met maximaal twee decimalen in.");
        if (data.MealVoucherCredits.Count(c => c.IsOpeningBalance) > 1)
            throw new InvalidOperationException("Er kan maar één beginsaldo voor de gezamenlijke maaltijdcheques zijn.");
    }

    /// <summary>Non-cash reporting lines only. Never feed these into bank balances or cash projections.</summary>
    public static IEnumerable<AccountingLine> Lines(HouseholdData data)
    {
        foreach (var credit in data.MealVoucherCredits.Where(c => !c.IsOpeningBalance))
        {
            var transaction = new Transaction { Id = credit.Id, Date = credit.Date, Amount = Value(credit),
                Description = "Maaltijdcheques", UserDescription = credit.Note };
            yield return new(transaction, null, transaction.Amount, null, AllocationTreatment.Ordinary, credit.Note);
        }
        var categories = data.Categories.ToDictionary(c => c.Id);
        var receiptItems = data.PurchaseReceiptItems.ToLookup(i => i.ReceiptId);
        var allocations = data.TransactionAllocations.ToLookup(a => a.TransactionId);
        var transactions = data.Transactions.ToDictionary(t => t.Id);
        foreach (var receipt in data.PurchaseReceipts.Where(r => r.IsApplied && r.MealVoucherAmount > 0))
        {
            // Use the linked bank date for the whole purchase's reports; the voucher register
            // itself follows the purchase date. A fully voucher-funded receipt uses its own date.
            var date = receipt.TransactionId is { } id ? transactions[id].Date : receipt.PurchaseDate!.Value;
            var transaction = new Transaction { Id = receipt.Id, Date = date, Amount = -receipt.MealVoucherAmount,
                Description = receipt.Merchant + " · maaltijdcheques" };
            var items = receiptItems[receipt.Id].ToList();
            foreach (var group in items.GroupBy(i => PurchaseReceipts.CategoryFor(receipt, i, items)))
            {
                var bankPaid = receipt.TransactionId is { } bankId
                    ? -allocations[bankId].Where(a => a.CategoryId == group.Key).Sum(a => a.Amount) : 0;
                var amount = group.Sum(i => i.Amount!.Value) - bankPaid;
                if (amount == 0) continue;
                var category = group.Key is { } categoryId ? categories.GetValueOrDefault(categoryId) : null;
                yield return new(transaction, null, -amount, group.Key, AllocationTreatment.Ordinary, receipt.Notes,
                    IsEssential: category?.IsEssential == true, IsExceptional: category?.IsExceptional == true);
            }
        }
    }
}
