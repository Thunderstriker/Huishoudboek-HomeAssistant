namespace Huishoudboek.Core;

public sealed record AccountingLine(Transaction Transaction, Guid? AllocationId, decimal Amount,
    Guid? CategoryId, AllocationTreatment Treatment, string Note,
    CategoryPurpose Purpose = CategoryPurpose.Normal, bool IsCurrentAccount = false)
{
    public decimal Income => Purpose == CategoryPurpose.Normal && Treatment == AllocationTreatment.Ordinary && Amount > 0 ? Amount : 0;
    public decimal Spending => Purpose == CategoryPurpose.Normal && (Amount < 0 || Treatment != AllocationTreatment.Ordinary) ? -Amount : 0;
    public decimal Savings => Purpose == CategoryPurpose.Savings && IsCurrentAccount ? -Amount : 0;
}

/// <summary>One interpretation of bank entries for every budget/report, never for account balances.</summary>
public static class TransactionAccounting
{
    public static IEnumerable<AccountingLine> Lines(HouseholdData data, IEnumerable<Transaction> transactions)
    {
        var purposes = data.Categories.ToDictionary(c => c.Id, c => c.Purpose);
        var current = data.Accounts.Where(a => a.Kind == AccountKind.Current).Select(a => a.Id).ToHashSet();
        AccountingLine Line(Transaction t, Guid? allocationId, decimal amount, Guid? category,
            AllocationTreatment treatment, string note) => new(t, allocationId, amount, category, treatment, note,
                category is { } id ? purposes.GetValueOrDefault(id) : CategoryPurpose.Normal, current.Contains(t.AccountId));
        var allocations = data.TransactionAllocations.ToLookup(a => a.TransactionId);
        Dictionary<Guid, Transaction>? entries = null;
        var splits = data.TransactionAllocations.ToDictionary(a => a.Id);
        var claims = data.ReimbursementClaims.ToDictionary(c => c.Id);
        var internalIds = TransferMatching.ConfirmedTransactionIds(data);
        foreach (var t in transactions)
        {
            if (t.Kind == TransactionKind.Transfer || internalIds.Contains(t.Id)) continue;
            if (!allocations[t.Id].Any())
            {
                yield return Line(t, null, t.Amount, t.CategoryId, AllocationTreatment.Ordinary, "");
                continue;
            }
            foreach (var a in allocations[t.Id])
            {
                var sourceId = a.OriginalTransactionId;
                var splitId = a.OriginalAllocationId;
                if (a.ReimbursementClaimId is { } claimId)
                {
                    sourceId = claims[claimId].ExpenseTransactionId;
                    splitId = claims[claimId].ExpenseAllocationId;
                }
                var category = sourceId is { } originalId
                    ? splitId is { } originalSplitId ? splits[originalSplitId].CategoryId :
                        (entries ??= data.Transactions.ToDictionary(entry => entry.Id))[originalId].CategoryId
                    : a.CategoryId;
                yield return Line(t, a.Id, a.Amount, category, a.Treatment, a.Note);
            }
        }
    }

    public static bool HasDetails(HouseholdData data, Guid transactionId) =>
        data.TransactionAllocations.Any(a => a.TransactionId == transactionId) ||
        data.ReimbursementClaims.Any(c => c.ExpenseTransactionId == transactionId) ||
        data.TransactionAllocations.Any(a => a.OriginalTransactionId == transactionId);

    public static decimal Paid(HouseholdData data, ReimbursementClaim claim, DateOnly asOf) =>
        data.TransactionAllocations.Where(a => a.ReimbursementClaimId == claim.Id &&
            data.Transactions.Any(t => t.Id == a.TransactionId && t.Date <= asOf)).Sum(a => a.Amount);

    public static decimal Outstanding(HouseholdData data, ReimbursementClaim claim, DateOnly asOf) =>
        claim.IsClosed || data.Transactions.Single(t => t.Id == claim.ExpenseTransactionId).Date > asOf
            ? 0 : Math.Max(0, claim.ExpectedAmount - Paid(data, claim, asOf));

    public static void SaveAllocations(HouseholdData data, Guid transactionId, IEnumerable<TransactionAllocation> items)
    {
        var original = data.TransactionAllocations;
        var proposed = items.Select(a => a with { TransactionId = transactionId, Note = a.Note?.Trim() ?? "" }).ToList();
        data.TransactionAllocations = original.Where(a => a.TransactionId != transactionId).Concat(proposed).ToList();
        try { Validation.Validate(data); }
        catch { data.TransactionAllocations = original; throw; }
    }

