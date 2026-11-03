namespace Huishoudboek.Core;

public static class Validation
{
    public static void Validate(HouseholdData data)
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static void Unique(IEnumerable<Guid> ids, string name)
        {
            var list = ids.ToList();
            Require(list.All(id => id != Guid.Empty) && list.Distinct().Count() == list.Count,
                $"Ongeldige of dubbele identificatie in {name}.");
        }
        static bool Money(decimal amount) => decimal.Round(amount, 2) == amount;
        Unique(data.Owners.Select(x => x.Id), "eigenaren");
        Unique(data.Accounts.Select(x => x.Id), "rekeningen");
        Unique(data.Categories.Select(x => x.Id), "categorieën");
        Unique(data.Transactions.Select(x => x.Id), "transacties");
        Unique(data.RecurringPayments.Select(x => x.Id), "vaste posten");
        Unique(data.BudgetOverrides.Select(x => x.Id), "maandbudgetten");
        Unique(data.Valuations.Select(x => x.Id), "waarderingen");
        Unique(data.ImportBatches.Select(x => x.Id), "imports");
        Unique(data.ImportProfiles.Select(x => x.Id), "importprofielen");
        Unique(data.Rules.Select(x => x.Id), "regels");
        var owners = data.Owners.Select(o => o.Id).ToHashSet();
        var accounts = data.Accounts.Select(a => a.Id).ToHashSet();
        var categories = data.Categories.Select(c => c.Id).ToHashSet();
        var recurring = data.RecurringPayments.Select(r => r.Id).ToHashSet();
        var batches = data.ImportBatches.Where(b => !b.Undone).Select(b => b.Id).ToHashSet();
        Require(data.Owners.All(o => !string.IsNullOrWhiteSpace(o.Name)), "Geef iedere eigenaar een naam.");
        foreach (var a in data.Accounts)
        {
            Require(!string.IsNullOrWhiteSpace(a.Name), "Geef iedere rekening een naam.");
            Require(Enum.IsDefined(a.Kind), "Onbekend rekeningtype.");
            Require(a.OwnerIds.Count > 0 && a.OwnerIds.All(owners.Contains), "Kies minstens één geldige eigenaar per rekening.");
            Require(a.SavingsGoal is null or > 0, "Een spaardoel moet positief zijn.");
            Require(Money(a.OpeningBalance) && (a.SavingsGoal == null || Money(a.SavingsGoal.Value)), "Rekeningsaldi en doelen hebben maximaal twee decimalen.");
        }
        Require(data.Categories.All(c => !string.IsNullOrWhiteSpace(c.Name) && c.MonthlyBudget >= 0 && Money(c.MonthlyBudget)), "Ongeldige categorie of budget (maximaal twee decimalen).");
        foreach (var t in data.Transactions)
        {
            Require(accounts.Contains(t.AccountId) && !string.IsNullOrWhiteSpace(t.Description), "Transactie mist een rekening of omschrijving.");
            Require(t.UserDescription != null, "Laat de eigen omschrijving leeg als u geen toelichting wilt toevoegen.");
            Require(t.Amount != 0 && decimal.Round(t.Amount, 2) == t.Amount, "Transactiebedrag moet ongelijk aan nul zijn en maximaal twee decimalen hebben.");
            Require(Enum.IsDefined(t.Kind), "Onbekend transactietype.");
            Require(t.CategoryId == null || categories.Contains(t.CategoryId.Value), "Onbekende categorie.");
            Require(t.RecurringPaymentId == null || recurring.Contains(t.RecurringPaymentId.Value), "Onbekende vaste post.");
            Require(t.ImportBatchId == null || batches.Contains(t.ImportBatchId.Value), "Onbekende of teruggedraaide import.");
            Require(t.PrincipalComponent >= 0 && t.PrincipalComponent <= Math.Abs(t.Amount), "Ongeldige hypotheekaflossing.");
            Require(Money(t.PrincipalComponent) && (t.StatementBalance == null || Money(t.StatementBalance.Value)), "Aflossing en banksaldo hebben maximaal twee decimalen.");
            Require(t.Kind == TransactionKind.Mortgage || t.PrincipalComponent == 0, "Aflossing hoort alleen bij een hypotheekbetaling.");
            Require(t.Kind != TransactionKind.Mortgage || t.Amount < 0 && data.Mortgage != null, "Een hypotheekbetaling vereist een hypotheek en een negatief bedrag.");
            if (t.Kind == TransactionKind.Transfer)
            {
                Require(t.Amount < 0 && t.CounterAccountId != null && accounts.Contains(t.CounterAccountId.Value) && t.CounterAccountId != t.AccountId,
                    "Een overboeking heeft een negatief bedrag en twee verschillende rekeningen nodig.");
                Require(t.RecurringPaymentId == null, "Een overboeking kan niet aan een vaste post worden gekoppeld.");
            }
            else Require(t.CounterAccountId == null, "Alleen een overboeking heeft een tegenrekening.");
            if (t.RecurringPaymentId is { } recurringId)
            {
                var match = data.RecurringPayments.Single(r => r.Id == recurringId);
                Require(match.AccountId == t.AccountId && Math.Sign(match.Amount) == Math.Sign(t.Amount), "Vaste post hoort bij een andere rekening of betaalrichting.");
                Require(!match.IsMortgage || t.Kind == TransactionKind.Mortgage, "De gekoppelde vaste post is een hypotheekbetaling; kies dat transactietype.");
            }
            var affected = data.Accounts.Where(a => a.Id == t.AccountId || a.Id == t.CounterAccountId);
            Require(affected.All(a => t.Date >= a.OpeningDate), "Transactie ligt vóór de openingsdatum van een rekening. Pas eerst de openingsdatum en het beginsaldo aan.");
        }
        var links = data.Transactions.Where(t => t.RecurringPaymentId != null).Select(t => (t.RecurringPaymentId, t.Date.Year, t.Date.Month)).ToList();
        Require(links.Distinct().Count() == links.Count, "Per maand kan één transactie aan dezelfde vaste post worden gekoppeld. Laat extra of deelbetalingen ongekoppeld.");
        foreach (var r in data.RecurringPayments)
        {
            Require(!string.IsNullOrWhiteSpace(r.Name) && accounts.Contains(r.AccountId) && r.Amount != 0 && decimal.Round(r.Amount, 2) == r.Amount,
                "Ongeldige vaste post.");
            Require(r.DayOfMonth is >= 1 and <= 31 && (r.EndDate == null || r.EndDate >= r.StartDate), "Ongeldige planning.");
            Require(r.CategoryId == null || categories.Contains(r.CategoryId.Value), "Onbekende categorie bij vaste post.");
            Require(!r.IsMortgage || r.Amount < 0 && data.Mortgage != null, "Een hypotheekpost vereist een hypotheek en een negatief bedrag.");
        }
        Require(data.BudgetOverrides.All(b => categories.Contains(b.CategoryId) && b.Year is >= 1900 and <= 2200 && b.Month is >= 1 and <= 12 && b.Amount >= 0 && Money(b.Amount)), "Ongeldig maandbudget.");
        Require(data.BudgetOverrides.Select(b => (b.CategoryId, b.Year, b.Month)).Distinct().Count() == data.BudgetOverrides.Count, "Dubbel maandbudget.");
        Require(data.Rules.All(r => !string.IsNullOrWhiteSpace(r.Contains) && categories.Contains(r.CategoryId)), "Ongeldige categorisatieregel.");
        Require(data.Valuations.All(v => data.Accounts.Any(a => a.Id == v.AccountId && a.Kind == AccountKind.Investment && v.Date >= a.OpeningDate) && v.SecuritiesValue >= 0 && Money(v.SecuritiesValue)), "Ongeldige effectenwaardering.");
        Require(data.Valuations.Select(v => (v.AccountId, v.Date)).Distinct().Count() == data.Valuations.Count, "Per rekening en datum is één waardering toegestaan.");
        Require(data.ImportBatches.All(b => accounts.Contains(b.AccountId) && b.RowCount >= 0), "Ongeldige importgeschiedenis.");
        if (data.Mortgage is { } m)
        {
            Require(m.ReferencePrincipal > 0 && m.MonthlyPayment > 0 && m.AnnualInterestPercent is >= 0 and <= 100 && m.RemainingMonths is > 0 and <= 600,
                "Controleer hypotheekbedrag, rente, maandbedrag en looptijd.");
            Require(Money(m.ReferencePrincipal) && Money(m.MonthlyPayment), "Hypotheekbedragen hebben maximaal twee decimalen.");
            Require(m.OwnerIds.Count > 0 && m.OwnerIds.All(owners.Contains), "Kies minstens één eigenaar voor de hypotheek.");
            Require(m.FixedRateUntil == null || m.FixedRateUntil >= m.ReferenceDate, "Rentevaste einddatum ligt vóór de peildatum.");
            Require(data.Transactions.Where(t => t.Kind == TransactionKind.Mortgage && t.Date >= m.ReferenceDate).Sum(t => t.PrincipalComponent) <= m.ReferencePrincipal,
                "Geregistreerde aflossingen overschrijden de hypotheekschuld op de peildatum.");
        }
    }
}
