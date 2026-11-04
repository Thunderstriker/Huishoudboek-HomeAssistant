namespace Huishoudboek.Core;

public enum AccountKind { Current, Savings, Investment }
public enum TransactionKind { Normal, Transfer, Mortgage }
public enum PaymentFrequency { Monthly, Quarterly, Yearly, Once }
public enum AllocationTreatment { Ordinary, Refund, Reimbursement }
public enum CategoryPurpose { Normal, Savings, OwnAccountMovement }

public sealed record Owner
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
}

public sealed record Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public AccountKind Kind { get; set; }
    public string BankReference { get; set; } = "";
    public List<Guid> OwnerIds { get; set; } = [];
    public decimal OpeningBalance { get; set; }
    public DateOnly OpeningDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal? SavingsGoal { get; set; }
    public decimal MinimumCashBuffer { get; set; }
}

public sealed record Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public decimal MonthlyBudget { get; set; }
    public CategoryPurpose Purpose { get; set; }
}

public sealed record BudgetOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CategoryId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
}

public sealed record Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string Description { get; set; } = "";
    public string UserDescription { get; set; } = "";
    public decimal Amount { get; set; }
    public Guid? CategoryId { get; set; }
    public TransactionKind Kind { get; set; }
    public Guid? CounterAccountId { get; set; }
    public decimal PrincipalComponent { get; set; }
    public Guid? RecurringPaymentId { get; set; }
    public DateOnly? RecurringOccurrenceDate { get; set; }
    public Guid? ImportBatchId { get; set; }
    public decimal? StatementBalance { get; set; }
}

/// <summary>Reporting allocations only; the parent bank entry retains its original cash effect.</summary>
public sealed record TransactionAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransactionId { get; set; }
    public decimal Amount { get; set; }
    public Guid? CategoryId { get; set; }
    public string Note { get; set; } = "";
    public AllocationTreatment Treatment { get; set; }
    public Guid? OriginalTransactionId { get; set; }
    public Guid? OriginalAllocationId { get; set; }
    public Guid? ReimbursementClaimId { get; set; }
}

/// <summary>An expectation, not a bank entry or spendable cash. Expenses remain until paid.</summary>
public sealed record ReimbursementClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExpenseTransactionId { get; set; }
    public Guid? ExpenseAllocationId { get; set; }
    public string Counterparty { get; set; } = "";
    public decimal ExpectedAmount { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Note { get; set; } = "";
    public bool IsClosed { get; set; }
}

public sealed record RecurringPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid AccountId { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal Amount { get; set; }
    public int DayOfMonth { get; set; } = 1;
    public PaymentFrequency Frequency { get; set; }
    public decimal MatchTolerance { get; set; } = 0.01m;
    public int MatchWindowDays { get; set; } = 5;
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? EndDate { get; set; }
    public string MatchText { get; set; } = "";
    public bool IsMortgage { get; set; }
}

/// <summary>A reporting-only decision about two existing entries; never changes their account effects.</summary>
public sealed record TransferMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DebitTransactionId { get; set; }
    public Guid CreditTransactionId { get; set; }
    public bool IsConfirmed { get; set; } = true;
}

public sealed record CategorizationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Contains { get; set; } = "";
    public Guid CategoryId { get; set; }
}

public sealed record InvestmentValuation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    // Securities only. The account's cash balance is calculated separately.
    public decimal SecuritiesValue { get; set; }
}

public sealed record Mortgage
{
    public string Name { get; set; } = "Hypotheek";
    public List<Guid> OwnerIds { get; set; } = [];
    public decimal ReferencePrincipal { get; set; }
    public DateOnly ReferenceDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal AnnualInterestPercent { get; set; }
    public decimal MonthlyPayment { get; set; }
    public int RemainingMonths { get; set; } = 360;
    public DateOnly? FixedRateUntil { get; set; }
}

public sealed record ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public Guid AccountId { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.Now;
    public int RowCount { get; set; }
    public bool Undone { get; set; }
}

public sealed record ImportProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string DateColumn { get; set; } = "";
    public string DescriptionColumn { get; set; } = "";
    public string ExtraDescriptionColumn { get; set; } = "";
    public string AmountColumn { get; set; } = "";
    public string DebitColumn { get; set; } = "";
    public string CreditColumn { get; set; } = "";
    public string DirectionColumn { get; set; } = "";
    public string CurrencyColumn { get; set; } = "";
    public string AccountReferenceColumn { get; set; } = "";
    // Retained for database compatibility; statement imports do not link counterparties to accounts.
    public string CounterAccountReferenceColumn { get; set; } = "";
    public string BalanceColumn { get; set; } = "";
    public string FallbackDescriptionColumn { get; set; } = "";
    public string CultureName { get; set; } = "nl-NL";
    public string DateFormat { get; set; } = "";
}

public sealed class HouseholdData
{
    public List<Owner> Owners { get; set; } = [];
    public List<Account> Accounts { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<BudgetOverride> BudgetOverrides { get; set; } = [];
    public List<Transaction> Transactions { get; set; } = [];
    public List<TransactionAllocation> TransactionAllocations { get; set; } = [];
    public List<ReimbursementClaim> ReimbursementClaims { get; set; } = [];
    public List<TransferMatch> TransferMatches { get; set; } = [];
    public List<RecurringPayment> RecurringPayments { get; set; } = [];
    public List<CategorizationRule> Rules { get; set; } = [];
    public List<InvestmentValuation> Valuations { get; set; } = [];
    public List<ImportBatch> ImportBatches { get; set; } = [];
    public List<ImportProfile> ImportProfiles { get; set; } = [];
    public List<AccountReview> AccountReviews { get; set; } = [];
    public Mortgage? Mortgage { get; set; }

    public static HouseholdData CreateEmpty() => new()
    {
        Categories = new[] { "Huishouden", "Wonen & hypotheek", "Voeding", "Verjaardagen & cadeaus",
            "Vrije tijd", "Verzekeringen", "Auto & vervoer", "Medische kosten", "Energie & water",
            "Abonnementen", "Kleding", "Overig", "Salaris", "Dividend & rente" }
            .Select(name => new Category { Name = name }).Concat(new[] {
                new Category { Name = "Sparen & beleggen", Purpose = CategoryPurpose.Savings },
                new Category { Name = "Tussen eigen rekeningen", Purpose = CategoryPurpose.OwnAccountMovement }
            }).ToList()
    };
}

public interface IHouseholdStore
{
    HouseholdData Load();
    void Save(HouseholdData data);
    void Backup(string destination);
    void Restore(string source);
}
