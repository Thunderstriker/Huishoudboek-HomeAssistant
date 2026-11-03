namespace Huishoudboek.Infrastructure;

/// <summary>Finds explicit connections first; local website discovery requires prior service adoption.</summary>
public static class DesktopServiceConfiguration
{
    public static string? ResolvePath(string[] arguments, string? environmentPath, string localApplicationData, bool demo)
    {
        if (demo) return null;
        var index = Array.IndexOf(arguments, "--service-config");
        if (index >= 0)
        {
            var path = arguments.ElementAtOrDefault(index + 1);
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Geef een serviceconfiguratiebestand op.");
            return path;
        }
        if (!string.IsNullOrWhiteSpace(environmentPath)) return environmentPath;
        var root = Path.Combine(localApplicationData, "Huishoudboek");
        var localPath = Path.Combine(root, "Website", "desktop-service.json");
        return File.Exists(Path.Combine(root, "Data", AdministrationLease.ServiceMarker)) && File.Exists(localPath)
            ? localPath : null;
    }
}
