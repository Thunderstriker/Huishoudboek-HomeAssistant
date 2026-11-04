using System.Globalization;
using Huishoudboek.Core;

namespace Huishoudboek.Web;

/// <summary>Detached editor values. Editing/cancelling never mutates the shared administration.</summary>
public sealed class AllocationDraft
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Amount { get; set; } = "0";
    public Guid? CategoryId { get; set; }
    public AllocationTreatment Treatment { get; set; }
    public string Source { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class ClaimDraft
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? AllocationId { get; set; }
    public string Counterparty { get; set; } = "";
    public string Amount { get; set; } = "0";
    public string DueDate { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsClosed { get; set; }
}

public sealed class TransactionDetailsDraft
{
    public Guid TransactionId { get; }
    public string Revision { get; }
    public bool UseAllocations { get; set; }
    public List<AllocationDraft> Allocations { get; } = [];
    public List<ClaimDraft> Claims { get; } = [];
    public string Error { get; set; } = "";

    public TransactionDetailsDraft(WebState state, Transaction transaction)
    {
        TransactionId = transaction.Id; Revision = state.Revision;
        var lines = TransactionAccounting.Lines(state.Data, new[] { transaction }).Where(l => l.AllocationId != null)
            .ToDictionary(l => l.AllocationId!.Value);
        foreach (var a in state.Data.TransactionAllocations.Where(a => a.TransactionId == transaction.Id))
            Allocations.Add(new() { Id = a.Id, Amount = Format(a.Amount), CategoryId = lines[a.Id].CategoryId, Treatment = a.Treatment,
                Note = a.Note, Source = a.ReimbursementClaimId is { } claim ? "claim:" + claim :
                    a.OriginalTransactionId is { } source ? SourceKey(source, a.OriginalAllocationId) : "" });
        UseAllocations = Allocations.Count > 0;
        if (!UseAllocations) Allocations.Add(new() { Amount = Format(transaction.Amount), CategoryId = transaction.CategoryId });
        foreach (var c in state.Data.ReimbursementClaims.Where(c => c.ExpenseTransactionId == transaction.Id))
            Claims.Add(new() { Id = c.Id, AllocationId = c.ExpenseAllocationId, Counterparty = c.Counterparty,
                Amount = Format(c.ExpectedAmount), DueDate = c.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                Note = c.Note, IsClosed = c.IsClosed });
    }

    public static string Format(decimal amount) => amount.ToString("0.00", UiCulture.Culture);
    public static decimal Parse(string value) => decimal.TryParse(value.Trim(), NumberStyles.AllowLeadingSign |
        NumberStyles.AllowDecimalPoint, UiCulture.Culture, out var amount) ? amount :
        throw new InvalidOperationException(UiCulture.Text("InvalidAllocationAmount"));
    public static string SourceKey(Guid transactionId, Guid? allocationId) => transactionId + "|" + allocationId;

    public void Apply(HouseholdData data)
    {
        var allocations = UseAllocations ? Allocations.Select(a =>
        {
            var item = new TransactionAllocation { Id = a.Id, Amount = Parse(a.Amount), CategoryId = a.CategoryId,
                Treatment = a.Treatment, Note = a.Note };
            if (a.Treatment != AllocationTreatment.Ordinary && a.Source.Length > 0)
            {
                item.CategoryId = null;
                if (a.Source.StartsWith("claim:", StringComparison.Ordinal)) item.ReimbursementClaimId = Guid.Parse(a.Source[6..]);
                else
                {
                    var parts = a.Source.Split('|'); item.OriginalTransactionId = Guid.Parse(parts[0]);
                    item.OriginalAllocationId = parts[1].Length == 0 ? null : Guid.Parse(parts[1]);
                }
            }
            return item;
        }).ToList() : [];
        if (UseAllocations && (allocations.Count == 0 || allocations.Sum(a => a.Amount) != data.Transactions.Single(t => t.Id == TransactionId).Amount))
            throw new InvalidOperationException(UiCulture.Text("AllocationTotalRequired"));
        var claims = Claims.Select(c => new ReimbursementClaim { Id = c.Id, ExpenseAllocationId = c.AllocationId,
            Counterparty = c.Counterparty, ExpectedAmount = Parse(c.Amount), Note = c.Note, IsClosed = c.IsClosed,
            DueDate = c.DueDate.Length == 0 ? null : DateOnly.ParseExact(c.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture) }).ToList();
        TransactionAccounting.SaveDetails(data, TransactionId, allocations, claims);
    }
}