    public static void SaveDetails(HouseholdData data, Guid transactionId, IEnumerable<TransactionAllocation> allocations,
        IEnumerable<ReimbursementClaim> claims)
    {
        var oldAllocations = data.TransactionAllocations;
        var oldClaims = data.ReimbursementClaims;
        // Materialize both requests before touching either collection (enumeration/parsing can fail).
        var proposedAllocations = oldAllocations.Where(a => a.TransactionId != transactionId).Concat(
            allocations.Select(a => a with { TransactionId = transactionId, Note = a.Note?.Trim() ?? "" })).ToList();
        var proposedClaims = oldClaims.Where(c => c.ExpenseTransactionId != transactionId).Concat(
            claims.Select(c => c with { ExpenseTransactionId = transactionId, Counterparty = c.Counterparty?.Trim() ?? "", Note = c.Note?.Trim() ?? "" })).ToList();
        data.TransactionAllocations = proposedAllocations;
        data.ReimbursementClaims = proposedClaims;
        try { Validation.Validate(data); }
        catch { data.TransactionAllocations = oldAllocations; data.ReimbursementClaims = oldClaims; throw; }
    }

    public static void SaveClaim(HouseholdData data, ReimbursementClaim item)
    {
        var original = data.ReimbursementClaims;
        data.ReimbursementClaims = original.Where(c => c.Id != item.Id).Append(item with
        { Counterparty = item.Counterparty?.Trim() ?? "", Note = item.Note?.Trim() ?? "" }).ToList();
        try { Validation.Validate(data); }
        catch { data.ReimbursementClaims = original; throw; }
    }

    public static void DeleteClaim(HouseholdData data, Guid id)
    {
        if (data.TransactionAllocations.Any(a => a.ReimbursementClaimId == id))
            throw new InvalidOperationException("Ontkoppel eerst de ontvangen vergoedingen. U kunt de verwachting ook sluiten.");
        data.ReimbursementClaims.RemoveAll(c => c.Id == id);
    }

    public static void EnsureCanDelete(HouseholdData data, IReadOnlySet<Guid> ids)
    {
        var claimIds = data.ReimbursementClaims.Where(c => ids.Contains(c.ExpenseTransactionId)).Select(c => c.Id).ToHashSet();
        if (data.TransactionAllocations.Any(a => !ids.Contains(a.TransactionId) &&
            (a.OriginalTransactionId is { } source && ids.Contains(source) ||
             a.ReimbursementClaimId is { } claim && claimIds.Contains(claim))))
            throw new InvalidOperationException("Ontkoppel eerst de retouren/vergoedingen van deze aankoop. De ontvangen bankboekingen blijven behouden.");
    }

    public static void RemoveForTransactions(HouseholdData data, IReadOnlySet<Guid> ids)
    {
        EnsureCanDelete(data, ids);
        data.TransactionAllocations.RemoveAll(a => ids.Contains(a.TransactionId));
        data.ReimbursementClaims.RemoveAll(c => ids.Contains(c.ExpenseTransactionId));
    }

    public static void Validate(HouseholdData data)
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static bool Money(decimal amount) => decimal.Round(amount, 2) == amount;
        var transactions = data.Transactions.ToDictionary(t => t.Id);
        var allocations = data.TransactionAllocations.ToDictionary(a => a.Id);
        var byTransaction = data.TransactionAllocations.ToLookup(a => a.TransactionId);
        var claims = data.ReimbursementClaims.ToDictionary(c => c.Id);
        var categories = data.Categories.Select(c => c.Id).ToHashSet();
        bool NormalCategory(Guid? id) => id == null || data.Categories.Any(c => c.Id == id && c.Purpose == CategoryPurpose.Normal);
        var internalIds = TransferMatching.ConfirmedTransactionIds(data);
        var recoveries = new Dictionary<(Guid, Guid?), decimal>();
        var refunds = new Dictionary<(Guid, Guid?), decimal>();

        decimal Expense(Guid transactionId, Guid? allocationId)
        {
            Require(transactions.TryGetValue(transactionId, out var t) && t.Kind == TransactionKind.Normal &&
                t.Amount < 0 && !internalIds.Contains(transactionId), "Kies een gewone uitgave als oorspronkelijke aankoop.");
            if (allocationId is { } id)
            {
                Require(allocations.TryGetValue(id, out var a) && a.TransactionId == transactionId && a.Amount < 0 &&
                    a.Treatment == AllocationTreatment.Ordinary && NormalCategory(a.CategoryId), "Kies een bestaande gewone uitgavensplitsing van deze aankoop.");
                return -a!.Amount;
            }
            Require(!byTransaction[transactionId].Any(), "Kies de oorspronkelijke splitsing, niet de hele gesplitste aankoop.");
            Require(NormalCategory(t!.CategoryId), "Retouren en vergoedingen horen bij gewone uitgaven, niet bij sparen of eigen rekeningbewegingen.");
            return -t!.Amount;
        }

