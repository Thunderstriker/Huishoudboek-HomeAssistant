namespace Huishoudboek.Core;

public sealed record AccountSummary(Guid Id, string Name, string Kind, decimal Cash,
    decimal Securities, decimal Total, DateOnly? ValuationDate, decimal? SavingsGoal);
public sealed record AccountOverview(DateOnly AsOf, string Currency, IReadOnlyList<AccountSummary> Accounts,
    decimal CurrentTotal, decimal SavingsTotal, decimal InvestmentTotal);
public sealed record MonthOverview(string Period, DateOnly AsOf, string Currency, decimal Income,
    decimal Outflow, decimal Savings, decimal Available, IReadOnlyList<CategoryResult> Categories,
    IReadOnlyList<string> Warnings);

/// <summary>Client-neutral, actual-only reports. No personal bank references or transaction descriptions.</summary>
public static class Reporting
{
    public static AccountOverview Accounts(HouseholdData data, DateOnly today)
    {
        var accounts = data.Accounts.Select(a =>
        {
            var cash = Finance.Balance(data, a, today);
            var valuation = a.Kind == AccountKind.Investment
                ? data.Valuations.Where(v => v.AccountId == a.Id && v.Date <= today).OrderByDescending(v => v.Date).FirstOrDefault()
                : null;
            var securities = valuation?.SecuritiesValue ?? 0;
            return new AccountSummary(a.Id, a.Name, a.Kind.ToString().ToLowerInvariant(), cash, securities,
                cash + securities, valuation?.Date, a.SavingsGoal);
        }).ToList();
        return new(today, "EUR", accounts, accounts.Where(a => a.Kind == "current").Sum(a => a.Total),
            accounts.Where(a => a.Kind == "savings").Sum(a => a.Total), accounts.Where(a => a.Kind == "investment").Sum(a => a.Total));
    }

    public static MonthOverview Month(HouseholdData data, int year, int month, DateOnly today)
    {
        if (year is < 1900 or > 2200 || month is < 1 or > 12)
            throw new InvalidOperationException("Kies een geldige maand tussen 1900 en 2200.");
        var report = Finance.Report(data, year, month, today);
        var end = new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
        var categories = data.Categories.Where(c => c.Purpose == CategoryPurpose.Normal).Select(c => report.Categories.FirstOrDefault(r => r.Id == c.Id)
            ?? new CategoryResult(c.Id, c.Name,
                data.BudgetOverrides.FirstOrDefault(b => b.CategoryId == c.Id && b.Year == year && b.Month == month)?.Amount ?? c.MonthlyBudget, 0))
            .Concat(report.Categories.Where(c => c.Id == Guid.Empty)).ToList();
        var warnings = new List<string> { "Werkelijke boekingen, geen prognose. Onvolledige imports kunnen het beeld vertekenen.",
            "Budget 0 betekent geen limiet. Maandoverschot is geen rekeningsaldo. Interne overboekingen zijn geen uitgaven." };
        if (new DateOnly(year, month, 1) > today) warnings.Add("Toekomstige maand: nog geen werkelijke boekingen.");
        return new($"{year:D4}-{month:D2}", today < end ? today : end, "EUR", report.Income, report.Outflow,
            report.Savings, report.Available, categories, warnings);
    }
}
