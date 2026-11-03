namespace Huishoudboek.Core;

public sealed record CategorizationPreview(IReadOnlyList<Transaction> Matches, int UncategorizedCount,
    int EarlierConflictCount, bool ConflictingExactRule, bool ReusesRule);

public sealed record CategoryDeletionPreview(string Name, decimal MonthlyBudget, int TransactionCount,
    int RecurringPaymentCount, int RuleCount, int BudgetOverrideCount);

/// <summary>Category edits with an optional personal description. Never changes bank text, amounts, types or recurring links.</summary>
public static class Categorization
{
    public static (decimal Income, decimal Outflow) ReviewTotals(IEnumerable<Transaction> transactions)
    {
        var ordinary = transactions.Where(t => t.Kind != TransactionKind.Transfer).ToList();
        return (ordinary.Where(t => t.Amount > 0).Sum(t => t.Amount), -ordinary.Where(t => t.Amount < 0).Sum(t => t.Amount));
    }

    public static void RenameCategory(HouseholdData data, Guid categoryId, string name)
    {
        var category = data.Categories.FirstOrDefault(c => c.Id == categoryId)
            ?? throw new InvalidOperationException("Kies een bestaande categorie om te hernoemen.");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Vul een categorienaam in.");
        name = name.Trim();
        if (data.Categories.Any(c => c.Id != categoryId && string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Er bestaat al een categorie met deze naam. Kies een andere naam.");
        category.Name = name;
    }

    public static CategoryDeletionPreview PreviewCategoryDeletion(HouseholdData data, Guid categoryId)
    {
        var category = data.Categories.FirstOrDefault(c => c.Id == categoryId && categoryId != Guid.Empty)
            ?? throw new InvalidOperationException("Kies een bestaande categorie om te verwijderen.");
        return new(category.Name, category.MonthlyBudget, data.Transactions.Count(t => t.CategoryId == categoryId),
            data.RecurringPayments.Count(r => r.CategoryId == categoryId), data.Rules.Count(r => r.CategoryId == categoryId),
            data.BudgetOverrides.Count(b => b.CategoryId == categoryId));
    }

    public static void DeleteCategory(HouseholdData data, Guid categoryId)
    {
        if (categoryId == Guid.Empty || !data.Categories.Any(c => c.Id == categoryId))
            throw new InvalidOperationException("Kies een bestaande categorie om te verwijderen.");
        // Only labels and category settings are removed, never financial records or recurring links.
        foreach (var t in data.Transactions.Where(t => t.CategoryId == categoryId)) t.CategoryId = null;
        foreach (var r in data.RecurringPayments.Where(r => r.CategoryId == categoryId)) r.CategoryId = null;
        data.Rules.RemoveAll(r => r.CategoryId == categoryId);
        data.BudgetOverrides.RemoveAll(b => b.CategoryId == categoryId);
        data.Categories.RemoveAll(c => c.Id == categoryId);
    }

    public static CategorizationRule? Match(HouseholdData data, string description) =>
        data.Rules.FirstOrDefault(r => description.Contains(r.Contains, StringComparison.OrdinalIgnoreCase));

    public static CategorizationPreview Preview(HouseholdData data, string text, Guid? categoryId,
        IReadOnlySet<Guid>? scope = null)
    {
        text = text.Trim();
        var matches = text.Length == 0 ? [] : data.Transactions.Where(t => t.Kind != TransactionKind.Transfer &&
            (scope == null || scope.Contains(t.Id)) && t.Description.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        var exact = data.Rules.Where(r => string.Equals(r.Contains.Trim(), text, StringComparison.OrdinalIgnoreCase)).ToList();
        return new(matches, matches.Count(t => t.CategoryId == null),
            matches.Count(t => Match(data, t.Description) is { } rule && rule.CategoryId != categoryId),
            exact.Any(r => r.CategoryId != categoryId), exact.Any(r => r.CategoryId == categoryId));
    }

    public static int Assign(HouseholdData data, IReadOnlySet<Guid> transactionIds, Guid? categoryId,
        string? rememberText = null, IReadOnlySet<Guid>? matchingUncategorizedIds = null, string? userDescription = null)
    {
        if (transactionIds.Count == 0) throw new InvalidOperationException("Selecteer eerst één of meer transacties.");
        if (categoryId != null && !data.Categories.Any(c => c.Id == categoryId))
            throw new InvalidOperationException("Kies een bestaande categorie.");
        var selected = data.Transactions.Where(t => transactionIds.Contains(t.Id)).ToList();
        if (selected.Count != transactionIds.Count) throw new InvalidOperationException("Een geselecteerde transactie bestaat niet meer. Vernieuw de selectie.");
        if (userDescription != null && selected.Count != 1)
            throw new InvalidOperationException("Voeg een eigen omschrijving toe aan één betaling tegelijk.");
        CategorizationRule? newRule = null;
        var additional = new List<Transaction>();
        if (rememberText != null)
        {
            var text = rememberText.Trim();
            if (text.Length == 0 || categoryId == null) throw new InvalidOperationException("Vul herkenningstekst in en kies een categorie om een regel te onthouden.");
            if (selected.Any(t => t.Kind == TransactionKind.Transfer))
                throw new InvalidOperationException("Tekstregels gelden niet voor interne overboekingen.");
            if (selected.Any(t => !t.Description.Contains(text, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("De herkenningstekst moet in alle geselecteerde omschrijvingen voorkomen.");
            var preview = Preview(data, text, categoryId);
            if (preview.ConflictingExactRule)
                throw new InvalidOperationException("Deze herkenningstekst heeft al een andere categorie. Bewerk de bestaande regel via Beheer & import.");
            if (!preview.ReusesRule) newRule = new CategorizationRule { Contains = text, CategoryId = categoryId.Value };
            additional = preview.Matches.Where(t => t.CategoryId == null && matchingUncategorizedIds?.Contains(t.Id) == true).ToList();
        }
        else if (matchingUncategorizedIds is { Count: > 0 })
            throw new InvalidOperationException("Kies eerst een herkenningsregel voor de overige transacties.");

        // Validate the entire request before mutating; the caller persists it in one atomic save.
        if (newRule != null) data.Rules.Add(newRule);
        var targets = selected.Concat(additional).DistinctBy(t => t.Id).ToList();
        foreach (var transaction in targets) transaction.CategoryId = categoryId;
        // A personal description belongs only to the current payment, never to a rule or its other matches.
        if (userDescription != null) selected[0].UserDescription = userDescription.Trim();
        return targets.Count;
    }
}
