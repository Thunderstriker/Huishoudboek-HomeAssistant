namespace Huishoudboek.Core;

public sealed record DashboardOverview(DateOnly AsOf, MonthlyReport Report, decimal CurrentCash, decimal SavingsBalance,
    decimal NetWorth, decimal ProjectedCurrentCash, string Status, string Explanation, string ForecastNote);
public sealed record AnnualMonth(int Month, decimal Income, decimal Outflow, decimal Savings)
{
    public decimal Available => Income - Outflow - Savings;
}
public sealed record AnnualOverview(IReadOnlyList<AnnualMonth> Months, IReadOnlyList<CategoryResult> Categories)
{
    public decimal Income => Months.Sum(m => m.Income);
    public decimal Outflow => Months.Sum(m => m.Outflow);
    public decimal Savings => Months.Sum(m => m.Savings);
    public decimal Available => Income - Outflow - Savings;
}
public sealed record CategoryBudgetOverview(Guid Id, string Name, decimal DefaultBudget, decimal Budget, decimal Spent)
{
    public decimal Remaining => Budget - Spent;
    public bool IsOverBudget => Budget > 0 && Spent > Budget;
}
public sealed record MortgageProjection(decimal Balance, IReadOnlyList<MortgageRow> Rows)
{
    public decimal TotalInterest => Rows.Sum(r => r.Interest);
}

