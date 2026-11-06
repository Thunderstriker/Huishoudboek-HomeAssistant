using System.Security.Cryptography;
using System.Text.Json;

namespace Huishoudboek.Service;

/// <summary>Explicit developer-only setup. Never adopts an existing administration.</summary>
public static class LocalPreviewBootstrap
{
    private sealed record Settings(int PreviewVersion, ServiceOptions Huishoudboek);

    public static string Prepare(string root)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("Gebruik een absoluut pad voor de lokale preview.", nameof(root));
        root = Path.GetFullPath(root);
        var configuration = Path.Combine(root, "preview.json");
        var directory = Path.Combine(root, "administration");
        if (File.Exists(configuration))
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(configuration))
                ?? throw new InvalidDataException("De previewconfiguratie is ongeldig.");
            if (settings.PreviewVersion != 1 || settings.Huishoudboek == null)
                throw new InvalidDataException("Dit is geen ondersteunde previewconfiguratie.");
            settings.Huishoudboek.Validate();
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Path.GetFullPath(settings.Huishoudboek.DataDirectory), directory, comparison))
                throw new InvalidDataException("De preview moet zijn eigen administratie gebruiken.");
            // Validate and upgrade only this preview copy, under its writer lease and with the normal safety backup.
            // Missing databases or service markers are never silently recreated.
            using var administration = new AdministrationCoordinator(directory, allowUpgrade: true);
            return configuration;
        }

        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidDataException("De previewmap is niet leeg en heeft geen preview.json. Bestaande bestanden zijn niet gewijzigd.");
        var options = new Settings(1,
            new ServiceOptions { DataDirectory = directory, EditorToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ReaderToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) });

        Directory.CreateDirectory(root);
        using (var administration = new AdministrationCoordinator(directory, initialize: true))
            administration.Replace(administration.Read().Revision, LocalPreviewData.Create(DateOnly.FromDateTime(DateTime.Today)));
        using (var file = new FileStream(configuration, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(file, options, new JsonSerializerOptions { WriteIndented = true, IgnoreReadOnlyProperties = true });
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(configuration, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return configuration;
    }

    public static string[] Arguments(string root, int port)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "Kies een poort van 1024 tot 65535.");
        return ["--config", Prepare(root), "--urls", $"http://127.0.0.1:{port}"];
    }
}
