using System.Globalization;
using System.Text.Json;
using Huishoudboek.Core;
using static Huishoudboek.Web.UiCulture;

namespace Huishoudboek.Web;

public sealed record FieldOption(string Value, string Label);
public sealed record EditorSaveProposal(int MatchCount, Action<HouseholdData> SaveOnly, Action<HouseholdData> SaveAndLink);
public sealed class EditorField(string key, string label, string type, string value = "", bool required = true)
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Type { get; } = type;
    public string Value { get; set; } = value;
    public bool Required { get; } = required;
    public bool ReadOnly { get; set; }
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
    public List<EditorField> Fields { get; } = [];
    public Action<HouseholdData> Apply { get; private set; } = _ => { };
    private Guid? _recurringPaymentId;
    private bool _transaction;
    public bool IsFieldVisible(EditorField field) => !_transaction || field.Key switch
    {
        "counter" => Text("kind") == "Transfer",
        "principal" => Text("kind") == "Mortgage",
        "recurring" => Text("kind") != "Transfer",
        "occurrence" => Text("kind") != "Transfer" && Text("recurring").Length > 0,
        _ => true
    };
    public IEnumerable<FieldOption> Options(EditorField field) => _transaction && field.Key == "category" && Text("kind") != "Transfer"
        ? field.Options.Where(o => o.Value != "default") : field.Options;
    public EditorSaveProposal PrepareSave(HouseholdData data, DateOnly today)
    {
        if (_recurringPaymentId is not { } paymentId) return new(0, Apply, Apply);
        // Preview on an isolated copy; neither the plan nor its links are saved before consent.
        var candidate = JsonSerializer.Deserialize<HouseholdData>(JsonSerializer.SerializeToUtf8Bytes(data))!;
        Apply(candidate);
        Validation.Validate(candidate);
        var payment = candidate.RecurringPayments.Single(r => r.Id == paymentId);
        var matches = RecurringSchedule.BatchCandidates(candidate, paymentId, today);
        void SaveOnly(HouseholdData d) => HouseholdEdits.SaveRecurring(d, payment);
        void SaveAndLink(HouseholdData d)
        {
            SaveOnly(d);
            foreach (var match in matches)
                RecurringSchedule.Link(d, match.Transaction.Id, paymentId, match.DueDate);
        }
        return new(matches.Count, SaveOnly, SaveAndLink);
    }
    public string Text(string key) => Fields.Single(f => f.Key == key).Value.Trim();
    public Guid Id(string key) => Guid.TryParse(Text(key), out var id) ? id : throw new InvalidOperationException(UiCulture.Text("InvalidAccountCategory"));
    public Guid? OptionalId(string key) => string.IsNullOrEmpty(Text(key)) ? null : Id(key);
    public decimal Number(string key)
    {
        if (!decimal.TryParse(Text(key), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Culture, out var value))
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
    private static IEnumerable<FieldOption> Categories(HouseholdData d) => new[] { new FieldOption("", UiCulture.Text("Uncategorized")) }.Concat(d.Categories.Select(c => new FieldOption(c.Id.ToString(), c.Name)));

    public static EditorDraft Owner(WebState s, Owner? original = null)
    {
        var item = original ?? new Owner(); var draft = new EditorDraft(UiCulture.Text("Owner"), s.Revision);
        draft.Field("name", "Name", "text", item.Name);
        draft.Apply = d => HouseholdEdits.SaveOwner(d, item with { Name = draft.Text("name") }); return draft;
    }
    public static EditorDraft Account(WebState s, Account? original = null)
    {
        var item = original ?? new Account { OwnerIds = s.Data.Owners.Select(o => o.Id).ToList(), OpeningDate = new DateOnly(s.Today.Year, 1, 1) };
        var draft = new EditorDraft(UiCulture.Text("Account"), s.Revision) { Note = UiCulture.Text("AccountEditorNote") };
        draft.Field("name", "Name", "text", item.Name);
        draft.Choice("kind", "AccountType", Enum.GetValues<AccountKind>().Select(k => new FieldOption(k.ToString(), WebState.Kind(k))), item.Kind.ToString());
        draft.Field("reference", "BankReference", "text", item.BankReference, false); draft.Owners(s.Data, item.OwnerIds);
        draft.DateField("date", "OpeningDate", item.OpeningDate); draft.Money("balance", "OpeningBalanceEuro", item.OpeningBalance);
        draft.Field("goal", "SavingsGoalEuro", "money", item.SavingsGoal?.ToString("0.00", Culture) ?? "", false);
        draft.Money("buffer", "AccountBufferEuro", item.MinimumCashBuffer);
        draft.Apply = d => HouseholdEdits.SaveAccount(d, item with { Name = draft.Text("name"), Kind = Enum.Parse<AccountKind>(draft.Text("kind")),
            BankReference = draft.Text("reference"), OwnerIds = draft.Fields.Single(f => f.Key == "owners").OwnerIds.ToList(), OpeningDate = draft.Date("date"),
            OpeningBalance = draft.Number("balance"), SavingsGoal = draft.Text("goal").Length == 0 ? null : draft.Number("goal"),
            MinimumCashBuffer = draft.Number("buffer") }); return draft;
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
        { Note = UiCulture.Text("SimpleEntryNote") };
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
    public static EditorDraft Category(WebState s, Category? original = null, bool renameOnly = false)
    {
        var item = original ?? new Category();
        var draft = new EditorDraft(UiCulture.Text(renameOnly ? "RenameCategory" : "CategoryDefaultBudget"), s.Revision)
        { Note = UiCulture.Text(renameOnly ? "RenameCategoryNote" : "CategoryEditorNote") };
        draft.Field("name", "Name", "text", item.Name);
        if (!renameOnly) draft.Money("budget", "DefaultMonthlyBudgetEuro", item.MonthlyBudget);
        draft.Apply = d => { if (renameOnly) Categorization.RenameCategory(d, item.Id, draft.Text("name"));
            else HouseholdEdits.SaveCategory(d, item with { Name = draft.Text("name"), MonthlyBudget = draft.Number("budget") }); }; return draft;
    }
    public static EditorDraft Budget(WebState s, Category item)
    {
        var year = s.Year; var month = s.Month;
        var draft = new EditorDraft(UiCulture.Text("MonthlyBudgetTitle", item.Name, $"{month:D2}-{year}"), s.Revision) { Note = UiCulture.Text("MonthlyBudgetEditorNote") };
        draft.Money("budget", "BudgetEuro", s.Data.BudgetOverrides.FirstOrDefault(b => b.CategoryId == item.Id && b.Year == year && b.Month == month)?.Amount ?? item.MonthlyBudget);
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
        draft.Choice("kind", "Type", [new("Normal", UiCulture.Text("IncomeExpense")), new("Transfer", UiCulture.Text("InternalTransfer")), new("Mortgage", UiCulture.Text("MortgagePrincipal"))], item.Kind.ToString());
        var categoryOptions = new[] { new FieldOption("default", UiCulture.Text("AutomaticTransferCategory")) }.Concat(Categories(s.Data));
        var automatic = item.Kind == TransactionKind.Transfer && (original == null || item.CategoryId != null &&
            string.Equals(s.Data.Categories.FirstOrDefault(c => c.Id == item.CategoryId)?.Name.Trim(), TransferCategories.DefaultName(s.Data, item), StringComparison.OrdinalIgnoreCase));
        draft.Choice("category", "TransactionCategory", categoryOptions, automatic ? "default" : item.CategoryId?.ToString() ?? "");
        draft.Choice("counter", "CounterAccount", new[] { new FieldOption("", UiCulture.Text("NotApplicable")) }.Concat(Accounts(s.Data)), item.CounterAccountId?.ToString() ?? "");
        draft.Money("principal", "ActualPrincipalEuro", item.PrincipalComponent);
        draft.Choice("recurring", "RecurringLink", new[] { new FieldOption("", UiCulture.Text("NoLink")) }.Concat(s.Data.RecurringPayments.Select(r => new FieldOption(r.Id.ToString(), r.Name))), item.RecurringPaymentId?.ToString() ?? "");
        draft.DateField("occurrence", "OptionalRecurringDueDate", item.RecurringOccurrenceDate, false);
        draft.Note += " " + UiCulture.Text("TransactionRecurringNote");
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
        draft.Apply = d => HouseholdEdits.SaveTransaction(d, item with { Date = draft.Date("date"), AccountId = draft.Id("account"), Description = draft.Text("description"),
            UserDescription = draft.Text("userDescription"), Amount = draft.Number("amount"), Kind = Enum.Parse<TransactionKind>(draft.Text("kind")),
            CategoryId = draft.Text("category") == "default" ? null : draft.OptionalId("category"),
            CounterAccountId = draft.Text("kind") == "Transfer" ? draft.OptionalId("counter") : null,
            PrincipalComponent = draft.Text("kind") == "Mortgage" ? draft.Number("principal") : 0,
            RecurringPaymentId = draft.Text("kind") == "Transfer" ? null : draft.OptionalId("recurring"),
            RecurringOccurrenceDate = draft.Text("kind") == "Transfer" || draft.OptionalId("recurring") == null || draft.Text("occurrence").Length == 0
                ? null : draft.Date("occurrence") }, draft.Text("category") == "default"); return draft;
    }
    public static EditorDraft Recurring(WebState s, RecurringPayment? original = null)
    {
        var item = original ?? new RecurringPayment { AccountId = s.Data.Accounts.FirstOrDefault()?.Id ?? Guid.Empty, StartDate = s.Today, DayOfMonth = s.Today.Day };
        var draft = new EditorDraft(UiCulture.Text("RecurringPayment"), s.Revision)
        { Note = UiCulture.Text("RecurringEditorNote"), _recurringPaymentId = item.Id };
        draft.Field("name", "Name", "text", item.Name); draft.Choice("account", "Account", Accounts(s.Data), item.AccountId.ToString());
        draft.Money("amount", "RecurringAmountEuro", item.Amount); draft.Field("day", "RecurringDay", "number", item.DayOfMonth.ToString(CultureInfo.InvariantCulture));
        draft.Choice("frequency", "Frequency", Enum.GetValues<PaymentFrequency>().Select(f => new FieldOption(f.ToString(), Frequency(f))), item.Frequency.ToString());
        draft.Choice("category", "Category", Categories(s.Data), item.CategoryId?.ToString() ?? "");
        draft.DateField("start", "StartDate", item.StartDate); draft.DateField("end", "OptionalEndDate", item.EndDate, false);
        draft.Field("text", "OptionalMatchText", "text", item.MatchText, false); draft.Field("mortgage", "IsMortgagePayment", "checkbox", item.IsMortgage.ToString());
        draft.Money("tolerance", "MatchToleranceEuro", item.MatchTolerance);
        draft.Field("window", "MatchWindowDays", "number", item.MatchWindowDays.ToString(CultureInfo.InvariantCulture));
        draft.Apply = d => HouseholdEdits.SaveRecurring(d, item with { Name = draft.Text("name"), AccountId = draft.Id("account"), Amount = draft.Number("amount"),
            DayOfMonth = int.TryParse(draft.Text("day"), out var day) ? day : 0, CategoryId = draft.OptionalId("category"), StartDate = draft.Date("start"),
            EndDate = draft.Text("end").Length == 0 ? null : draft.Date("end"), MatchText = draft.Text("text"), IsMortgage = bool.Parse(draft.Text("mortgage")),
            Frequency = Enum.Parse<PaymentFrequency>(draft.Text("frequency")), MatchTolerance = draft.Number("tolerance"),
            MatchWindowDays = int.TryParse(draft.Text("window"), out var window) ? window : -1 }); return draft;
    }
    public static string Frequency(PaymentFrequency frequency) => frequency switch
    { PaymentFrequency.Monthly => UiCulture.Text("Monthly"), PaymentFrequency.Quarterly => UiCulture.Text("Quarterly"), PaymentFrequency.Yearly => UiCulture.Text("Yearly"), _ => UiCulture.Text("Once") };
    public static EditorDraft Rule(WebState s, CategorizationRule? original = null)
    {
        var item = original ?? new CategorizationRule(); var draft = new EditorDraft(UiCulture.Text("CategorizationRule"), s.Revision) { Note = UiCulture.Text("RuleEditorNote") };
        draft.Field("text", "DescriptionContains", "text", item.Contains);
        draft.Choice("category", "Category", s.Data.Categories.Select(c => new FieldOption(c.Id.ToString(), c.Name)), item.CategoryId.ToString());
        draft.Apply = d => HouseholdEdits.SaveRule(d, item with { Contains = draft.Text("text"), CategoryId = draft.Id("category") }); return draft;
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
        var draft = new EditorDraft(UiCulture.Text("AnnuityMortgage"), s.Revision) { Note = UiCulture.Text("MortgageEditorNote") };
        draft.Field("name", "NameLender", "text", item.Name); draft.Owners(s.Data, item.OwnerIds);
        draft.Money("principal", "ReferenceDebtEuro", item.ReferencePrincipal); draft.DateField("date", "ReferenceDate", item.ReferenceDate);
        draft.Field("rate", "FixedAnnualRatePercent", "money", item.AnnualInterestPercent.ToString("0.####", Culture)); draft.Money("payment", "MonthlyAnnuityEuro", item.MonthlyPayment);
        draft.Field("months", "RemainingTermMonths", "number", item.RemainingMonths.ToString(CultureInfo.InvariantCulture));
        draft.DateField("fixedUntil", "OptionalFixedRateUntil", item.FixedRateUntil, false);
        draft.Apply = d => d.Mortgage = item with { Name = draft.Text("name"), OwnerIds = draft.Fields.Single(f => f.Key == "owners").OwnerIds.ToList(),
            ReferencePrincipal = draft.Number("principal"), ReferenceDate = draft.Date("date"), AnnualInterestPercent = draft.Number("rate"), MonthlyPayment = draft.Number("payment"),
            RemainingMonths = int.TryParse(draft.Text("months"), out var months) ? months : 0, FixedRateUntil = draft.Text("fixedUntil").Length == 0 ? null : draft.Date("fixedUntil") }; return draft;
    }
}
