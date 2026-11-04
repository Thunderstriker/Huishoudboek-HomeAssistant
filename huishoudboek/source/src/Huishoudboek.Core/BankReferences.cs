namespace Huishoudboek.Core;

public static class BankReferences
{
    public static string Normalize(string reference) =>
        string.Concat(reference.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
