namespace Huishoudboek.Core;

public enum FinancialDayRisk { Comfortable, Busy, BufferRisk, Shortfall }

public static class FinancialCalendar
{
    public static FinancialDayRisk Risk(DailyBalance day, Account account, decimal dailyBaseline, int outgoingCount)
    {
        if (day.LowestBalance < 0) return FinancialDayRisk.Shortfall;
        if (day.LowestBalance < account.MinimumCashBuffer + account.EarmarkedAmount || outgoingCount >= 3)
            return FinancialDayRisk.BufferRisk;
        return day.Payments + day.VariableSpending > Math.Max(25, dailyBaseline * 2)
            ? FinancialDayRisk.Busy : FinancialDayRisk.Comfortable;
    }
}
