namespace Huishoudboek.Core;

/// <summary>Category selection affects automatic estimates only, never actual or scheduled cash movements.</summary>
public static class SpendingForecast
{
    public static void InitializeDefaults(HouseholdData data)
    {
        // Names only seed legacy/new administrations. Persist IDs so renaming a category keeps its selection.
        data.InsightSettings.SpendingForecastCategoryIds ??= data.Categories
            .Where(c => Eligible(c) && (string.Equals(c.Name.Trim(), "Voeding", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Name.Trim(), "Medische kosten", StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.Id).ToList();
    }

    public static bool Eligible(Category category) => category.Purpose == CategoryPurpose.Normal && !category.IsExceptional;

    public static IReadOnlySet<Guid> CategoryIds(HouseholdData data)
    {
        var selected = data.InsightSettings.SpendingForecastCategoryIds;
        return data.Categories.Where(c => Eligible(c) && selected?.Contains(c.Id) == true).Select(c => c.Id).ToHashSet();
    }

    public static void SetIncluded(HouseholdData data, Guid categoryId, bool included)
    {
        if (!data.Categories.Any(c => c.Id == categoryId && Eligible(c)))
            throw new InvalidOperationException("Kies een gewone, niet-uitzonderlijke categorie voor de uitgavenraming.");
        InitializeDefaults(data);
        var selected = data.InsightSettings.SpendingForecastCategoryIds!;
        selected.RemoveAll(id => id == categoryId);
        if (included) selected.Add(categoryId);
    }
}