/// <summary>Detailed desktop/web reports. Unlike the public reader reports, explanations can contain bank text.</summary>
public static class DetailedReporting
{
    public static IReadOnlyList<CategoryBudgetOverview> Budgets(HouseholdData data, int year, int month, DateOnly today, IReadOnlySet<Guid>? scope = null)
    {
        var report = Finance.Report(data, year, month, today, scope);
        return data.Categories.Select(c => new CategoryBudgetOverview(c.Id, c.Name, c.MonthlyBudget,
            data.BudgetOverrides.FirstOrDefault(b => b.CategoryId == c.Id && b.Year == year && b.Month == month)?.Amount ?? c.MonthlyBudget,
            report.Categories.FirstOrDefault(r => r.Id == c.Id)?.Spent ?? 0)).ToList();
    }
    public static MortgageProjection Mortgage(HouseholdData data, DateOnly today)
    {
        var balance = Finance.MortgageBalance(data, today);
        return new(balance, data.Mortgage is { } mortgage ? Finance.MortgageSchedule(mortgage, balance, today) : []);
    }
    public static DashboardOverview Dashboard(HouseholdData data, int year, int month, DateOnly today, Guid? ownerId = null)
    {
        var scope = data.Accounts.Where(a => ownerId == null || a.OwnerIds.Contains(ownerId.Value)).Select(a => a.Id).ToHashSet();
        var end = new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
        var asOf = today < end ? today : end;
        var report = Finance.Report(data, year, month, today, scope);
        var big = report.Categories.Where(c => c.IsOverBudget).Take(3).ToList();
        var unbudgeted = report.Categories.Where(c => c.Budget == 0 && c.Spent > 0).Sum(c => c.Spent);
        var culture = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");
        string Money(decimal v) => v.ToString("C2", culture);
        var status = data.Accounts.Count == 0 ? "Welkom! Voeg eerst eigenaren en rekeningen toe." :
            report.Available < 0 ? "Meer uitgegeven en opzijgezet dan ontvangen" : big.Count > 0 ? "Let op: categoriebudget overschreden" :
            report.Forecast.Available < 0 ? "Let op: verwacht tekort einde maand" : report.Budget == 0 ? "Stel budgetten in om overschrijdingen te herkennen" : "Binnen de ingestelde categoriebudgetten";
        var reason = big.Count > 0 ? string.Join(" · ", big.Select(c => $"{c.Name}: {Money(c.Overspent)} boven budget")) : "Geen overschrijding bij categorieën met een ingesteld budget.";
        if (unbudgeted > 0) reason += $" {Money(unbudgeted)} uitgaven zonder ingesteld budget.";
        if (report.Forecast.Available < 0) reason += $" Verwacht tekort uit maandinkomen: {Money(-report.Forecast.Available)}.";
        var linked = TransferMatching.ConfirmedTransactionIds(data);
        var largest = TransactionAccounting.Lines(data, data.Transactions.Where(t => t.Date.Year == year && t.Date.Month == month &&
            t.Date <= today && scope.Contains(t.AccountId))).Where(l => l.Spending > 0 && big.Any(c => c.Id == l.CategoryId))
            .OrderByDescending(l => l.Spending).Take(2).ToList();
        if (largest.Count > 0) reason += " Grootste betalingen: " + string.Join("; ", largest.Select(l => $"{l.Transaction.Description} ({Money(l.Spending)})")) + ".";
        var note = $"Schatting: geboekte bedragen + nog niet gekoppelde vaste posten + {Money(report.Forecast.VariableEstimate)} extra variabele uitgaven op basis van het daggemiddelde. " +
            (report.Forecast.OverdueCount > 0 ? $"{report.Forecast.OverdueCount} vervallen vaste post(en) nog niet gekoppeld; controleer op dubbeltelling. " : "") +
            "Geen automatische prognose van toekomstige spaaroverboekingen. " +
            (report.Forecast.LimitedHistory ? "Weinig gegevens: de prognose is beperkt betrouwbaar. " : "") +
            "Het verwachte eindsaldo van betaalrekeningen gebruikt de dagelijkse cashprognose met maximaal 90 dagen uitgavenhistorie; dit is een andere schatting dan het maandresultaat. " +
            (end.DayNumber - today.DayNumber > CashFlow.IncomeSearchDays ? "Verre toekomstige maand: het verwachte saldo begint bij de saldi van vandaag, zonder tussenliggende maanden. " : "") +
            "Onvolledige imports kunnen het beeld vertekenen. Hypotheekbetalingen tellen volledig als maandlast; alleen geregistreerde aflossing verlaagt de schuld.";
        var accounts = data.Accounts.Where(a => scope.Contains(a.Id)).ToList();
        var debt = data.Mortgage != null && (ownerId == null || data.Mortgage.OwnerIds.Contains(ownerId.Value)) ? Finance.MortgageBalance(data, asOf) : 0;
        return new(asOf, report, accounts.Where(a => a.Kind == AccountKind.Current).Sum(a => Finance.Balance(data, a, asOf)),
            accounts.Where(a => a.Kind == AccountKind.Savings).Sum(a => Finance.Balance(data, a, asOf)),
            accounts.Sum(a => Finance.Balance(data, a, asOf) + Finance.SecuritiesValue(data, a.Id, asOf)) - debt,
            Finance.ProjectedCurrentBalance(data, year, month, today, scope), status, reason, note);
    }

    public static AnnualOverview Year(HouseholdData data, int year, DateOnly today, Guid? ownerId = null)
    {
        var scope = data.Accounts.Where(a => ownerId == null || a.OwnerIds.Contains(ownerId.Value)).Select(a => a.Id).ToHashSet();
        var reports = Enumerable.Range(1, 12).Select(m => Finance.Report(data, year, m, today, scope)).ToList();
        var months = reports.Select((r, i) => new AnnualMonth(i + 1, r.Income, r.Outflow, r.Savings)).ToList();
        var categories = data.Categories.Select(c => new CategoryResult(c.Id, c.Name,
            Enumerable.Range(1, 12).Sum(m => data.BudgetOverrides.FirstOrDefault(b => b.CategoryId == c.Id && b.Year == year && b.Month == m)?.Amount ?? c.MonthlyBudget),
            reports.SelectMany(r => r.Categories).Where(r => r.Id == c.Id).Sum(r => r.Spent)))
            .Concat(reports.SelectMany(r => r.Categories).Where(c => c.Id == Guid.Empty).GroupBy(c => c.Id).Select(g => new CategoryResult(Guid.Empty, "Zonder categorie", 0, g.Sum(c => c.Spent))))
            .OrderByDescending(c => c.Spent).ToList();
        return new(months, categories);
    }
}
