namespace Huishoudboek.Core;

public enum ContractKind { Streaming, Mobile, Internet, TelecomBundle, Music, Software, CloudStorage, Gaming, Gym, News, Insurance, Maintenance, Other }
public enum ContractStatus { Active, Trial, CancellationRequested, Cancelled, Expired }
public enum ContractBillingCycle { Weekly, Monthly, Quarterly, HalfYearly, Yearly }
public enum ContractNoticeUnit { Days, Months }

/// <summary>Contract metadata only. Never creates payments or modifies a recurring payment.</summary>
public sealed record SubscriptionContract
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Plan { get; set; } = "";
    public ContractKind Kind { get; set; } = ContractKind.Other;
    public ContractStatus Status { get; set; }
    public string CountryCode { get; set; } = "BE";
    public string Postcode { get; set; } = "";
    public Guid? OwnerId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? AccountId { get; set; }
    public string ExternalPaymentLabel { get; set; } = "";
    public Guid? RecurringPaymentId { get; set; }
    public decimal Amount { get; set; }
    public ContractBillingCycle BillingCycle { get; set; } = ContractBillingCycle.Monthly;
    public decimal? RegularAmount { get; set; }
    public DateOnly? StartDate { get; set; }
    public bool IsIndefinite { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly? RenewalDate { get; set; }
    public DateOnly? MinimumCommitmentUntil { get; set; }
    public int? NoticePeriod { get; set; }
    public ContractNoticeUnit NoticeUnit { get; set; }
    public DateOnly? CancellationDeadline { get; set; }
    public DateOnly? TrialEndsOn { get; set; }
    public DateOnly? PromotionEndsOn { get; set; }
    public DateOnly? ReviewOn { get; set; }
    public string CancellationUrl { get; set; } = "";
    public string ContractUrl { get; set; } = "";
    public string Notes { get; set; } = "";
    public decimal? DownloadMbps { get; set; }
    public decimal? UploadMbps { get; set; }
    public decimal? DataLimitGb { get; set; }
    public bool UnlimitedData { get; set; }
    public string NetworkAndConditions { get; set; } = "";
    public int? SimultaneousStreams { get; set; }
    public string VideoQuality { get; set; } = "";
    public bool? HasAds { get; set; }
}

public sealed record ContractAttention(SubscriptionContract Contract, string Reason, DateOnly? Date);
public sealed record ContractCostOverview(decimal TrackedAnnual, decimal ExternalAnnual, int UnknownPriceCount)
{
    public decimal Annual => TrackedAnnual + ExternalAnnual;
}

public static class SubscriptionContracts
{
    public static bool IsOpen(SubscriptionContract contract) => contract.Status is not (ContractStatus.Cancelled or ContractStatus.Expired);
    public static bool IsCurrent(SubscriptionContract contract, DateOnly today) => IsOpen(contract) &&
        (contract.StartDate == null || contract.StartDate <= today) && (contract.EndDate == null || contract.EndDate >= today);

    public static IEnumerable<SubscriptionContract> Visible(HouseholdData data, Guid? ownerId) => data.Contracts.Where(c =>
        ownerId == null || (c.OwnerId != null ? c.OwnerId == ownerId : c.AccountId == null ||
            data.Accounts.Any(a => a.Id == c.AccountId && a.OwnerIds.Contains(ownerId.Value))));

    public static decimal AnnualFactor(ContractBillingCycle cycle) => cycle switch
    {
        ContractBillingCycle.Weekly => 52, ContractBillingCycle.Monthly => 12,
        ContractBillingCycle.Quarterly => 4, ContractBillingCycle.HalfYearly => 2,
        ContractBillingCycle.Yearly => 1, _ => throw new InvalidOperationException("Onbekende facturatieperiode.")
    };

    public static decimal? CurrentAmount(SubscriptionContract contract, DateOnly today)
    {
        if (contract.Status == ContractStatus.Trial && contract.TrialEndsOn < today) return null;
        if (contract.PromotionEndsOn < today) return contract.RegularAmount;
        return contract.Amount;
    }

