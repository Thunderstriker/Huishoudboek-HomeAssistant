using Huishoudboek.Core;

namespace Huishoudboek.Web;

public sealed record CashTimelinePoint(DateOnly Date, decimal Closing, decimal Lowest, IReadOnlyList<string> Income,
    IReadOnlyList<string> Payments, decimal IncomeAmount = 0, decimal PaymentAmount = 0);

public static class CashTimeline
{
    public static IReadOnlyList<CashTimelinePoint> Points(AccountCashFlow forecast, DateOnly today, int horizon)
    {
        if (horizon is not (7 or 30 or 90)) throw new ArgumentOutOfRangeException(nameof(horizon));
        var events = forecast.Events.Where(e => !e.IsUncertain && (e.PurchaseDate == null || e.PurchaseDate > today)).ToLookup(e => e.Date);
        return new[] { new CashTimelinePoint(today, forecast.CurrentBalance, forecast.CurrentBalance, [], []) }
            .Concat(forecast.Days.Where(d => d.Date > today && d.Date <= today.AddDays(horizon)).OrderBy(d => d.Date)
                .Select(d => new CashTimelinePoint(d.Date, d.ClosingBalance, d.LowestBalance,
                    events[d.Date].Where(e => e.Amount > 0).Select(e => e.Name).ToArray(),
                    events[d.Date].Where(e => e.Amount < 0).Select(e => e.Name).ToArray(),
                    events[d.Date].Where(e => e.Amount > 0).Sum(e => e.Amount),
                    -events[d.Date].Where(e => e.Amount < 0).Sum(e => e.Amount)))).ToList();
    }
}
