namespace Huishoudboek.Web;

/// <summary>Stable identity colours, never dependent on spending rank, culture or process hash seeds.</summary>
public static class CategoryColors
{
    public const string Other = "#8393a5";
    public const string Uncategorized = "#8b7e70";
    private static readonly string[] Palette = ["#32836b", "#526dc1", "#be6432", "#8556a3", "#b18a28",
        "#327eab", "#b25173", "#6b833c", "#8271b5", "#aa6d46", "#288989", "#ad554a"];

    public static string For(Guid id)
    {
        if (id == Guid.Empty) return Uncategorized;
        uint hash = 2166136261;
        foreach (var b in id.ToByteArray()) hash = unchecked((hash ^ b) * 16777619);
        return Palette[hash % Palette.Length];
    }
}
