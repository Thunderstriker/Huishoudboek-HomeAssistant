namespace Huishoudboek.Core;

public sealed record CategoryTreeEntry(Category Category, int Depth, string Path);

/// <summary>Category organization only: budgets, accounting and category IDs remain independent.</summary>
public static class CategoryHierarchy
{
    public static IReadOnlyList<CategoryTreeEntry> Ordered(HouseholdData data)
    {
        var children = data.Categories.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Id)
            .ToLookup(c => c.ParentId ?? Guid.Empty);
        var pending = new Stack<CategoryTreeEntry>(children[Guid.Empty].Reverse().Select(c => new CategoryTreeEntry(c, 0, c.Name)));
        var result = new List<CategoryTreeEntry>();
        while (pending.TryPop(out var entry))
        {
            result.Add(entry);
            foreach (var child in children[entry.Category.Id].Reverse())
                pending.Push(new(child, entry.Depth + 1, $"{entry.Path} › {child.Name}"));
        }
        return result;
    }

    public static string Path(HouseholdData data, Guid categoryId)
    {
        var categories = data.Categories.ToDictionary(c => c.Id);
        var names = new Stack<string>();
        var visited = new HashSet<Guid>();
        Guid? current = categoryId;
        while (current is { } id && categories.TryGetValue(id, out var category) && visited.Add(id))
        {
            names.Push(category.Name);
            current = category.ParentId;
        }
        return string.Join(" › ", names);
    }

    public static IReadOnlySet<Guid> SubtreeIds(HouseholdData data, Guid categoryId)
    {
        var children = data.Categories.ToLookup(c => c.ParentId ?? Guid.Empty);
        var result = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        pending.Push(categoryId);
        while (pending.TryPop(out var id))
            if (result.Add(id))
                foreach (var child in children[id]) pending.Push(child.Id);
        return result;
    }

    public static void ValidateParent(HouseholdData data, Guid categoryId, Guid? parentId)
    {
        var categories = data.Categories.ToDictionary(c => c.Id);
        var visited = new HashSet<Guid> { categoryId };
        while (parentId is { } id)
        {
            if (!visited.Add(id)) throw new InvalidOperationException("Een categorie kan niet onder zichzelf of een eigen subcategorie vallen.");
            if (!categories.TryGetValue(id, out var parent)) throw new InvalidOperationException("Kies een bestaande bovenliggende categorie.");
            parentId = parent.ParentId;
        }
    }

    public static void Validate(HouseholdData data)
    {
        var categories = data.Categories.ToDictionary(c => c.Id);
        var complete = new HashSet<Guid>();
        foreach (var category in data.Categories)
        {
            var chain = new HashSet<Guid>();
            Guid? current = category.Id;
            while (current is { } id && !complete.Contains(id))
            {
                if (!chain.Add(id)) throw new InvalidOperationException("Een categorie kan niet onder zichzelf of een eigen subcategorie vallen.");
                if (!categories.TryGetValue(id, out var item)) throw new InvalidOperationException("Kies een bestaande bovenliggende categorie.");
                current = item.ParentId;
            }
            complete.UnionWith(chain);
        }
    }
}
