namespace Huishoudboek.Core;

public enum ReceiptLineKind { Product, Discount, Deposit, Adjustment }
public enum PurchaseItemStatus { Kept, Returned, RefundExpected, Refunded }

/// <summary>Purchase evidence only. Receipt edits never create or change bank entries or allocations.</summary>
public sealed record PurchaseReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AccountId { get; set; }
    public Guid? TransactionId { get; set; }
    public string Merchant { get; set; } = "";
    public DateOnly? PurchaseDate { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public decimal? Total { get; set; }
    public string Notes { get; set; } = "";
    public bool IsReviewed { get; set; }
    // The bytes live in a separate SQLite BLOB table, not in every administration snapshot.
    public string DocumentName { get; set; } = "";
    public string DocumentContentType { get; set; } = "";
}

public sealed record PurchaseReceiptItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReceiptId { get; set; }
    public int Position { get; set; }
    public string OriginalDescription { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal? UnitPrice { get; set; }
    // Signed final amount. Discounts are separate negative lines; VAT already included is not a line.
    public decimal? Amount { get; set; }
    public ReceiptLineKind Kind { get; set; }
    public Guid? RelatedItemId { get; set; }
    public Guid? CategoryId { get; set; }
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Location { get; set; } = "";
    public DateOnly? ReturnBy { get; set; }
    public DateOnly? WarrantyUntil { get; set; }
    public PurchaseItemStatus Status { get; set; }
    public Guid? ReimbursementClaimId { get; set; }
}

public static class PurchaseReceipts
{
    public const int MaximumItems = 500;
    public const long MaximumDocumentBytes = 8L * 1024 * 1024;

    public static decimal ItemTotal(IEnumerable<PurchaseReceiptItem> items) => items.Sum(i => i.Amount ?? 0);

    public static bool Reconciles(PurchaseReceipt receipt, IReadOnlyCollection<PurchaseReceiptItem> items) =>
        receipt.Total is { } total && items.Count > 0 && items.All(i => i.Amount != null && !string.IsNullOrWhiteSpace(i.Name)) &&
        ItemTotal(items) == total;

    public static void Save(HouseholdData data, PurchaseReceipt receipt, IReadOnlyList<PurchaseReceiptItem> items)
    {
        var copy = receipt with { Merchant = receipt.Merchant.Trim(), InvoiceNumber = receipt.InvoiceNumber.Trim(), Notes = receipt.Notes.Trim() };
        if (copy.TransactionId is { } transactionId)
        {
            var transaction = data.Transactions.SingleOrDefault(t => t.Id == transactionId)
                ?? throw new InvalidOperationException("De gekoppelde bankboeking bestaat niet meer. Vernieuw de gegevens en kies opnieuw.");
            copy.AccountId = transaction.AccountId;
        }
        var index = data.PurchaseReceipts.FindIndex(r => r.Id == copy.Id);
        if (index < 0) data.PurchaseReceipts.Add(copy); else data.PurchaseReceipts[index] = copy;
        data.PurchaseReceiptItems.RemoveAll(i => i.ReceiptId == copy.Id);
        data.PurchaseReceiptItems.AddRange(items.Select((i, position) => i with { ReceiptId = copy.Id, Position = position,
            Name = i.Name.Trim(), Unit = i.Unit.Trim(), Model = i.Model.Trim(), SerialNumber = i.SerialNumber.Trim(), Location = i.Location.Trim() }));
    }

    public static void Delete(HouseholdData data, Guid receiptId)
    {
        data.PurchaseReceiptItems.RemoveAll(i => i.ReceiptId == receiptId);
        data.PurchaseReceipts.RemoveAll(r => r.Id == receiptId);
    }

    public static void DetachTransactions(HouseholdData data, IReadOnlySet<Guid> transactionIds)
    {
        var detached = data.PurchaseReceipts.Where(r => r.TransactionId is { } id && transactionIds.Contains(id)).ToList();
        foreach (var receipt in detached) { receipt.TransactionId = null; receipt.IsReviewed = false; }
        var receiptIds = detached.Select(r => r.Id).ToHashSet();
        foreach (var item in data.PurchaseReceiptItems.Where(i => receiptIds.Contains(i.ReceiptId))) item.ReimbursementClaimId = null;
    }

