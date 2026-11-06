using Huishoudboek.Core;

namespace Huishoudboek.Service;

/// <summary>Fictional, relative-date data seeded once in a new local preview, never in Home Assistant.</summary>
public static class LocalPreviewData
{
    public static HouseholdData Create(DateOnly today)
    {
        var data = HouseholdData.CreateEmpty();
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-12);
        var alex = new Owner { Name = "Alex (demo)" };
        var sam = new Owner { Name = "Sam (demo)" };
        data.Owners.AddRange([alex, sam]);
        var current = new Account { Name = "Gezamenlijk (demo)", Kind = AccountKind.Current, OpeningDate = start,
            OpeningBalance = 2500, MinimumCashBuffer = 1500, OwnerIds = [alex.Id, sam.Id] };
        var savings = new Account { Name = "Spaarbuffer (demo)", Kind = AccountKind.Savings, OpeningDate = start,
            OpeningBalance = 8000, SavingsGoal = 20000, OwnerIds = [alex.Id, sam.Id] };
        var investment = new Account { Name = "Beleggingen (demo)", Kind = AccountKind.Investment, OpeningDate = start,
            OwnerIds = [alex.Id], IncludeInFreedom = false };
        data.Accounts.AddRange([current, savings, investment]);
        var categories = data.Categories.ToDictionary(c => c.Name);
        foreach (var (name, budget) in new (string, decimal)[] { ("Wonen & hypotheek", 1250), ("Voeding", 500),
            ("Energie & water", 175), ("Verzekeringen", 95), ("Abonnementen", 60), ("Auto & vervoer", 150),
            ("Vrije tijd", 200), ("Kleding", 100), ("Verjaardagen & cadeaus", 75) })
            categories[name].MonthlyBudget = budget;
        foreach (var name in new[] { "Wonen & hypotheek", "Voeding", "Energie & water", "Verzekeringen", "Auto & vervoer" })
            categories[name].IsEssential = true;

        RecurringPayment Recurring(string name, string category, decimal amount, int day)
        {
            var payment = new RecurringPayment { Name = name, AccountId = current.Id, CategoryId = categories[category].Id,
                Amount = amount, DayOfMonth = day, StartDate = start, MatchText = name };
            data.RecurringPayments.Add(payment);
            return payment;
        }
        var recurring = new[] { Recurring("Demo salaris Alex", "Salaris", 3100, 1), Recurring("Demo salaris Sam", "Salaris", 2200, 1),
            Recurring("Demo huur", "Wonen & hypotheek", -1250, 2), Recurring("Demo energie", "Energie & water", -165, 5),
            Recurring("Demo verzekering", "Verzekeringen", -92, 8), Recurring("Demo internet", "Abonnementen", -52, 12),
            Recurring("Demo sparen", "Sparen & beleggen", -650, 15) };

        for (var month = start; month <= today; month = month.AddMonths(1))
        {
            var end = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
            if (end > today) end = today;
            foreach (var payment in recurring)
            {
                var date = new DateOnly(month.Year, month.Month, payment.DayOfMonth);
                if (date > today) continue;
                data.Transactions.Add(new Transaction { AccountId = current.Id, Date = date, Description = payment.Name,
                    Amount = payment.Amount, CategoryId = payment.CategoryId, RecurringPaymentId = payment.Id, RecurringOccurrenceDate = date });
                if (payment.Name == "Demo sparen")
                    data.Transactions.Add(new Transaction { AccountId = savings.Id, Date = date, Description = "Demo spaarbijdrage",
                        Amount = -payment.Amount, CategoryId = categories["Sparen & beleggen"].Id });
            }
            void Spend(int day, string category, string description, decimal amount)
            {
                var date = new DateOnly(month.Year, month.Month, day);
                if (date <= today) data.Transactions.Add(new Transaction { AccountId = current.Id, Date = date,
                    Description = description, Amount = -amount, CategoryId = categories[category].Id });
            }
            foreach (var day in new[] { 3, 10, 17, 24 })
                Spend(day, "Voeding", "Demo supermarkt", 105 + (month.Month * 7 + day) % 35);
            Spend(7, "Vrije tijd", "Demo restaurant", 85 + month.Month * 3);
            Spend(21, "Vrije tijd", "Demo dagje uit", 48);
            Spend(11, "Auto & vervoer", "Demo vervoer", 90 + month.Month * 2);
            Spend(19, "Kleding", "Demo kleding", 65 + month.Month * 4);
            Spend(25, "Verjaardagen & cadeaus", "Demo cadeau", 45);
            foreach (var account in new[] { current, savings })
            {
                var transactions = data.Transactions.Where(t => t.AccountId == account.Id && t.Date >= month && t.Date <= end).ToList();
                var batch = new ImportBatch { AccountId = account.Id, FileName = $"demo-{month:yyyy-MM}.csv",
                    ImportedAt = end.ToDateTime(new TimeOnly(12, 0)), CoverageFrom = month, CoverageThrough = end, RowCount = transactions.Count };
                data.ImportBatches.Add(batch);
                foreach (var transaction in transactions) transaction.ImportBatchId = batch.Id;
            }
            data.Valuations.Add(new InvestmentValuation { AccountId = investment.Id, Date = end,
                SecuritiesValue = 12000 + ((month.Year - start.Year) * 12 + month.Month - start.Month) * 180 });
        }
        data.Rules.Add(new CategorizationRule { Contains = "Demo supermarkt", CategoryId = categories["Voeding"].Id });
        Validation.Validate(data);
        return data;
    }
}