    // A run rate at today's price, not next year's invoices or recorded household spending.
    public static ContractCostOverview Costs(IEnumerable<SubscriptionContract> contracts, DateOnly today)
    {
        decimal tracked = 0, external = 0; var unknown = 0;
        foreach (var contract in contracts.Where(c => IsCurrent(c, today)))
        {
            if (CurrentAmount(contract, today) is not { } price) { unknown++; continue; }
            var annual = price * AnnualFactor(contract.BillingCycle);
            if (contract.AccountId == null) external += annual; else tracked += annual;
        }
        return new(tracked, external, unknown);
    }

    public static DateOnly? Deadline(SubscriptionContract contract)
    {
        if (contract.CancellationDeadline is { } explicitDate) return explicitDate;
        if (contract.RenewalDate is not { } renewal || contract.NoticePeriod is not { } period) return null;
        // Months are calendar months, never an assumed 30 days. Unknown terms remain unknown.
        return contract.NoticeUnit == ContractNoticeUnit.Months ? renewal.AddMonths(-period) : renewal.AddDays(-period);
    }

    public static IReadOnlyList<ContractAttention> Attention(IEnumerable<SubscriptionContract> contracts, DateOnly today, int horizonDays = 30)
    {
        var result = new List<ContractAttention>(); var through = today.AddDays(horizonDays);
        foreach (var contract in contracts.Where(IsOpen))
        {
            void Add(string reason, DateOnly? date) { if (date != null && date <= through) result.Add(new(contract, reason, date)); }
            if (contract.Status == ContractStatus.CancellationRequested) result.Add(new(contract, "ContractCancellationPending", contract.EndDate));
            else
            {
                var deadline = Deadline(contract);
                Add("ContractCancellationDue", deadline);
                Add("ContractRenewalDue", contract.RenewalDate);
                if (!contract.IsIndefinite)
                {
                    if (contract.RenewalDate == null && contract.Status != ContractStatus.Trial)
                        result.Add(new(contract, "ContractRenewalUnknown", null));
                    if (deadline == null) result.Add(new(contract, "ContractDeadlineUnknown", null));
                }
            }
            if (contract.Status == ContractStatus.Trial) Add("ContractTrialDue", contract.TrialEndsOn);
            if (contract.Status == ContractStatus.Trial && contract.TrialEndsOn == null) result.Add(new(contract, "ContractTrialUnknown", null));
            Add("ContractPromotionDue", contract.PromotionEndsOn);
            Add("ContractReviewDue", contract.ReviewOn);
            Add("ContractEndDue", contract.EndDate);
        }
        return result.OrderBy(a => a.Date == null ? 2 : a.Date <= today ? 0 : 1).ThenBy(a => a.Date).ThenBy(a => a.Contract.Name).ToList();
    }

    public static bool SafeLink(string? value) => value != null && (value.Length == 0 ||
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" &&
        uri.UserInfo.Length == 0 && !value.Any(char.IsControl));

    public static void Save(HouseholdData data, SubscriptionContract contract)
    {
        contract = contract with { Name = contract.Name.Trim(), Provider = contract.Provider.Trim(), Plan = contract.Plan.Trim(),
            CountryCode = contract.CountryCode.Trim().ToUpperInvariant(), Postcode = contract.Postcode.Trim(),
            ExternalPaymentLabel = contract.AccountId == null ? contract.ExternalPaymentLabel.Trim() : "",
            Notes = contract.Notes.Trim(), CancellationUrl = contract.CancellationUrl.Trim(), ContractUrl = contract.ContractUrl.Trim(),
            VideoQuality = contract.VideoQuality.Trim(), NetworkAndConditions = contract.NetworkAndConditions.Trim() };
        var index = data.Contracts.FindIndex(c => c.Id == contract.Id);
        if (index < 0) data.Contracts.Add(contract); else data.Contracts[index] = contract;
    }

