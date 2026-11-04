using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;

namespace Huishoudboek.Service;

/// <summary>One writer, including the complete read/validate/write operation. Revisions are invalidated on restart and restore.</summary>
public sealed class AdministrationCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly AdministrationLease _lease;
    private readonly SqliteHouseholdStore _store;
    private HouseholdData _data;
    private string _revision = NewRevision();
    public string DirectoryPath { get; }
    public Guid AdministrationId { get; }

    public AdministrationCoordinator(string directory, bool initialize = false, bool claim = false, bool allowUpgrade = false)
    {
        DirectoryPath = Path.GetFullPath(directory);
        if (!initialize && !Directory.Exists(DirectoryPath)) throw new InvalidDataException("De administratie bestaat niet. Gebruik --initialize alleen voor een nieuwe administratie.");
        _lease = new AdministrationLease(DirectoryPath, service: true);
        try
        {
            var path = Path.Combine(DirectoryPath, "huishoudboek.db");
            if (initialize && File.Exists(path)) throw new InvalidDataException("--initialize is alleen toegestaan voor een nieuwe administratie.");
            var marker = Path.Combine(DirectoryPath, AdministrationLease.ServiceMarker);
            if (!initialize && !claim && !File.Exists(marker))
                throw new InvalidDataException("Deze administratie is nog niet aan de service toegewezen. Maak eerst een back-up en start eenmalig met --claim.");
            // Home Assistant upgrades supported schemas under the writer lease, after validation and a SQLite-aware backup.
            // Normal Windows-service adoption still requires an explicit upgrade beforehand.
            _store = new SqliteHouseholdStore(path, allowCreate: initialize, allowUpgrade: allowUpgrade);
            _data = _store.Load();
            if (!File.Exists(marker)) File.WriteAllText(marker, Guid.NewGuid().ToString("D"));
            AdministrationId = Guid.Parse(File.ReadAllText(marker).Trim());
        }
        catch { _lease.Dispose(); throw; }
    }

    private static string NewRevision() => $"\"{Guid.NewGuid():N}\"";
    private static HouseholdData Clone(HouseholdData data) => JsonSerializer.Deserialize<HouseholdData>(JsonSerializer.SerializeToUtf8Bytes(data))!;

    public AdministrationSnapshot Read()
    {
        lock (_gate) return new(_revision, Clone(_data));
    }

    public AdministrationSnapshot Update(string? expected, Action<HouseholdData> change)
    {
        lock (_gate)
        {
            CheckRevision(expected);
            var candidate = Clone(_data);
            change(candidate);
            Validation.Validate(candidate);
            var isolated = Clone(candidate);
            var revision = NewRevision();
            _store.Save(isolated);
            _data = isolated;
            _revision = revision;
            return new(_revision, Clone(_data));
        }
    }

    public AdministrationSnapshot Replace(string? expected, HouseholdData data) => Update(expected, candidate =>
    {
        // The desktop compatibility endpoint is intentionally editor-only and guarded by an exact revision.
        candidate.Owners = data.Owners; candidate.Accounts = data.Accounts; candidate.Categories = data.Categories;
        candidate.Transactions = data.Transactions; candidate.RecurringPayments = data.RecurringPayments;
        candidate.TransferMatches = data.TransferMatches;
        candidate.TransactionAllocations = data.TransactionAllocations;
        candidate.ReimbursementClaims = data.ReimbursementClaims;
        candidate.BudgetOverrides = data.BudgetOverrides; candidate.Valuations = data.Valuations;
        candidate.ImportBatches = data.ImportBatches; candidate.ImportProfiles = data.ImportProfiles;
        candidate.Rules = data.Rules; candidate.Mortgage = data.Mortgage;
    });

    private void CheckRevision(string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) throw new MissingRevisionException();
        if (expected != _revision) throw new AdministrationConflictException();
    }

    public byte[] Backup()
    {
        lock (_gate)
        {
            var path = Path.Combine(DirectoryPath, $"download-{Guid.NewGuid():N}.db");
            try { _store.Backup(path); return File.ReadAllBytes(path); }
            finally { File.Delete(path); }
        }
    }

    public string Restore(string? expected, string uploadPath)
    {
        lock (_gate)
        {
            CheckRevision(expected);
            // Validate the source before creating a safety copy or modifying the active administration.
            var restored = SqliteHouseholdStore.ReadBackup(uploadPath);
            var revision = NewRevision();
            var safety = Path.Combine(DirectoryPath, $"voor-herstel-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
            _store.Backup(safety);
            _store.Save(restored);
            _data = restored;
            _revision = revision;
            return _revision;
        }
    }

    public void Dispose() => _lease.Dispose();
}

public sealed class MissingRevisionException : InvalidOperationException
{
    public MissingRevisionException() : base("Een actuele If-Match-revisie is verplicht.") { }
}
