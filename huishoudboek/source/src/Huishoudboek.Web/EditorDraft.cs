using System.Globalization;
using System.Text.Json;
using Huishoudboek.Core;
using static Huishoudboek.Web.UiCulture;

namespace Huishoudboek.Web;

public sealed record FieldOption(string Value, string Label);
public sealed record EditorSaveProposal(int MatchCount, Action<HouseholdData> SaveOnly, Action<HouseholdData> SaveAndLink)
{
    public bool ReviewRecognition { get; init; }
    public IReadOnlyList<RecognitionChange> Changes { get; init; } = [];
    public IReadOnlyList<Transaction> Samples { get; init; } = [];
    public int PreviewCount { get; init; }
    public int AmbiguousNameCount { get; init; }
    public int EarlierCategoryCount { get; init; }
    public IReadOnlySet<Guid> InitialSelection { get; init; } = new HashSet<Guid>();
    public Action<HouseholdData, IReadOnlySet<Guid>>? SaveSelected { get; init; }
}
public sealed class EditorField(string key, string label, string type, string value = "", bool required = true)
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Type { get; } = type;
    public string Value { get; set; } = value;
    public bool Required { get; } = required;
    public bool ReadOnly { get; set; }
    public string Error { get; set; } = "";
    public string Section { get; set; } = "";
    public List<FieldOption> Options { get; set; } = [];
    public HashSet<Guid> OwnerIds { get; set; } = [];
}

/// <summary>Presentation-only draft. No changes touch the shared snapshot before an explicit save.</summary>
public sealed class EditorDraft(string title, string revision)
{
    public string Title { get; } = title;
    public string Revision { get; } = revision;
    public string Note { get; private set; } = "";
    public string Error { get; set; } = "";
    public string SaveLabel { get; private set; } = UiCulture.Text("Save");
    public string SuccessMessage { get; private set; } = UiCulture.Text("ChangesSaved");
    public List<EditorField> Fields { get; } = [];
    public Action<HouseholdData> Apply { get; private set; } = _ => { };
    private Guid? _seedTransactionId;
    private Guid? _recognitionRuleId;
    private IReadOnlySet<Guid>? _recognitionScope;
    public bool IsRecognition => _recognitionRuleId != null;
    public Guid? RecognitionRuleId => _recognitionRuleId;
    public Guid? SeedTransactionId => _seedTransactionId;
    private bool _transaction;
    private bool _category;
    private bool _mortgage;
    private bool _simpleTransaction;
    private bool _contract;
    private IReadOnlyList<RecurringPayment> _contractPayments = [];
    public string Fingerprint => JsonSerializer.Serialize(new { Rule = _recognitionRuleId,
        Fields = Fields.Select(f => new { f.Key, f.Value, Owners = f.OwnerIds.OrderBy(id => id) }) });