    public static void Validate(HouseholdData data)
    {
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static bool Price(decimal? value) => value == null || value is >= 0 and <= 1_000_000_000m && decimal.Round(value.Value, 2) == value;
        static bool Quantity(decimal? value) => value == null || value is > 0 and <= 1_000_000m && decimal.Round(value.Value, 2) == value;
        Require(data.Contracts.Select(c => c.Id).Distinct().Count() == data.Contracts.Count && data.Contracts.All(c => c.Id != Guid.Empty), "Ongeldige of dubbele contractidentificatie.");
        Require(data.Contracts.Where(c => c.RecurringPaymentId != null).Select(c => c.RecurringPaymentId).Distinct().Count() == data.Contracts.Count(c => c.RecurringPaymentId != null), "Een vaste post kan maar aan één contract worden gekoppeld.");
        foreach (var c in data.Contracts)
        {
            Require(c.Name is { Length: > 0 and <= 200 } && !string.IsNullOrWhiteSpace(c.Name) && c.Provider is { Length: > 0 and <= 200 } && !string.IsNullOrWhiteSpace(c.Provider), "Vul een contractnaam en aanbieder in (maximaal 200 tekens).");
            Require(c.Plan is { Length: <= 200 } && c.Postcode is { Length: <= 20 } && c.ExternalPaymentLabel is { Length: <= 200 } &&
                c.Notes is { Length: <= 2000 } && c.NetworkAndConditions is { Length: <= 2000 } && c.VideoQuality is { Length: <= 100 }, "Contracttekst ontbreekt of is te lang.");
            Require(c.CountryCode is { Length: 2 } && c.CountryCode.All(ch => ch is >= 'A' and <= 'Z'), "Gebruik een landcode van twee hoofdletters, bijvoorbeeld BE.");
            Require(Enum.IsDefined(c.Kind) && Enum.IsDefined(c.Status) && Enum.IsDefined(c.BillingCycle) && Enum.IsDefined(c.NoticeUnit), "Onbekend contracttype, status of periode.");
            Require(Price(c.Amount) && Price(c.RegularAmount), "Contractprijzen zijn niet-negatief en hebben maximaal twee decimalen.");
            Require(c.OwnerId == null || data.Owners.Any(o => o.Id == c.OwnerId), "Onbekende contracteigenaar.");
            Require(c.AccountId == null || data.Accounts.Any(a => a.Id == c.AccountId), "Onbekende betaalrekening bij het contract.");
            Require(c.AccountId == null || c.ExternalPaymentLabel.Length == 0, "Een geregistreerde rekening heeft geen extern betaallabel.");
            Require(c.CategoryId == null || data.Categories.Any(cat => cat.Id == c.CategoryId), "Onbekende contractcategorie.");
            Require(c.RecurringPaymentId == null || c.AccountId != null && data.RecurringPayments.Any(r => r.Id == c.RecurringPaymentId && r.AccountId == c.AccountId && r.Amount < 0 && !r.IsMortgage && r.Frequency != PaymentFrequency.Once), "Kies een bestaande terugkerende uitgaande vaste post op dezelfde rekening, zonder hypotheek.");
            Require(c.NoticePeriod is null or >= 0 and <= 120, "Kies een opzegtermijn van 0–120 dagen of maanden, of laat deze onbekend.");
            Require(c.RenewalDate == null || c.NoticePeriod == null || (c.NoticeUnit == ContractNoticeUnit.Months
                ? c.RenewalDate.Value.Year * 12 + c.RenewalDate.Value.Month - 1 >= 12 + c.NoticePeriod
                : c.RenewalDate.Value.DayNumber >= c.NoticePeriod), "De opzegtermijn valt buiten het ondersteunde datumbereik.");
            Require(c.StartDate == null || new[] { c.EndDate, c.RenewalDate, c.MinimumCommitmentUntil, c.TrialEndsOn, c.PromotionEndsOn }.All(date => date == null || date >= c.StartDate), "Een contractdatum ligt vóór de startdatum.");
            Require(SafeLink(c.CancellationUrl) && SafeLink(c.ContractUrl) && c.CancellationUrl.Length <= 2000 && c.ContractUrl.Length <= 2000, "Gebruik een geldige http(s)-link zonder inloggegevens.");
            Require(Quantity(c.DownloadMbps) && Quantity(c.UploadMbps) && Quantity(c.DataLimitGb) && (!c.UnlimitedData || c.DataLimitGb == null), "Snelheden en datalimieten moeten positief zijn; kies een limiet óf onbeperkt.");
            Require(c.SimultaneousStreams is null or >= 1 and <= 100, "Kies 1–100 gelijktijdige streams, of laat dit onbekend.");
        }
    }
}