        foreach (var group in byTransaction)
        {
            Require(transactions.TryGetValue(group.Key, out var t) && t.Kind == TransactionKind.Normal &&
                !internalIds.Contains(group.Key), "Splitsingen en retouren/vergoedingen zijn niet toegestaan bij transfers of hypotheekbetalingen.");
            Require(group.Sum(a => a.Amount) == t!.Amount, "De splitsingen moeten samen exact gelijk zijn aan het bankbedrag.");
        }
        foreach (var a in data.TransactionAllocations)
        {
            var t = transactions[a.TransactionId];
            Require(a.Amount != 0 && Money(a.Amount) && Math.Sign(a.Amount) == Math.Sign(t.Amount),
                "Splitsingen hebben maximaal twee decimalen en dezelfde betaalrichting als de bankboeking.");
            Require(Enum.IsDefined(a.Treatment) && a.Note != null, "Ongeldige splitsing of verwerking.");
            Require(a.CategoryId == null || categories.Contains(a.CategoryId.Value), "Onbekende categorie bij splitsing.");
            Require(a.OriginalAllocationId == null || a.OriginalTransactionId != null, "Een oorspronkelijke splitsing vereist een aankoop.");
            Require(a.Treatment != AllocationTreatment.Ordinary ||
                a.OriginalTransactionId == null && a.ReimbursementClaimId == null, "Gewone inkomsten/uitgaven hebben geen retourkoppeling.");
            Require(a.Treatment == AllocationTreatment.Ordinary || a.Amount > 0, "Retouren en vergoedingen moeten positieve ontvangsten zijn.");
            Require(a.Treatment == AllocationTreatment.Ordinary || NormalCategory(a.CategoryId),
                "Gebruik een gewone uitgavencategorie voor een retour of vergoeding.");
            Guid? source = a.OriginalTransactionId;
            var split = a.OriginalAllocationId;
            if (a.ReimbursementClaimId is { } claimId)
            {
                Require(a.Treatment == AllocationTreatment.Reimbursement && claims.ContainsKey(claimId) && source == null,
                    "Kies een bestaande vergoedingsverwachting, zonder tweede aankoopkoppeling.");
                var claim = claims[claimId]; source = claim.ExpenseTransactionId; split = claim.ExpenseAllocationId;
            }
            if (source is { } originalId)
            {
                var maximum = Expense(originalId, split);
                Require(a.CategoryId == null && transactions[originalId].Date <= t.Date,
                    "Een gekoppelde ontvangst gebruikt de aankoopcategorie en mag niet vóór de aankoop liggen.");
                var key = (originalId, split);
                recoveries[key] = recoveries.GetValueOrDefault(key) + a.Amount;
                Require(recoveries[key] <= maximum, "Retouren en vergoedingen samen overschrijden de oorspronkelijke uitgave.");
                if (a.Treatment == AllocationTreatment.Refund) refunds[key] = refunds.GetValueOrDefault(key) + a.Amount;
            }
            // Deliberately uncategorized recoveries remain expense reductions after category deletion.
        }
        var reserved = new Dictionary<(Guid, Guid?), decimal>();
        foreach (var c in data.ReimbursementClaims)
        {
            Require(!string.IsNullOrWhiteSpace(c.Counterparty) && c.Note != null && c.ExpectedAmount > 0 && Money(c.ExpectedAmount),
                "Vul een betaler en een positief verwacht bedrag met maximaal twee decimalen in.");
            var maximum = Expense(c.ExpenseTransactionId, c.ExpenseAllocationId);
            Require(c.DueDate == null || c.DueDate >= transactions[c.ExpenseTransactionId].Date, "Verwachte betaaldatum ligt vóór de aankoop.");
            var paid = data.TransactionAllocations.Where(a => a.ReimbursementClaimId == c.Id).Sum(a => a.Amount);
            Require(paid <= c.ExpectedAmount, "Ontvangen vergoedingen overschrijden het verwachte bedrag.");
            var key = (c.ExpenseTransactionId, c.ExpenseAllocationId);
            reserved[key] = reserved.GetValueOrDefault(key) + (c.IsClosed ? paid : c.ExpectedAmount);
            // Untracked reimbursements also occupy the expense's recovery capacity.
            var untracked = data.TransactionAllocations.Where(a => a.Treatment == AllocationTreatment.Reimbursement &&
                a.ReimbursementClaimId == null && a.OriginalTransactionId == key.Item1 && a.OriginalAllocationId == key.Item2).Sum(a => a.Amount);
            Require(reserved[key] + refunds.GetValueOrDefault(key) + untracked <= maximum,
                "Vergoedingsverwachtingen en retouren overschrijden de uitgave. Pas de verwachting eerst aan of sluit deze.");
        }
    }
}
