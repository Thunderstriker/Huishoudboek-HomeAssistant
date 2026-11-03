using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

public interface IAdministrationStore : IHouseholdStore
{
    string DatabasePath { get; }
    bool IsRemote => false;
}

public sealed class AdministrationConflictException : InvalidOperationException
{
    public AdministrationConflictException() : base("De administratie is elders gewijzigd. Vernieuw de gegevens en probeer uw wijziging opnieuw. Er is niets overschreven.") { }
}

/// <summary>OS-enforced cross-session exclusion. The lock file is deliberately never deleted.</summary>
public sealed class AdministrationLease : IDisposable
{
    public const string ServiceMarker = "service-managed.txt";
    private readonly FileStream _stream;

    public AdministrationLease(string directory, bool service = false)
    {
        Directory.CreateDirectory(directory);
        try { _stream = new FileStream(Path.Combine(directory, "administration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("Deze administratie is al geopend door de desktop of de service."); }
        if (!service && File.Exists(Path.Combine(directory, ServiceMarker)))
        {
            _stream.Dispose();
            throw new InvalidOperationException("Deze administratie wordt beheerd door de service. Start de desktop met --service-config <bestand>. Open de database niet rechtstreeks.");
        }
    }

    public void Dispose() => _stream.Dispose();
}
