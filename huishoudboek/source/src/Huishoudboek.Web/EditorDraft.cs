using System.Globalization;
using Huishoudboek.Core;

namespace Huishoudboek.Web;

public sealed record FieldOption(string Value, string Label);
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
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");
    public string Title { get; } = title;
    public string Revision { get; } = revision;
    public string Note { get; private set; } = "";
    public string Error { get; set; } = "";
    public List<EditorField> Fields { get; } = [];
    public Action<HouseholdData> Apply { get; private set; } = _ => { };
    public string Text(string key) => Fields.Single(f => f.Key == key).Value.Trim();
    public Guid Id(string key) => Guid.TryParse(Text(key), out var id) ? id : throw new InvalidOperationException("Kies een geldige rekening of categorie.");
    public Guid? OptionalId(string key) => string.IsNullOrEmpty(Text(key)) ? null : Id(key);
    public decimal Number(string key)
    {
        if (!decimal.TryParse(Text(key), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Dutch, out var value))
            throw new InvalidOperationException("Gebruik een geldig bedrag met een komma als decimaalteken, zonder duizendtallen.");
        return value;
    }
    public DateOnly Date(string key) => DateOnly.TryParseExact(Text(key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date : throw new InvalidOperationException("Vul een geldige datum in.");
    private EditorField Field(string key, string label, string type, string value = "", bool required = true)
    { var field = new EditorField(key, label, type, value, required); Fields.Add(field); return field; }
    private void Money(string key, string label, decimal value) => Field(key, label, "money", value.ToString("0.00", Dutch));
    private void DateField(string key, string label, DateOnly? date, bool required = true) => Field(key, label, "date", date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", required);
    private void Owners(HouseholdData data, IEnumerable<Guid> selected)
    {
        var field = Field("owners", "Eigenaren", "owners"); field.OwnerIds = selected.ToHashSet();
        field.Options = data.Owners.Select(o => new FieldOption(o.Id.ToString(), o.Name)).ToList();
    }
    private void Choice(string key, string label, IEnumerable<FieldOption> options, string value)
    { var field = Field(key, label, "select", value, false); field.Options = options.ToList(); }
    private static IEnumerable<FieldOption> Accounts(HouseholdData d) => d.Accounts.Select(a => new FieldOption(a.Id.ToString(), a.Name));
    private static IEnumerable<FieldOption> Categories(HouseholdData d) => new[] { new FieldOption("", "Zonder categorie") }.Concat(d.Categories.Select(c => new FieldOption(c.Id.ToString(), c.Name)));

    public static EditorDraft Owner(WebState s, Owner? original = null)
    {
        var item = original ?? new Owner(); var draft = new EditorDraft("Eigenaar", s.Revision);
        draft.Field("name", "Naam", "text", item.Name);
        draft.Apply = d => HouseholdEdits.SaveOwner(d, item with { Name = draft.Text("name") }); return draft;
    }
    public static EditorDraft Account(WebState s, Account? original = null)
    {
        var item = original ?? new Account { OwnerIds = s.Data.Owners.Select(o => o.Id).ToList(), OpeningDate = new DateOnly(s.Today.Year, 1, 1) };
        var draft = new EditorDraft("Rekening", s.Revision) { Note = "Beginsaldo = saldo aan het BEGIN van de openingsdatum, vóór betalingen op die dag. Een wijziging kan bestaande boekingen ongeldig maken; die wordt dan geweigerd." };
        draft.Field("name", "Naam", "text", item.Name);
        draft.Choice("kind", "Rekeningtype", Enum.GetValues<AccountKind>().Select(k => new FieldOption(k.ToString(), WebState.Kind(k))), item.Kind.ToString());
        draft.Field("reference", "Rekeningnummer / IBAN", "text", item.BankReference, false); draft.Owners(s.Data, item.OwnerIds);
        draft.DateField("date", "Openingsdatum", item.OpeningDate); draft.Money("balance", "Beginsaldo (€)", item.OpeningBalance);
        draft.Field("goal", "Spaardoel (€), optioneel", "money", item.SavingsGoal?.ToString("0.00", Dutch) ?? "", false);
        draft.Money("buffer", "Minimale cashbuffer op deze rekening (€)", item.MinimumCashBuffer);
        draft.Apply = d => HouseholdEdits.SaveAccount(d, item with { Name = draft.Text("name"), Kind = Enum.Parse<AccountKind>(draft.Text("kind")),
            BankReference = draft.Text("reference"), OwnerIds = draft.Fields.Single(f => f.Key == "owners").OwnerIds.ToList(), OpeningDate = draft.Date("date"),
            OpeningBalance = draft.Number("balance"), SavingsGoal = draft.Text("goal").Length == 0 ? null : draft.Number("goal"),
            MinimumCashBuffer = draft.Number("buffer") }); return draft;
    }
    public static EditorDraft CashBuffer(WebState s, Account item)
    {
        var draft = new EditorDraft($"Cashbuffer · {item.Name}", s.Revision)
        { Note = "Deze reserve wordt niet als bestedingsruimte gerekend. Geld op andere rekeningen dekt deze buffer of betalingen niet automatisch. 0 betekent geen extra reserve." };
        draft.Money("buffer", "Minimale cashbuffer (€)", item.MinimumCashBuffer);
        draft.Apply = d => HouseholdEdits.SaveAccount(d, d.Accounts.Single(a => a.Id == item.Id) with { MinimumCashBuffer = draft.Number("buffer") });
        return draft;
    }
    public static EditorDraft Category(WebState s, Category? original = null, bool renameOnly = false)
    {
        var item = original ?? new Category();
        var draft = new EditorDraft(renameOnly ? "Categorie hernoemen" : "Categorie / standaardbudget", s.Revision)
        { Note = renameOnly ? "Alleen de naam verandert, overal in de administratie. Koppelingen en budgetten blijven behouden." : "Standaardbudget geldt maandelijks. 0 betekent geen limiet ingesteld." };
        draft.Field("name", "Naam", "text", item.Name);
        if (!renameOnly) draft.Money("budget", "Standaard maandbudget (€)", item.MonthlyBudget);
        draft.Apply = d => { if (renameOnly) Categorization.RenameCategory(d, item.Id, draft.Text("name"));
            else HouseholdEdits.SaveCategory(d, item with { Name = draft.Text("name"), MonthlyBudget = draft.Number("budget") }); }; return draft;
    }
    public static EditorDraft Budget(WebState s, Category item)
    {
        var year = s.Year; var month = s.Month;
        var draft = new EditorDraft($"Maandbudget · {item.Name} · {month:D2}-{year}", s.Revision) { Note = "Alleen deze maand wordt aangepast. Andere maanden en het standaardbudget blijven gelijk. 0 = geen limiet." };
        draft.Money("budget", "Budget (€)", s.Data.BudgetOverrides.FirstOrDefault(b => b.CategoryId == item.Id && b.Year == year && b.Month == month)?.Amount ?? item.MonthlyBudget);
        draft.Apply = d => HouseholdEdits.SetBudget(d, item.Id, year, month, draft.Number("budget")); return draft;
    }
    public static EditorDraft Transaction(WebState s, Transaction? original = null, bool mortgage = false)
    {
        var item = original ?? new Transaction { AccountId = s.Data.Accounts.FirstOrDefault()?.Id ?? Guid.Empty, Kind = mortgage ? TransactionKind.Mortgage : TransactionKind.Normal };
        var draft = new EditorDraft("Transactie", s.Revision) { Note = "Inkomsten positief; uitgaven, hypotheek en overboekingen negatief. Een transfer wordt één keer opgeslagen en werkt beide rekeningen bij. Bij hypotheek: alleen werkelijke aflossing invullen; onbekend = 0. De volledige betaling is een cashlast." };
        if (s.Data.TransferMatches.Any(m => m.DebitTransactionId == item.Id || m.CreditTransactionId == item.Id))
            draft.Note += " Deze boeking heeft een transferbeslissing. Wijzigen van bedrag, datum, rekening, type of vaste-postkoppeling verwijdert die beslissing. Gebruik Transfers koppelen voor twee geïmporteerde boekingen; omzetting naar een handmatige transfer kan de andere rekening dubbel bijwerken.";
        draft.DateField("date", "Datum", item.Date); draft.Choice("account", "Rekening (bij overboeking: VAN)", Accounts(s.Data), item.AccountId.ToString());
        draft.Field("description", "Oorspronkelijke omschrijving", "text", item.Description);
        draft.Field("userDescription", "Eigen omschrijving (optioneel)", "text", item.UserDescription, false); draft.Money("amount", "Bedrag (€)", item.Amount);
        draft.Choice("kind", "Type", [new("Normal", "Inkomst / uitgave"), new("Transfer", "Interne overboeking"), new("Mortgage", "Hypotheek / extra aflossing")], item.Kind.ToString());
        var categoryOptions = new[] { new FieldOption("default", "Automatisch bij transfer: Sparen / Investeringen (anders zonder categorie)") }.Concat(Categories(s.Data));
        var automatic = original == null || item.Kind == TransactionKind.Transfer && item.CategoryId != null &&
            string.Equals(s.Data.Categories.FirstOrDefault(c => c.Id == item.CategoryId)?.Name.Trim(), TransferCategories.DefaultName(s.Data, item), StringComparison.OrdinalIgnoreCase);
        draft.Choice("category", "Categorie (transfer: alleen een label)", categoryOptions, automatic ? "default" : item.CategoryId?.ToString() ?? "");
        draft.Choice("counter", "NAAR rekening (alleen transfer)", new[] { new FieldOption("", "Niet van toepassing") }.Concat(Accounts(s.Data)), item.CounterAccountId?.ToString() ?? "");
        draft.Money("principal", "Werkelijke aflossing (€), alleen hypotheek", item.PrincipalComponent);
        draft.Choice("recurring", "Koppeling vaste post (voorkomt dubbele prognose)", new[] { new FieldOption("", "Geen koppeling") }.Concat(s.Data.RecurringPayments.Select(r => new FieldOption(r.Id.ToString(), r.Name))), item.RecurringPaymentId?.ToString() ?? "");
        draft.DateField("occurrence", "Geplande betaaldatum vaste post (optioneel; leeg = bankmaand)", item.RecurringOccurrenceDate, false);
        draft.Note += " Koppel bij voorkeur via Vaste posten, vooral als de bankdatum in een andere maand valt. Een afwijkend werkelijk bedrag verandert de schatting van volgende betalingen niet.";
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
            draft.Note += $" Banksaldo importrekening {s.AccountName(source)}: {WebState.Money(balance)}. Geen vervanging van uw beginsaldo.";
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
        var draft = new EditorDraft("Vaste post", s.Revision) { Note = "Een planning verandert uw werkelijke saldo niet. Kwartaal/jaar beginnen in de startmaand; eenmalig gebruikt de startdatum. Dag 29–31 wordt in korte maanden ingekort. Een koppeling vervangt één schatting door het echte bankbedrag. Ruimere herkenningsmarges leveren controlevoorstellen; automatisch koppelen blijft beperkt tot ± 1 cent en 5 dagen. Wijziging van een gekoppelde planning kan eerst ontkoppelen vereisen." };
        draft.Field("name", "Naam", "text", item.Name); draft.Choice("account", "Rekening", Accounts(s.Data), item.AccountId.ToString());
        draft.Money("amount", "Bedrag per betaling (€), negatief voor uitgave", item.Amount); draft.Field("day", "Dag (1–31; niet voor eenmalig)", "number", item.DayOfMonth.ToString(CultureInfo.InvariantCulture));
        draft.Choice("frequency", "Frequentie", Enum.GetValues<PaymentFrequency>().Select(f => new FieldOption(f.ToString(), Frequency(f))), item.Frequency.ToString());
        draft.Choice("category", "Categorie", Categories(s.Data), item.CategoryId?.ToString() ?? "");
        draft.DateField("start", "Startdatum", item.StartDate); draft.DateField("end", "Einddatum (optioneel)", item.EndDate, false);
        draft.Field("text", "Herkenningstekst (optioneel)", "text", item.MatchText, false); draft.Field("mortgage", "Dit is de hypotheekbetaling", "checkbox", item.IsMortgage.ToString());
        draft.Money("tolerance", "Herkenning: maximale bedragafwijking (€)", item.MatchTolerance);
        draft.Field("window", "Herkenning: maximaal datumverschil (0–31 dagen)", "number", item.MatchWindowDays.ToString(CultureInfo.InvariantCulture));
        draft.Apply = d => HouseholdEdits.SaveRecurring(d, item with { Name = draft.Text("name"), AccountId = draft.Id("account"), Amount = draft.Number("amount"),
            DayOfMonth = int.TryParse(draft.Text("day"), out var day) ? day : 0, CategoryId = draft.OptionalId("category"), StartDate = draft.Date("start"),
            EndDate = draft.Text("end").Length == 0 ? null : draft.Date("end"), MatchText = draft.Text("text"), IsMortgage = bool.Parse(draft.Text("mortgage")),
            Frequency = Enum.Parse<PaymentFrequency>(draft.Text("frequency")), MatchTolerance = draft.Number("tolerance"),
            MatchWindowDays = int.TryParse(draft.Text("window"), out var window) ? window : -1 }); return draft;
    }
    public static string Frequency(PaymentFrequency frequency) => frequency switch
    { PaymentFrequency.Monthly => "Maandelijks", PaymentFrequency.Quarterly => "Per kwartaal", PaymentFrequency.Yearly => "Jaarlijks", _ => "Eenmalig" };
    public static EditorDraft Rule(WebState s, CategorizationRule? original = null)
    {
        var item = original ?? new CategorizationRule(); var draft = new EditorDraft("Categorisatieregel", s.Revision) { Note = "Eerste passende regel wint. Bestaande categorieën worden niet overschreven; transfers vallen buiten tekstregels." };
        draft.Field("text", "Omschrijving bevat", "text", item.Contains);
        draft.Choice("category", "Categorie", s.Data.Categories.Select(c => new FieldOption(c.Id.ToString(), c.Name)), item.CategoryId.ToString());
        draft.Apply = d => HouseholdEdits.SaveRule(d, item with { Contains = draft.Text("text"), CategoryId = draft.Id("category") }); return draft;
    }
    public static EditorDraft Valuation(WebState s, InvestmentValuation? original = null)
    {
        var item = original ?? new InvestmentValuation { AccountId = s.Data.Accounts.FirstOrDefault(a => a.Kind == AccountKind.Investment)?.Id ?? Guid.Empty };
        var draft = new EditorDraft("Effectenwaardering", s.Revision) { Note = "Alleen effectenwaarde, EXCLUSIEF cash. Waardeveranderingen zijn geen inkomsten. Individuele aandelen, transacties, koersen en crypto worden niet ondersteund." };
        draft.Choice("account", "Effectenrekening", Accounts(s.Data).Where(a => s.Data.Accounts.Any(x => x.Id.ToString() == a.Value && x.Kind == AccountKind.Investment)), item.AccountId.ToString());
        draft.DateField("date", "Waarderingsdatum", item.Date); draft.Money("value", "Marktwaarde effecten (€)", item.SecuritiesValue);
        draft.Apply = d => HouseholdEdits.SaveValuation(d, item with { AccountId = draft.Id("account"), Date = draft.Date("date"), SecuritiesValue = draft.Number("value") }); return draft;
    }
    public static EditorDraft Mortgage(WebState s)
    {
        var item = s.Data.Mortgage ?? new Mortgage { OwnerIds = s.Data.Owners.Select(o => o.Id).ToList() };
        var draft = new EditorDraft("Annuïteitenhypotheek", s.Revision) { Note = "Schuld op peildatum is vóór betalingen op die dag. Alleen geregistreerde aflossingen op of na de peildatum verlagen de werkelijke schuld. Neem bedragen over van uw kredietgever; voorkom dubbeltelling bij een nieuwe peildatum." };
        draft.Field("name", "Naam / kredietgever", "text", item.Name); draft.Owners(s.Data, item.OwnerIds);
        draft.Money("principal", "Openstaande schuld op peildatum (€)", item.ReferencePrincipal); draft.DateField("date", "Peildatum", item.ReferenceDate);
        draft.Field("rate", "Vaste jaarrente (%)", "money", item.AnnualInterestPercent.ToString("0.####", Dutch)); draft.Money("payment", "Maandelijkse annuïteit (€)", item.MonthlyPayment);
        draft.Field("months", "Resterende looptijd op peildatum (maanden)", "number", item.RemainingMonths.ToString(CultureInfo.InvariantCulture));
        draft.DateField("fixedUntil", "Rente vast tot en met (optioneel)", item.FixedRateUntil, false);
        draft.Apply = d => d.Mortgage = item with { Name = draft.Text("name"), OwnerIds = draft.Fields.Single(f => f.Key == "owners").OwnerIds.ToList(),
            ReferencePrincipal = draft.Number("principal"), ReferenceDate = draft.Date("date"), AnnualInterestPercent = draft.Number("rate"), MonthlyPayment = draft.Number("payment"),
            RemainingMonths = int.TryParse(draft.Text("months"), out var months) ? months : 0, FixedRateUntil = draft.Text("fixedUntil").Length == 0 ? null : draft.Date("fixedUntil") }; return draft;
    }
}