    public bool ValidateFields()
    {
        foreach (var field in Fields) field.Error = "";
        foreach (var field in Fields.Where(f => IsFieldVisible(f) && !f.ReadOnly))
        {
            var text = field.Value.Trim();
            if (field.Type == "owners") { if (field.OwnerIds.Count == 0) field.Error = UiCulture.Text("FieldRequired"); continue; }
            if (field.Required && text.Length == 0) { field.Error = UiCulture.Text("FieldRequired"); continue; }
            if (field.Type == "select" && !Options(field).Any(o => o.Value == field.Value)) field.Error = UiCulture.Text("FieldInvalidChoice");
            if (text.Length == 0) continue;
            if (field.Type == "date" && !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                field.Error = UiCulture.Text("InvalidDate");
            if (field.Type == "number" && !int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                field.Error = UiCulture.Text("FieldInteger");
            if (_contract && field.Type == "url" && !SubscriptionContracts.SafeLink(text)) field.Error = UiCulture.Text("ContractLinkInvalid");
            if (field.Type == "money")
            {
                if (!TryNumber(text, out var number) || (field.Key is not ("rate" or "monthlyRate") && decimal.Round(number, 2) != number))
                    field.Error = UiCulture.Text("InvalidAmount", Culture.NumberFormat.NumberDecimalSeparator);
                else if (field.Key is "budget" or "buffer" or "earmarked" or "goal" or "value" or "tolerance" && number < 0)
                    field.Error = UiCulture.Text("FieldNonnegative");
                else if (_simpleTransaction && field.Key == "amount" && number <= 0) field.Error = UiCulture.Text("FieldPositive");
                else if (_contract && field.Key is "contractAmount" or "regularAmount" && (number < 0 || number > 1_000_000_000m)) field.Error = UiCulture.Text("ContractPriceInvalid");
                else if (_contract && field.Key is "download" or "upload" or "dataLimit" && (number <= 0 || number > 1_000_000m)) field.Error = UiCulture.Text("ContractQuantityInvalid");
            }
            if (field.Key == "day" && int.TryParse(text, out var day) && day is < 1 or > 31) field.Error = UiCulture.Text("FieldDayRange");
            if (_contract && field.Key == "noticePeriod" && int.TryParse(text, out var notice) && notice is < 0 or > 120) field.Error = UiCulture.Text("ContractNoticeInvalid");
            if (_contract && field.Key == "streams" && int.TryParse(text, out var streams) && streams is < 1 or > 100) field.Error = UiCulture.Text("ContractStreamsInvalid");
            if (_contract && field.Key == "country" && (text.Length != 2 || !text.All(char.IsAsciiLetter))) field.Error = UiCulture.Text("ContractCountryInvalid");
            if (_contract && text.Length > (field.Key is "name" or "provider" or "plan" or "externalPayment" ? 200 : field.Key == "postcode" ? 20 : field.Key == "videoQuality" ? 100 : 2000))
                field.Error = UiCulture.Text("ContractTextTooLong");
        }
        if (IsRecognition && Text("planning").Length > 0 && Text("account").Length == 0)
            Fields.Single(f => f.Key == "account").Error = UiCulture.Text("FieldRequired");
        if (Fields.FirstOrDefault(f => f.Key == "complete") is { } consent && !bool.Parse(consent.Value))
            consent.Error = UiCulture.Text("AccountPeriodConsentRequired");
        var start = Fields.FirstOrDefault(f => f.Key is "start" or "from");
        var end = Fields.FirstOrDefault(f => f.Key is "end" or "through");
        if (start != null && end != null && IsFieldVisible(start) && IsFieldVisible(end) &&
            DateOnly.TryParse(start.Value, out var from) && DateOnly.TryParse(end.Value, out var through) && through < from)
            end.Error = UiCulture.Text("FieldEndBeforeStart");
        if (_contract && start != null && DateOnly.TryParseExact(start.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var contractStart))
            foreach (var field in Fields.Where(f => f.Key is "renewal" or "commitment" or "trialEnd" or "promotionEnd"))
                if (DateOnly.TryParseExact(field.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < contractStart)
                    field.Error = UiCulture.Text("FieldEndBeforeStart");
        return Fields.All(f => f.Error.Length == 0);
    }
    private static bool TryNumber(string text, out decimal value) => decimal.TryParse(text.Trim().Replace(',', '.'),
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    public string Label(EditorField field) => _category && field.Key == "budget" && Text("purpose") == "Savings"
        ? UiCulture.Text("DefaultMonthlyTargetEuro") : field.Label;
    public bool IsFieldVisible(EditorField field) => _contract ? field.Key switch
    {
        "externalPayment" => Text("paymentAccount").Length == 0,
        "contractRecurring" => Text("paymentAccount").Length > 0,
        "download" or "upload" or "unlimited" or "network" => Text("contractKind") is "Mobile" or "Internet" or "TelecomBundle",
        "dataLimit" => Text("contractKind") is "Mobile" or "Internet" or "TelecomBundle" && !bool.Parse(Text("unlimited")),
        "streams" or "videoQuality" or "ads" => Text("contractKind") == "Streaming",
        _ => true
    } : IsRecognition && field.Key is "amount" or "day" or "frequency" or "amountMode" or "start" or "end" or "mortgage" or "window"
        ? Text("planning").Length > 0 : IsRecognition && field.Key == "tolerance"
        ? Text("planning").Length > 0 && Text("amountMode") == "Fixed" : IsRecognition && field.Key == "direction"
        ? Text("planning").Length == 0 : _category && field.Key is "essential" or "exceptional"
        ? Text("purpose") == "Normal" : _category && field.Key == "budget"
        ? Text("purpose") != "OwnAccountMovement" : _mortgage && field.Key == "monthlyRate"
        ? Text("rateConvention") == "Monthly" : !_transaction || field.Key switch
    {
        "counter" => Text("kind") == "Transfer",
        "principal" => Text("kind") == "Mortgage",
        "recurring" => Text("kind") != "Transfer",
        "occurrence" => Text("kind") != "Transfer" && Text("recurring").Length > 0,
        _ => true
    };
    public IEnumerable<FieldOption> Options(EditorField field) => _contract && field.Key == "contractRecurring"
        ? field.Options.Where(option => option.Value.Length == 0 || _contractPayments.Any(p => p.Id.ToString() == option.Value && p.AccountId.ToString() == Text("paymentAccount")))
        : field.Options;
    public void FieldChanged(EditorField field)
    {
        if (_contract && field.Key == "paymentAccount") Fields.Single(f => f.Key == "contractRecurring").Value = "";
    }
    public EditorSaveProposal PrepareSave(HouseholdData data, DateOnly today)
    {
        if (_recognitionRuleId is not { } ruleId) return new(0, Apply, Apply);
        // Preview on an isolated copy. Saving settings alone never applies them to history.
        var candidate = JsonSerializer.Deserialize<HouseholdData>(JsonSerializer.SerializeToUtf8Bytes(data))!;
        Apply(candidate);
        Validation.Validate(candidate);
        var rule = candidate.Rules.Single(r => r.Id == ruleId);
        var recognition = new RecognitionMatcher(candidate);
        var changes = TransactionRecognition.PreviewHistory(candidate, today, ruleId, _recognitionScope);
        var transfers = TransferMatching.ConfirmedTransactionIds(candidate);
        var matches = candidate.Transactions.Where(t => t.Kind != TransactionKind.Transfer && !transfers.Contains(t.Id) &&
            (_recognitionScope == null || _recognitionScope.Contains(t.AccountId)) && recognition.Matches(rule, t)).ToList();
        void SaveSelected(HouseholdData d, IReadOnlySet<Guid> selected)
        {
            Apply(d);
            TransactionRecognition.ApplyHistory(d, selected, today, ruleId, _recognitionScope);
        }
        void SaveAndApply(HouseholdData d) => SaveSelected(d, changes.Select(m => m.Before.Id).ToHashSet());
        return new(changes.Count, Apply, SaveAndApply) { ReviewRecognition = true, Changes = changes, SaveSelected = SaveSelected,
            PreviewCount = matches.Count, Samples = matches.OrderByDescending(t => t.Date).Take(5).ToList(),
            AmbiguousNameCount = matches.Count(recognition.Ambiguous),
            EarlierCategoryCount = matches.Count(t => rule.CategoryId != null && recognition.Category(t) is { } first && first.CategoryId != rule.CategoryId),
            InitialSelection = changes.Where(c => c.Before.Id == _seedTransactionId).Select(c => c.Before.Id).ToHashSet() };
    }
    public string Text(string key) => Fields.Single(f => f.Key == key).Value.Trim();
    public Guid Id(string key) => Guid.TryParse(Text(key), out var id) ? id : throw new InvalidOperationException(UiCulture.Text("InvalidAccountCategory"));
    public Guid? OptionalId(string key) => string.IsNullOrEmpty(Text(key)) ? null : Id(key);
    public decimal Number(string key)
    {
        if (!TryNumber(Text(key), out var value))
            throw new InvalidOperationException(UiCulture.Text("InvalidAmount", Culture.NumberFormat.NumberDecimalSeparator));
        return value;
    }
    public DateOnly Date(string key) => DateOnly.TryParseExact(Text(key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date : throw new InvalidOperationException(UiCulture.Text("InvalidDate"));
    private EditorField Field(string key, string label, string type, string value = "", bool required = true)
    { var field = new EditorField(key, UiCulture.Text(label), type, value, required); Fields.Add(field); return field; }
    private void Money(string key, string label, decimal value) => Field(key, label, "money", value.ToString("0.00", Culture));
    private void DateField(string key, string label, DateOnly? date, bool required = true) => Field(key, label, "date", date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", required);
    private void Owners(HouseholdData data, IEnumerable<Guid> selected)
    {
        var field = Field("owners", "Owners", "owners"); field.OwnerIds = selected.ToHashSet();
        field.Options = data.Owners.Select(o => new FieldOption(o.Id.ToString(), o.Name)).ToList();
    }
    private void Choice(string key, string label, IEnumerable<FieldOption> options, string value)
    { var field = Field(key, label, "select", value, false); field.Options = options.ToList(); }
    private static IEnumerable<FieldOption> Accounts(HouseholdData d) => d.Accounts.Select(a => new FieldOption(a.Id.ToString(), a.Name));
    private static IEnumerable<FieldOption> Categories(HouseholdData d) => new[] { new FieldOption("", UiCulture.Text("Uncategorized")) }
        .Concat(CategoryHierarchy.Ordered(d).Select(e => new FieldOption(e.Category.Id.ToString(), CategoryLabel(d, e.Category))));
    public static string CategoryLabel(HouseholdData data, Category category) => category.Purpose == CategoryPurpose.Normal
        ? CategoryHierarchy.Path(data, category.Id) : $"{CategoryHierarchy.Path(data, category.Id)} · {PurposeLabel(category.Purpose)}";
    public static string CategoryLabel(Category category) => category.Purpose == CategoryPurpose.Normal
        ? category.Name : $"{category.Name} · {PurposeLabel(category.Purpose)}";
    public static string PurposeLabel(CategoryPurpose purpose) => UiCulture.Text(purpose switch
    {
        CategoryPurpose.Savings => "SavingsPurpose",
        CategoryPurpose.OwnAccountMovement => "OwnAccountPurpose",
        _ => "NormalPurpose"
    });

    public static EditorDraft Owner(WebState s, Owner? original = null)
    {
        var item = original ?? new Owner(); var draft = new EditorDraft(UiCulture.Text("Owner"), s.Revision);
        draft.Field("name", "Name", "text", item.Name);
        draft.Apply = d => HouseholdEdits.SaveOwner(d, item with { Name = draft.Text("name") }); return draft;
    }
    public static EditorDraft Account(WebState s, Account? original = null)
    {
        var item = original ?? new Account { OwnerIds = s.Data.Owners.Select(o => o.Id).ToList(), OpeningDate = new DateOnly(s.Today.Year, 1, 1) };
        var draft = new EditorDraft(UiCulture.Text("Account"), s.Revision) { Note = UiCulture.Text("AccountEditorNote") + " " + UiCulture.Text("FreedomAccountHelp") };
        draft.Field("name", "Name", "text", item.Name);
        draft.Choice("kind", "AccountType", Enum.GetValues<AccountKind>().Select(k => new FieldOption(k.ToString(), WebState.Kind(k))), item.Kind.ToString());
        draft.Field("reference", "BankReference", "text", item.BankReference, false); draft.Owners(s.Data, item.OwnerIds);
        draft.DateField("date", "OpeningDate", item.OpeningDate); draft.Money("balance", "OpeningBalanceEuro", item.OpeningBalance);
        draft.Field("goal", "SavingsGoalEuro", "money", item.SavingsGoal?.ToString("0.00", Culture) ?? "", false);
        draft.Money("buffer", "AccountBufferEuro", item.MinimumCashBuffer);
        draft.Field("freedom", "FreedomIncludeAccount", "checkbox", item.IncludeInFreedom.ToString());
        draft.Money("earmarked", "FreedomEarmarkedEuro", item.EarmarkedAmount);
        draft.Apply = d => HouseholdEdits.SaveAccount(d, item with { Name = draft.Text("name"), Kind = Enum.Parse<AccountKind>(draft.Text("kind")),
            BankReference = draft.Text("reference"), OwnerIds = draft.Fields.Single(f => f.Key == "owners").OwnerIds.ToList(), OpeningDate = draft.Date("date"),
            OpeningBalance = draft.Number("balance"), SavingsGoal = draft.Text("goal").Length == 0 ? null : draft.Number("goal"),
            MinimumCashBuffer = draft.Number("buffer"), IncludeInFreedom = bool.Parse(draft.Text("freedom")),
            EarmarkedAmount = draft.Number("earmarked") }); return draft;
    }
    public static EditorDraft CashBuffer(WebState s, Account item)
    {
        var draft = new EditorDraft(UiCulture.Text("CashBufferTitle", item.Name), s.Revision)
        { Note = UiCulture.Text("CashBufferEditorNote") };
        draft.Money("buffer", "BufferEuro", item.MinimumCashBuffer);
        draft.Apply = d => HouseholdEdits.SaveAccount(d, d.Accounts.Single(a => a.Id == item.Id) with { MinimumCashBuffer = draft.Number("buffer") });
        return draft;
    }
    public static EditorDraft BalanceCheck(WebState s, Account item)
    {
        var draft = new EditorDraft(UiCulture.Text("BankBalanceCheckTitle", item.Name), s.Revision)
        { Note = UiCulture.Text("BankBalanceCheckNote") };
        draft.DateField("date", "BalanceCheckDate", s.Today);
        draft.Field("balance", "BankBalanceEuro", "money");
        draft.Apply = d =>
        {
            var through = draft.Date("date"); var balance = draft.Number("balance");
            var account = d.Accounts.Single(a => a.Id == item.Id);
            if (through > s.Today || through < account.OpeningDate) throw new InvalidOperationException(UiCulture.Text("BalanceCheckDateError"));
            var calculated = Finance.Balance(d, account, through);
            if (balance != calculated) throw new InvalidOperationException(UiCulture.Text("BalanceCheckDifference", WebState.Money(calculated - balance)));
            FirstWeek.CheckBalance(d, item.Id, through, balance, s.Today);
        };
        return draft;
    }
    public static EditorDraft SimpleTransaction(WebState s, bool income = false)
    {
        var item = new Transaction { AccountId = s.Data.Accounts.FirstOrDefault(a => s.Scope.Contains(a.Id))?.Id ?? Guid.Empty, Date = s.Today };
        var draft = new EditorDraft(UiCulture.Text(income ? "AddIncome" : "AddExpense"), s.Revision)
        { Note = UiCulture.Text("SimpleEntryNote"), _simpleTransaction = true };
        draft.DateField("date", "Date", item.Date);
        draft.Choice("account", "Account", Accounts(s.Data), item.AccountId.ToString());
        draft.Field("description", "Description", "text");
        draft.Money("amount", "PositiveAmountEuro", 0);
        draft.Choice("category", "Category", Categories(s.Data), "");
        draft.Apply = d =>
        {
            var amount = draft.Number("amount");
            if (amount <= 0) throw new InvalidOperationException(UiCulture.Text("PositiveEntryAmountError"));
            HouseholdEdits.SaveTransaction(d, item with { Date = draft.Date("date"), AccountId = draft.Id("account"),
                Description = draft.Text("description"), Amount = income ? amount : -amount, CategoryId = draft.OptionalId("category") });
        };
        return draft;
    }
    public static EditorDraft Category(WebState s, Category? original = null, bool renameOnly = false, Guid? parentId = null)
    {
        var item = original ?? new Category { ParentId = parentId,
            Purpose = s.Data.Categories.FirstOrDefault(c => c.Id == parentId)?.Purpose ?? CategoryPurpose.Normal };
        var draft = new EditorDraft(UiCulture.Text(renameOnly ? "RenameCategory" : original == null && parentId != null ? "AddSubcategory" : "CategoryDefaultBudget"), s.Revision)
        { Note = UiCulture.Text(renameOnly ? "RenameCategoryNote" : "CategoryEditorNote") + (renameOnly ? "" : " " + UiCulture.Text("CategoryInsightHelp") + " " + UiCulture.Text("CategoryHierarchyHelp")) };
        draft.Field("name", "Name", "text", item.Name);
        if (!renameOnly)
        {
            draft._category = true;
            var excluded = CategoryHierarchy.SubtreeIds(s.Data, item.Id);
            draft.Choice("parent", "ParentCategory", new[] { new FieldOption("", UiCulture.Text("RootCategory")) }
                .Concat(CategoryHierarchy.Ordered(s.Data).Where(e => !excluded.Contains(e.Category.Id))
                    .Select(e => new FieldOption(e.Category.Id.ToString(), CategoryLabel(s.Data, e.Category)))), item.ParentId?.ToString() ?? "");
            draft.Choice("purpose", "CountsAs", Enum.GetValues<CategoryPurpose>().Select(p => new FieldOption(p.ToString(), PurposeLabel(p))), item.Purpose.ToString());
            draft.Money("budget", "DefaultMonthlyBudgetEuro", item.MonthlyBudget);
            draft.Field("essential", "CategoryEssential", "checkbox", item.IsEssential.ToString());
            draft.Field("exceptional", "CategoryExceptional", "checkbox", item.IsExceptional.ToString());
        }
        draft.Apply = d => { if (renameOnly) Categorization.RenameCategory(d, item.Id, draft.Text("name"));
            else HouseholdEdits.SaveCategory(d, item with { Name = draft.Text("name"), ParentId = draft.OptionalId("parent"), MonthlyBudget = draft.Number("budget"),
                Purpose = Enum.Parse<CategoryPurpose>(draft.Text("purpose")),
                IsEssential = draft.Text("purpose") == "Normal" && bool.Parse(draft.Text("essential")),
                IsExceptional = draft.Text("purpose") == "Normal" && bool.Parse(draft.Text("exceptional")) }); }; return draft;
    }
    public static EditorDraft FreedomTarget(WebState s)
    {
        var draft = new EditorDraft(UiCulture.Text("FreedomTarget"), s.Revision) { Note = UiCulture.Text("FreedomTargetHelp") };
        draft.Field("months", "FreedomTargetMonths", "number", s.Data.InsightSettings.BufferTargetMonths.ToString(CultureInfo.InvariantCulture));
        draft.Apply = d => d.InsightSettings = d.InsightSettings with { BufferTargetMonths = int.TryParse(draft.Text("months"), out var months) ? months : 0 };
        return draft;
    }
    public static EditorDraft Budget(WebState s, Category item)
    {
        var year = s.Year; var month = s.Month;
        var draft = new EditorDraft(UiCulture.Text("MonthlyBudgetTitle", item.Name, $"{month:D2}-{year}"), s.Revision) { Note = UiCulture.Text("MonthlyBudgetEditorNote") };
        draft.Money("budget", item.Purpose == CategoryPurpose.Savings ? "SavingsTargetEuro" : "BudgetEuro", s.Data.BudgetOverrides.FirstOrDefault(b => b.CategoryId == item.Id && b.Year == year && b.Month == month)?.Amount ?? item.MonthlyBudget);
        draft.Apply = d => HouseholdEdits.SetBudget(d, item.Id, year, month, draft.Number("budget")); return draft;
    }
    public static EditorDraft Transaction(WebState s, Transaction? original = null, bool mortgage = false)
    {
        var item = original ?? new Transaction { AccountId = s.Data.Accounts.FirstOrDefault()?.Id ?? Guid.Empty, Kind = mortgage ? TransactionKind.Mortgage : TransactionKind.Normal };
        var draft = new EditorDraft(UiCulture.Text("Transaction"), s.Revision) { Note = UiCulture.Text("TransactionEditorNote"), _transaction = true };
        if (s.Data.TransferMatches.Any(m => m.DebitTransactionId == item.Id || m.CreditTransactionId == item.Id))
            draft.Note += " " + UiCulture.Text("TransactionTransferNote");
        draft.DateField("date", "Date", item.Date); draft.Choice("account", "TransactionAccount", Accounts(s.Data), item.AccountId.ToString());
        draft.Field("description", "OriginalDescription", "text", item.Description);
        draft.Field("userDescription", "OwnDescriptionOptional", "text", item.UserDescription, false); draft.Money("amount", "AmountEuro", item.Amount);
        draft.Choice("kind", "Type", item.Kind == TransactionKind.Transfer
            ? [new("Transfer", UiCulture.Text("LegacyTransfer"))]
            : [new("Normal", UiCulture.Text("IncomeExpense")), new("Mortgage", UiCulture.Text("MortgagePrincipal"))], item.Kind.ToString());
        draft.Choice("category", "TransactionCategory", Categories(s.Data), item.CategoryId?.ToString() ?? "");
        draft.Choice("counter", "CounterAccount", new[] { new FieldOption("", UiCulture.Text("NotApplicable")) }.Concat(Accounts(s.Data)), item.CounterAccountId?.ToString() ?? "");
        draft.Money("principal", "ActualPrincipalEuro", item.PrincipalComponent);
        draft.Choice("recurring", "RecurringLink", new[] { new FieldOption("", UiCulture.Text("NoLink")) }.Concat(s.Data.RecurringPayments.Select(r => new FieldOption(r.Id.ToString(), r.Name))), item.RecurringPaymentId?.ToString() ?? "");
        draft.DateField("occurrence", "OptionalRecurringDueDate", item.RecurringOccurrenceDate, false);
        draft.Note += " " + UiCulture.Text("TransactionRecurringNote");
        if (TransferMatching.IsInternal(s.Data, item))
        {
            draft.Note += " " + UiCulture.Text("LegacyTransferEditorNote");
            foreach (var field in draft.Fields.Where(f => f.Key is "date" or "account" or "amount" or "kind" or "counter" or "recurring" or "occurrence" or "principal"))
                field.ReadOnly = true;
        }
        if (TransactionAccounting.HasDetails(s.Data, item.Id))
        {
            draft.Note += " " + UiCulture.Text("TransactionHandlingNote");
            foreach (var field in draft.Fields.Where(f => f.Key is "date" or "account" or "description" or "amount" or "kind")) field.ReadOnly = true;
            if (s.Data.TransactionAllocations.Any(a => a.TransactionId == item.Id))
                draft.Fields.Single(f => f.Key == "category").ReadOnly = true;
        }
        if (item.StatementBalance is { } balance)
        {
            var source = s.Data.ImportBatches.FirstOrDefault(b => b.Id == item.ImportBatchId)?.AccountId ?? item.AccountId;
            draft.Note += " " + UiCulture.Text("TransactionStatementNote", s.AccountName(source), WebState.Money(balance));
        }
        draft.Apply = d =>
        {
            if (draft.Text("kind") == "Transfer" && item.Kind != TransactionKind.Transfer)
                throw new InvalidOperationException(UiCulture.Text("UseCategoryPurposes"));
            HouseholdEdits.SaveTransaction(d, item with { Date = draft.Date("date"), AccountId = draft.Id("account"), Description = draft.Text("description"),
            UserDescription = draft.Text("userDescription"), Amount = draft.Number("amount"), Kind = Enum.Parse<TransactionKind>(draft.Text("kind")),
            CategoryId = draft.OptionalId("category"),
            CounterAccountId = draft.Text("kind") == "Transfer" ? draft.OptionalId("counter") : null,
            PrincipalComponent = draft.Text("kind") == "Mortgage" ? draft.Number("principal") : 0,
            RecurringPaymentId = draft.Text("kind") == "Transfer" ? null : draft.OptionalId("recurring"),
            RecurringOccurrenceDate = draft.Text("kind") == "Transfer" || draft.OptionalId("recurring") == null || draft.Text("occurrence").Length == 0
                ? null : draft.Date("occurrence") });
        }; return draft;
    }
    public static EditorDraft Recurring(WebState s, RecurringPayment? original = null)
    {
        var item = original ?? new RecurringPayment { AccountId = s.Data.Accounts.FirstOrDefault()?.Id ?? Guid.Empty, StartDate = s.Today, DayOfMonth = s.Today.Day };
        return Rule(s, s.Data.Rules.FirstOrDefault(r => r.RecurringPaymentId == item.Id), scheduled: item);
    }
    public static EditorDraft Contract(WebState s, SubscriptionContract? original = null, RecurringPayment? payment = null)
    {
        var item = original ?? new SubscriptionContract { OwnerId = s.OwnerId, Name = payment?.Name ?? "", Provider = payment?.Name ?? "",
            AccountId = payment?.AccountId, RecurringPaymentId = payment?.Id, CategoryId = payment?.CategoryId,
            Amount = Math.Abs(payment?.Amount ?? 0), StartDate = payment?.StartDate,
            BillingCycle = payment?.Frequency switch { PaymentFrequency.Quarterly => ContractBillingCycle.Quarterly,
                PaymentFrequency.Yearly => ContractBillingCycle.Yearly, _ => ContractBillingCycle.Monthly } };
        var draft = new EditorDraft(UiCulture.Text("ContractTitle"), s.Revision)
        { _contract = true, _contractPayments = s.Data.RecurringPayments.Where(p => p.Amount < 0 && !p.IsMortgage && p.Frequency != PaymentFrequency.Once).ToList(),
            Note = UiCulture.Text("ContractEditorNote") };
        draft.Field("name", "Name", "text", item.Name);
        draft.Field("provider", "ContractProvider", "text", item.Provider);
        draft.Field("plan", "ContractPlan", "text", item.Plan, false);
        draft.Choice("contractKind", "Type", Enum.GetValues<ContractKind>().Select(k => new FieldOption(k.ToString(), UiCulture.Text($"ContractKind{k}"))), item.Kind.ToString());
        draft.Choice("status", "Status", Enum.GetValues<ContractStatus>().Select(k => new FieldOption(k.ToString(), UiCulture.Text($"ContractStatus{k}"))), item.Status.ToString());
        draft.Field("country", "ContractCountry", "text", item.CountryCode);
        draft.Field("postcode", "ContractPostcode", "text", item.Postcode, false);
        draft.Choice("owner", "Owner", new[] { new FieldOption("", UiCulture.Text("ContractShared")) }.Concat(s.Data.Owners.Select(o => new FieldOption(o.Id.ToString(), o.Name))), item.OwnerId?.ToString() ?? "");
        draft.Choice("category", "Category", Categories(s.Data), item.CategoryId?.ToString() ?? "");
        draft.Choice("paymentAccount", "ContractPaymentAccount", new[] { new FieldOption("", UiCulture.Text("ContractExternalPayment")) }.Concat(Accounts(s.Data)), item.AccountId?.ToString() ?? "");
        draft.Field("externalPayment", "ContractExternalLabel", "text", item.ExternalPaymentLabel, false);
        draft.Choice("contractRecurring", "ContractRecurringLink", new[] { new FieldOption("", UiCulture.Text("NoLink")) }.Concat(draft._contractPayments.Where(p => !s.Data.Contracts.Any(c => c.Id != item.Id && c.RecurringPaymentId == p.Id)).Select(p => new FieldOption(p.Id.ToString(), p.Name))), item.RecurringPaymentId?.ToString() ?? "");
        draft.Money("contractAmount", "ContractPrice", item.Amount);
        draft.Choice("billing", "ContractBilling", Enum.GetValues<ContractBillingCycle>().Select(k => new FieldOption(k.ToString(), UiCulture.Text($"ContractBilling{k}"))), item.BillingCycle.ToString());
        draft.Field("regularAmount", "ContractRegularPrice", "money", item.RegularAmount?.ToString("0.00", Culture) ?? "", false);
        draft.DateField("start", "ContractStart", item.StartDate, false);
        draft.Field("indefinite", "ContractIndefinite", "checkbox", item.IsIndefinite.ToString());
        draft.DateField("end", "ContractEnd", item.EndDate, false);
        draft.DateField("renewal", "ContractRenewal", item.RenewalDate, false);
        draft.DateField("commitment", "ContractCommitment", item.MinimumCommitmentUntil, false);
        draft.Field("noticePeriod", "ContractNotice", "number", item.NoticePeriod?.ToString(CultureInfo.InvariantCulture) ?? "", false);
        draft.Choice("noticeUnit", "ContractNoticeUnit", [new("Days", UiCulture.Text("ContractNoticeDays")), new("Months", UiCulture.Text("ContractNoticeMonths"))], item.NoticeUnit.ToString());
        draft.DateField("deadline", "ContractExplicitDeadline", item.CancellationDeadline, false);
        draft.DateField("trialEnd", "ContractTrialEnd", item.TrialEndsOn, false);
        draft.DateField("promotionEnd", "ContractPromotionEnd", item.PromotionEndsOn, false);
        draft.DateField("review", "ContractReview", item.ReviewOn, false);
        draft.Field("cancelUrl", "ContractCancellationUrl", "url", item.CancellationUrl, false);
        draft.Field("contractUrl", "ContractDocumentUrl", "url", item.ContractUrl, false);
        draft.Field("download", "ContractDownload", "money", item.DownloadMbps?.ToString("0.##", Culture) ?? "", false);
        draft.Field("upload", "ContractUpload", "money", item.UploadMbps?.ToString("0.##", Culture) ?? "", false);
        draft.Field("unlimited", "ContractUnlimited", "checkbox", item.UnlimitedData.ToString());
        draft.Field("dataLimit", "ContractDataLimit", "money", item.DataLimitGb?.ToString("0.##", Culture) ?? "", false);
        draft.Field("network", "ContractNetwork", "text", item.NetworkAndConditions, false);
        draft.Field("streams", "ContractStreams", "number", item.SimultaneousStreams?.ToString(CultureInfo.InvariantCulture) ?? "", false);
        draft.Field("videoQuality", "ContractVideoQuality", "text", item.VideoQuality, false);
        draft.Choice("ads", "ContractAds", [new("", UiCulture.Text("Unknown")), new("True", UiCulture.Text("ContractWithAds")), new("False", UiCulture.Text("ContractWithoutAds"))], item.HasAds?.ToString() ?? "");
        draft.Field("notes", "ContractNotes", "text", item.Notes, false);
        void Section(string title, params string[] keys)
        { foreach (var field in draft.Fields.Where(f => keys.Contains(f.Key))) field.Section = UiCulture.Text(title); }
        Section("ContractGeneralSection", "name", "provider", "plan", "contractKind", "status", "country", "postcode", "owner", "category");
        Section("ContractPaymentSection", "paymentAccount", "externalPayment", "contractRecurring", "contractAmount", "billing", "regularAmount");
        Section("ContractDatesSection", "start", "indefinite", "end", "renewal", "commitment", "noticePeriod", "noticeUnit", "deadline", "trialEnd", "promotionEnd", "review");
        Section("ContractLinksSection", "cancelUrl", "contractUrl");
        Section("ContractFeaturesSection", "download", "upload", "unlimited", "dataLimit", "network", "streams", "videoQuality", "ads");
        Section("ContractNotesSection", "notes");
        DateOnly? Date(string key) => draft.Text(key).Length == 0 ? null : draft.Date(key);
        decimal? Number(string key) => draft.Text(key).Length == 0 ? null : draft.Number(key);
        int? Integer(string key) => draft.Text(key).Length == 0 ? null : int.Parse(draft.Text(key), CultureInfo.InvariantCulture);
        draft.Apply = d =>
        {
            var telecom = draft.Text("contractKind") is "Mobile" or "Internet" or "TelecomBundle";
            var streaming = draft.Text("contractKind") == "Streaming";
            SubscriptionContracts.Save(d, item with { Name = draft.Text("name"), Provider = draft.Text("provider"), Plan = draft.Text("plan"),
                Kind = Enum.Parse<ContractKind>(draft.Text("contractKind")), Status = Enum.Parse<ContractStatus>(draft.Text("status")),
                CountryCode = draft.Text("country"), Postcode = draft.Text("postcode"), OwnerId = draft.OptionalId("owner"), CategoryId = draft.OptionalId("category"),
                AccountId = draft.OptionalId("paymentAccount"), ExternalPaymentLabel = draft.Text("externalPayment"),
                RecurringPaymentId = draft.Text("paymentAccount").Length == 0 ? null : draft.OptionalId("contractRecurring"),
                Amount = draft.Number("contractAmount"), RegularAmount = Number("regularAmount"), BillingCycle = Enum.Parse<ContractBillingCycle>(draft.Text("billing")),
                StartDate = Date("start"), IsIndefinite = bool.Parse(draft.Text("indefinite")), EndDate = Date("end"),
                RenewalDate = Date("renewal"), MinimumCommitmentUntil = Date("commitment"),
                NoticePeriod = Integer("noticePeriod"), NoticeUnit = Enum.Parse<ContractNoticeUnit>(draft.Text("noticeUnit")), CancellationDeadline = Date("deadline"),
                TrialEndsOn = Date("trialEnd"), PromotionEndsOn = Date("promotionEnd"), ReviewOn = Date("review"),
                CancellationUrl = draft.Text("cancelUrl"), ContractUrl = draft.Text("contractUrl"), Notes = draft.Text("notes"),
                DownloadMbps = telecom ? Number("download") : null, UploadMbps = telecom ? Number("upload") : null,
                UnlimitedData = telecom && bool.Parse(draft.Text("unlimited")), DataLimitGb = telecom && !bool.Parse(draft.Text("unlimited")) ? Number("dataLimit") : null,
                NetworkAndConditions = telecom ? draft.Text("network") : "", SimultaneousStreams = streaming ? Integer("streams") : null,
                VideoQuality = streaming ? draft.Text("videoQuality") : "", HasAds = streaming && draft.Text("ads").Length > 0 ? bool.Parse(draft.Text("ads")) : null });
        };
        return draft;
    }
    public static bool CanCreateRecurring(HouseholdData data, Transaction transaction) => transaction.RecurringPaymentId == null &&
        !TransferMatching.IsInternal(data, transaction) && !TransactionAccounting.HasDetails(data, transaction.Id);

    public static EditorDraft RecognitionFromTransaction(WebState state, Transaction transaction) => Rule(state,
        state.Data.Rules.FirstOrDefault(r => r.Id == transaction.RecognitionRuleId) ??
        state.Data.Rules.FirstOrDefault(r => r.RecurringPaymentId != null && r.RecurringPaymentId == transaction.RecurringPaymentId) ??
        TransactionRecognition.Identity(state.Data, transaction), transaction);

    public static EditorDraft RecurringFromTransaction(WebState s, Transaction transaction)
    {
        if (!CanCreateRecurring(s.Data, transaction)) throw new InvalidOperationException(UiCulture.Text("RecurringSeedUnavailable"));
        var draft = Rule(s, seed: transaction, scheduled: new RecurringPayment { Name = transaction.UserDescription.Length > 0 ? transaction.UserDescription : transaction.RecognizedName,
            AccountId = transaction.AccountId, Amount = transaction.Amount, DayOfMonth = transaction.Date.Day,
            StartDate = new(transaction.Date.Year, transaction.Date.Month, 1), CategoryId = transaction.CategoryId,
            MatchText = "", IsMortgage = transaction.Kind == TransactionKind.Mortgage });
        draft._seedTransactionId = transaction.Id;
        return draft;
    }

    public static EditorDraft ImportCoverage(WebState s, ImportBatch batch)
    {
        var entries = s.Data.Transactions.Where(t => t.ImportBatchId == batch.Id).ToList();
        var draft = new EditorDraft(UiCulture.Text(batch.CoverageFrom != null ? "EditStatementCoverage" : "ConfirmStatementCoverage"), s.Revision)
        { Note = UiCulture.Text("ConfirmStatementCoverageNote"), SaveLabel = UiCulture.Text("Confirm"), SuccessMessage = UiCulture.Text("StatementPeriodSaved") };
        draft.DateField("from", "FromInclusive", batch.CoverageFrom ?? entries.Select(t => (DateOnly?)t.Date).Min() ?? s.Today);
        draft.DateField("through", "ThroughInclusive", batch.CoverageThrough ?? entries.Select(t => (DateOnly?)t.Date).Max() ?? s.Today);
        draft.Apply = d =>
        {
            var saved = d.ImportBatches.Single(b => b.Id == batch.Id);
            var from = draft.Date("from"); var through = draft.Date("through");
            var rows = d.Transactions.Where(t => t.ImportBatchId == batch.Id).ToList();
            if (saved.Undone || rows.Count != saved.RowCount || from > through || through > s.Today ||
                rows.Any(t => t.Date < from || t.Date > through))
                throw new InvalidOperationException(UiCulture.Text("InvalidStatementCoverage"));
            saved.CoverageFrom = from; saved.CoverageThrough = through;
        };
        return draft;
    }

    public static EditorDraft AccountCoverage(WebState s, Account account, AccountStatementPeriod? original = null)
    {
        var draft = new EditorDraft(UiCulture.Text("AccountPeriodTitle", account.Name), s.Revision)
        {
            Note = UiCulture.Text("AccountPeriodNote"), SaveLabel = UiCulture.Text("ConfirmAccountPeriod"),
            SuccessMessage = UiCulture.Text("StatementPeriodSaved")
        };
        draft.DateField("from", "FromInclusive", original?.From ?? account.OpeningDate);
        draft.DateField("through", "ThroughInclusive", original?.Through ?? s.Today);
        draft.Field("complete", "AccountPeriodConsent", "checkbox", "False");
        draft.Apply = d =>
        {
            if (!bool.TryParse(draft.Text("complete"), out var complete) || !complete)
                throw new InvalidOperationException(UiCulture.Text("AccountPeriodConsentRequired"));
            var saved = d.Accounts.Single(a => a.Id == account.Id);
            var from = draft.Date("from"); var through = draft.Date("through");
            if (saved.Kind == AccountKind.Investment || from < saved.OpeningDate || from > through || through > s.Today)
                throw new InvalidOperationException(UiCulture.Text("InvalidAccountPeriod"));
            BankImportCoverage.ConfirmAccountPeriod(d, account.Id, from, through, s.Today, original?.Id);
        };
        return draft;
    }
    public static string Frequency(PaymentFrequency frequency) => frequency switch
    { PaymentFrequency.Monthly => UiCulture.Text("Monthly"), PaymentFrequency.Quarterly => UiCulture.Text("Quarterly"), PaymentFrequency.Yearly => UiCulture.Text("Yearly"), _ => UiCulture.Text("Once") };
    public static EditorDraft Rule(WebState s, CategorizationRule? original = null, Transaction? seed = null, RecurringPayment? scheduled = null)
    {
        scheduled ??= s.Data.RecurringPayments.FirstOrDefault(p => p.Id == original?.RecurringPaymentId);
        var item = original ?? new CategorizationRule { Name = scheduled?.Name ?? seed?.UserDescription ?? "", Contains = scheduled?.MatchText ?? "",
            CategoryId = scheduled?.CategoryId ?? seed?.CategoryId, AccountId = scheduled?.AccountId ?? seed?.AccountId,
            Direction = scheduled != null ? (PaymentDirection)Math.Sign(scheduled.Amount) : seed != null ? (PaymentDirection)Math.Sign(seed.Amount) : PaymentDirection.Any,
            NormalizeText = scheduled != null };
        var plan = scheduled ?? new RecurringPayment { AccountId = seed?.AccountId ?? s.Data.Accounts.FirstOrDefault()?.Id ?? Guid.Empty,
            Amount = seed?.Amount ?? 0, StartDate = seed == null ? s.Today : new(seed.Date.Year, seed.Date.Month, 1),
            DayOfMonth = seed?.Date.Day ?? s.Today.Day, IsMortgage = seed?.Kind == TransactionKind.Mortgage };
        var draft = new EditorDraft(UiCulture.Text("RecognitionSetup"), s.Revision) { Note = UiCulture.Text("RecognitionEditorNote"),
            _recognitionRuleId = item.Id, _seedTransactionId = seed?.Id, _recognitionScope = s.Scope };
        draft.Field("name", "RecognizeAsName", "text", item.Name, false);
        draft.Field("text", "DescriptionContains", "text", item.Contains, scheduled == null || seed != null && original == null);
        draft.Choice("category", "OptionalRecognitionCategory", Categories(s.Data), item.CategoryId?.ToString() ?? "");
        draft.Choice("account", "RecognitionAccount", new[] { new FieldOption("", UiCulture.Text("AllAccounts")) }.Concat(Accounts(s.Data)), item.AccountId?.ToString() ?? "");
        draft.Choice("direction", "PaymentDirection", [new("Any", UiCulture.Text("BothDirections")), new("Outgoing", UiCulture.Text("Expense")),
            new("Incoming", UiCulture.Text("IncomeSingle"))], item.Direction.ToString());
        draft.Choice("planning", "OptionalPlanning", scheduled == null ? [new("", UiCulture.Text("NoPlanning")), new("new", UiCulture.Text("CreatePlanning"))]
            : [new(plan.Id.ToString(), UiCulture.Text("ExistingSharedPlanning"))], scheduled == null ? "" : plan.Id.ToString());
        draft.Fields.Single(f => f.Key == "planning").ReadOnly = scheduled != null;
        draft.Money("amount", "RecurringAmountEuro", plan.Amount); draft.Field("day", "RecurringDay", "number", plan.DayOfMonth.ToString(CultureInfo.InvariantCulture));
        draft.Choice("frequency", "Frequency", Enum.GetValues<PaymentFrequency>().Select(f => new FieldOption(f.ToString(), Frequency(f))), plan.Frequency.ToString());
        draft.Choice("amountMode", "RecurringAmountMode", [new("Fixed", UiCulture.Text("FixedPaymentAmount")), new("Variable", UiCulture.Text("VariablePaymentAmount"))], plan.AmountMode.ToString());
        draft.DateField("start", "StartDate", plan.StartDate); draft.DateField("end", "OptionalEndDate", plan.EndDate, false);
        draft.Field("mortgage", "IsMortgagePayment", "checkbox", plan.IsMortgage.ToString());
        draft.Money("tolerance", "MatchToleranceEuro", plan.MatchTolerance);
        draft.Field("window", "MatchWindowDays", "number", plan.MatchWindowDays.ToString(CultureInfo.InvariantCulture));
        draft.Apply = d =>
        {
            var rule = item with { Name = draft.Text("name"), Contains = draft.Text("text"), CategoryId = draft.OptionalId("category"),
                AccountId = draft.OptionalId("account"), Direction = Enum.Parse<PaymentDirection>(draft.Text("direction")) };
            if (draft.Text("planning").Length == 0) HouseholdEdits.SaveRule(d, rule);
            else HouseholdEdits.SaveRecurring(d, plan with { Name = rule.Name, MatchText = rule.Contains, CategoryId = rule.CategoryId,
                AccountId = draft.Id("account"), Amount = draft.Number("amount"), DayOfMonth = int.TryParse(draft.Text("day"), out var day) ? day : 0,
                StartDate = draft.Date("start"), EndDate = draft.Text("end").Length == 0 ? null : draft.Date("end"),
                IsMortgage = bool.Parse(draft.Text("mortgage")), Frequency = Enum.Parse<PaymentFrequency>(draft.Text("frequency")),
                AmountMode = Enum.Parse<PaymentAmountMode>(draft.Text("amountMode")), MatchTolerance = draft.Number("tolerance"),
                MatchWindowDays = int.TryParse(draft.Text("window"), out var window) ? window : -1 }, rule);
        };
        return draft;
    }
    public static EditorDraft Valuation(WebState s, InvestmentValuation? original = null)
    {
        var item = original ?? new InvestmentValuation { AccountId = s.Data.Accounts.FirstOrDefault(a => a.Kind == AccountKind.Investment)?.Id ?? Guid.Empty };
        var draft = new EditorDraft(UiCulture.Text("SecuritiesValuation"), s.Revision) { Note = UiCulture.Text("ValuationEditorNote") };
        draft.Choice("account", "InvestmentAccount", Accounts(s.Data).Where(a => s.Data.Accounts.Any(x => x.Id.ToString() == a.Value && x.Kind == AccountKind.Investment)), item.AccountId.ToString());
        draft.DateField("date", "ValuationDate", item.Date); draft.Money("value", "SecuritiesMarketValueEuro", item.SecuritiesValue);
        draft.Apply = d => HouseholdEdits.SaveValuation(d, item with { AccountId = draft.Id("account"), Date = draft.Date("date"), SecuritiesValue = draft.Number("value") }); return draft;
    }
    public static EditorDraft Mortgage(WebState s)
    {
        var item = s.Data.Mortgage ?? new Mortgage { OwnerIds = s.Data.Owners.Select(o => o.Id).ToList() };
        var draft = new EditorDraft(UiCulture.Text("AnnuityMortgage"), s.Revision)
        { Note = UiCulture.Text("MortgageEditorNote") + " " + UiCulture.Text("MortgageRateHelp"), _mortgage = true };
        draft.Field("name", "NameLender", "text", item.Name); draft.Owners(s.Data, item.OwnerIds);
        draft.Money("principal", "ReferenceDebtEuro", item.ReferencePrincipal); draft.DateField("date", "ReferenceDate", item.ReferenceDate);
        draft.Field("rate", "FixedAnnualRatePercent", "money", item.AnnualInterestPercent.ToString("0.############################", Culture));
        draft.Choice("rateConvention", "MortgageRateConvention", Enum.GetValues<MortgageRateConvention>()
            .Select(c => new FieldOption(c.ToString(), MortgageRateLabel(c))), item.RateConvention.ToString());
        draft.Field("monthlyRate", "MortgageMonthlyRatePercent", "money", item.MonthlyInterestPercent?.ToString("0.############################", Culture) ?? "", false);
        draft.Money("payment", "MonthlyAnnuityEuro", item.MonthlyPayment);
        draft.Field("months", "RemainingTermMonths", "number", item.RemainingMonths.ToString(CultureInfo.InvariantCulture));
        draft.DateField("fixedUntil", "OptionalFixedRateUntil", item.FixedRateUntil, false);
        draft.Apply = d => d.Mortgage = item with { Name = draft.Text("name"), OwnerIds = draft.Fields.Single(f => f.Key == "owners").OwnerIds.ToList(),
            ReferencePrincipal = draft.Number("principal"), ReferenceDate = draft.Date("date"), AnnualInterestPercent = draft.Number("rate"),
            RateConvention = Enum.Parse<MortgageRateConvention>(draft.Text("rateConvention")),
            MonthlyInterestPercent = draft.Text("rateConvention") != "Monthly" ? item.MonthlyInterestPercent
                : draft.Text("monthlyRate").Length == 0 ? null : draft.Number("monthlyRate"), MonthlyPayment = draft.Number("payment"),
            RemainingMonths = int.TryParse(draft.Text("months"), out var months) ? months : 0, FixedRateUntil = draft.Text("fixedUntil").Length == 0 ? null : draft.Date("fixedUntil") }; return draft;
    }
    public static string MortgageRateLabel(MortgageRateConvention convention) => UiCulture.Text(convention switch
    {
        MortgageRateConvention.EffectiveAnnual => "MortgageEffectiveAnnual",
        MortgageRateConvention.Monthly => "MortgageExactMonthly",
        _ => "MortgageNominalAnnual"
    });
}