    public static void Validate(HouseholdData data)
    {
        static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        static bool Text(string? text, int limit) => text != null && text.Length <= limit;
        static bool Money(decimal? value) => value == null || value is >= -1_000_000_000m and <= 1_000_000_000m && decimal.Round(value.Value, 2) == value;
        static bool Date(DateOnly? value) => value == null || value.Value.Year is >= 1900 and <= 2200;
        var receipts = data.PurchaseReceipts.ToDictionary(r => r.Id);
        var items = data.PurchaseReceiptItems.ToDictionary(i => i.Id);
        foreach (var receipt in receipts.Values)
        {
            Require(Text(receipt.Merchant, 200) && Text(receipt.InvoiceNumber, 200) && Text(receipt.Notes, 4000), "Winkel, factuurnummer of notitie is te lang.");
            Require(Date(receipt.PurchaseDate) && Money(receipt.Total) && receipt.Total is null or >= 0, "Controleer de aankoopdatum en het bontotaal (maximaal twee decimalen).");
            Require(receipt.AccountId == null || data.Accounts.Any(a => a.Id == receipt.AccountId), "Kies een bestaande rekening voor de bon.");
            Require(receipt.TransactionId == null || data.Transactions.Any(t => t.Id == receipt.TransactionId && t.AccountId == receipt.AccountId && t.Kind == TransactionKind.Normal && t.Amount < 0),
                "Koppel de bon aan een gewone afschrijving op de gekozen rekening.");
            Require(Text(receipt.DocumentName, 200) && Text(receipt.DocumentContentType, 100) &&
                (receipt.DocumentName.Length == 0 ? receipt.DocumentContentType.Length == 0 :
                    receipt.DocumentContentType is "image/jpeg" or "image/png" or "application/pdf"), "Ongeldige bonbijlage.");
            var lines = items.Values.Where(i => i.ReceiptId == receipt.Id).ToList();
            Require(lines.Count <= MaximumItems && lines.Select(i => i.Position).Distinct().Count() == lines.Count, "Een bon bevat maximaal 500 regels met een unieke volgorde.");
            Require(!receipt.IsReviewed || !string.IsNullOrWhiteSpace(receipt.Merchant) && receipt.PurchaseDate != null && Reconciles(receipt, lines),
                "Bevestig een bon pas na controle van winkel, datum, regels en aansluitend bontotaal. Opslaan als concept blijft mogelijk.");
        }
        foreach (var item in items.Values)
        {
            Require(receipts.ContainsKey(item.ReceiptId) && item.Position >= 0 && Enum.IsDefined(item.Kind) && Enum.IsDefined(item.Status), "Ongeldige bonregel.");
            Require(Text(item.Name, 300) && Text(item.OriginalDescription, 2000) && Text(item.Unit, 30) && Text(item.Model, 200) && Text(item.SerialNumber, 200) && Text(item.Location, 200), "Een tekstveld van de bonregel is te lang.");
            Require(Money(item.Amount) && (item.Quantity == null || item.Quantity is > 0 and <= 1_000_000 && decimal.Round(item.Quantity.Value, 4) == item.Quantity) &&
                (item.UnitPrice == null || item.UnitPrice is >= 0 and <= 1_000_000_000 && decimal.Round(item.UnitPrice.Value, 4) == item.UnitPrice), "Controleer regelbedrag, aantal en eenheidsprijs.");
            Require(item.CategoryId == null || data.Categories.Any(c => c.Id == item.CategoryId && c.Purpose == CategoryPurpose.Normal), "Kies een gewone uitgavencategorie voor de bonregel.");
            Require(item.RelatedItemId == null || item.Kind == ReceiptLineKind.Discount && items.TryGetValue(item.RelatedItemId.Value, out var related) &&
                related.ReceiptId == item.ReceiptId && related.Kind == ReceiptLineKind.Product, "Koppel een korting alleen aan een product op dezelfde bon.");
            Require(item.Kind != ReceiptLineKind.Discount || item.Amount is null or <= 0, "Een kortingsregel heeft een negatief bedrag.");
            var purchaseDate = receipts[item.ReceiptId].PurchaseDate;
            Require(Date(item.ReturnBy) && Date(item.WarrantyUntil) && (purchaseDate == null ||
                (item.ReturnBy == null || item.ReturnBy >= purchaseDate) && (item.WarrantyUntil == null || item.WarrantyUntil >= purchaseDate)), "Een retour- of garantiedatum ligt niet vóór de aankoopdatum.");
            Require(item.ReimbursementClaimId == null || data.ReimbursementClaims.Any(c => c.Id == item.ReimbursementClaimId &&
                c.ExpenseTransactionId == receipts[item.ReceiptId].TransactionId), "Kies een bestaande vergoedingsverwachting van de gekoppelde aankoop.");
        }
    }
}
