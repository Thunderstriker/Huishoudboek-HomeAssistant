namespace Huishoudboek.Service;

public sealed class ServiceOptions
{
    public required string DataDirectory { get; init; }
    public required string EditorToken { get; init; }
    public required string ReaderToken { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DataDirectory) || !Path.IsPathFullyQualified(DataDirectory)) throw new InvalidOperationException("Stel een absoluut pad in voor Huishoudboek:DataDirectory.");
        if (string.IsNullOrWhiteSpace(EditorToken) || string.IsNullOrWhiteSpace(ReaderToken) ||
            EditorToken.Length < 32 || ReaderToken.Length < 32 || EditorToken == ReaderToken ||
            EditorToken.Any(char.IsWhiteSpace) || ReaderToken.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Stel twee verschillende geheime sleutels van minstens 32 tekens in: EditorToken en ReaderToken.");
    }
}
