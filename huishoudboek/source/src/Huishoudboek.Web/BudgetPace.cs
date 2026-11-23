using Huishoudboek.Core;

namespace Huishoudboek.Web;

public sealed record BudgetPaceResult(decimal Usage, string StatusKey, decimal? ExpectedUsage = null);

public static class BudgetPace
{
    public static BudgetPaceResult Describe(HouseholdData data, Guid categoryId, decimal spent, decimal budget,
        int year, int month, DateOnly today, IReadOnlySet<Guid> scope)
    {
        var usage = budget > 0 ? Math.Max(0, spent / budget * 100) : 0;
        if (budget <= 0) return new(usage, "NoLimit");
        if (spent > budget) return new(usage, "Exceeded");
        var first = new DateOnly(year, month, 1); var end = first.AddMonths(1).AddDays(-1);
        if (first > today) return new(usage, "BudgetPaceNotStarted");
        var asOf = today < end ? today : end;
        var accounts = data.Accounts.Where(a => scope.Contains(a.Id) && a.OpeningDate <= asOf && a.Kind != AccountKind.Investment).ToList();
        if (accounts.Count == 0 || accounts.Any(a => !BankImportCoverage.Covers(data, a.Id, a.OpeningDate > first ? a.OpeningDate : first, asOf)))
            return new(usage, "BudgetPaceIncomplete");
        if (data.RecurringPayments.Any(p => p.CategoryId == categoryId && p.Amount < 0 && scope.Contains(p.AccountId) &&
            RecurringSchedule.Occurrences(p, first, end).Any()))
            return new(usage, "BudgetPaceScheduled");
        if (asOf == end) return new(usage, "BudgetPaceCompleted");
        var expected = (asOf.DayNumber - first.DayNumber + 1m) / end.Day * 100;
        return new(usage, usage > expected + 10 ? "BudgetPaceAhead" : "BudgetPaceOnTrack", expected);
    }
}
