using Huishoudboek.Core;
using Huishoudboek.Infrastructure;

namespace Huishoudboek.Service;

/// <summary>Bounded, expiring private uploads and server-owned previews. No client-supplied financial rows are trusted.</summary>
public sealed class ImportWorkspace : IDisposable
{
    public const long MaximumFileBytes = 50L * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Upload> _uploads = [];
    private readonly string _directory;
    private readonly TimeProvider _clock;
    private readonly Timer _cleanup;
    private sealed class Upload(string path, string name, DateTimeOffset expires)
    {
        public string Path { get; } = path;
        public string Name { get; } = name;
        public DateTimeOffset Expires { get; } = expires;
        public PreviewRequest? Request { get; set; }
        public IReadOnlyList<ImportRow>? Rows { get; set; }
        public string? Revision { get; set; }
        public Guid PreviewId { get; set; }
        public ImportProfile? Profile { get; set; }
        public bool Ready { get; set; }
    }

    public ImportWorkspace(string directory, TimeProvider? clock = null)
    {
        _directory = Path.Combine(directory, "private-uploads");
        Directory.CreateDirectory(_directory);
        _clock = clock ?? TimeProvider.System;
        // Only this dedicated scratch directory is used; abandoned uploads contain private data and are removed on restart.
        foreach (var path in Directory.EnumerateFiles(_directory, "upload-*"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (Guid.TryParseExact(name[7..], "N", out _) && Path.GetExtension(path) is ".csv" or ".xls" or ".xlsx") File.Delete(path);
        }
        _cleanup = new Timer(_ =>
        {
            try { lock (_gate) Cleanup(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Retry cleanup; never log private filenames. */ }
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private void Cleanup()
    {
        foreach (var id in _uploads.Where(x => x.Value.Ready && x.Value.Expires <= _clock.GetUtcNow()).Select(x => x.Key).ToList()) Remove(id);
    }

    private void Remove(Guid id)
    {
        if (!_uploads.TryGetValue(id, out var item)) return;
        File.Delete(item.Path);
        _uploads.Remove(id);
    }

    public async Task<UploadResponse> UploadAsync(Stream source, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".csv" or ".xls" or ".xlsx")) throw new InvalidDataException("Kies een .csv-, .xls- of .xlsx-bestand.");
        var id = Guid.NewGuid();
        var path = Path.Combine(_directory, $"upload-{id:N}{extension}");
        lock (_gate)
        {
            Cleanup();
            if (_uploads.Count >= 4) throw new InvalidOperationException("Er staan al vier imports open. Rond deze af of wacht maximaal 20 minuten.");
            _uploads.Add(id, new(path, Path.GetFileName(fileName), _clock.GetUtcNow().AddMinutes(20)));
        }
        try
        {
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += count;
                    if (total > MaximumFileBytes) throw new InvalidDataException("Het bestand is groter dan 50 MB.");
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }
            var sheets = await Task.Run(() => ImportService.Worksheets(path), cancellationToken);
            lock (_gate) _uploads[id].Ready = true;
            return new(id, sheets);
        }
        catch { lock (_gate) Remove(id); throw; }
    }

    public PreviewResponse Preview(PreviewRequest request, AdministrationSnapshot snapshot)
    {
        lock (_gate)
        {
            Cleanup();
            if (!_uploads.TryGetValue(request.UploadId, out var upload) || !upload.Ready) throw new InvalidOperationException("De upload is niet gereed of verlopen. Upload het bestand opnieuw.");
            if (!snapshot.Data.Accounts.Any(a => a.Id == request.AccountId)) throw new InvalidOperationException("Kies een bestaande importrekening.");
            var table = ImportService.Read(upload.Path, request.Worksheet, request.Windows1252);
            var profile = request.Profile ?? ImportService.SuggestProfile(table.Headers);
            if (BankImportProfiles.All.Any(p => p == profile)) profile = BankImportProfiles.MatchHeaders(profile, table.Headers);
            var rows = ImportService.Preview(table, profile, request.AccountId, request.From, request.Through, snapshot.Data);
            upload.Request = request; upload.Profile = profile; upload.Rows = rows;
            upload.Revision = snapshot.Revision; upload.PreviewId = Guid.NewGuid();
            return new(upload.PreviewId, snapshot.Revision, profile, table.Headers,
                rows.FirstOrDefault(r => r.CategoryChoices.Count > 0)?.CategoryChoices ?? [], rows.Select(r =>
                    new PreviewRow(r.RowNumber, r.Date, r.Description, r.Amount, r.Transaction?.StatementBalance,
                        r.IsValid, r.Include, r.Note, r.CategoryId, null, r.Kind)).ToList());
        }
    }

    public CommitImportResponse Commit(CommitImportRequest request, string? revision, AdministrationCoordinator coordinator, DateOnly today)
    {
        lock (_gate)
        {
            Cleanup();
            var entry = _uploads.FirstOrDefault(x => x.Value.PreviewId == request.PreviewId && request.PreviewId != Guid.Empty);
            var upload = entry.Value ?? throw new InvalidOperationException("Het importvoorbeeld is verlopen. Verwerk het voorbeeld opnieuw.");
            if (revision != upload.Revision) throw new AdministrationConflictException();
            if (request.Rows.Length == 0 || request.Rows.Select(r => r.RowNumber).Distinct().Count() != request.Rows.Length)
                throw new InvalidOperationException("Selecteer één of meer verschillende geldige regels.");
            if (request.Rows.Any(r => r.CounterAccountId != null))
                throw new InvalidOperationException("Imports boeken alleen op de gekozen rekening; een tegenrekening koppelen is niet toegestaan.");
            var batchId = Guid.NewGuid();
            var snapshot = coordinator.Update(revision, data =>
            {
                var selected = new List<Transaction>();
                // Work on a fresh preview, so a failed confirmation never leaves half-applied row overrides behind.
                var table = ImportService.Read(upload.Path, upload.Request!.Worksheet, upload.Request.Windows1252);
                var rows = ImportService.Preview(table, upload.Profile!, upload.Request.AccountId, upload.Request.From, upload.Request.Through, data);
                foreach (var selection in request.Rows)
                {
                    var row = rows.SingleOrDefault(r => r.RowNumber == selection.RowNumber)
                        ?? throw new InvalidOperationException("Een geselecteerde regel bestaat niet in het voorbeeld.");
                    if (row.Transaction == null) throw new InvalidOperationException("Ongeldige regels kunnen niet worden geïmporteerd.");
                    var categoryId = selection.CategoryId;
                    if (categoryId != null && !data.Categories.Any(c => c.Id == categoryId))
                        throw new InvalidOperationException("Onbekende categorie.");
                    row.CategoryId = categoryId;
                    if (!row.IsValid) throw new InvalidOperationException("Een geselecteerde regel is ongeldig.");
                    var transaction = row.Transaction;
                    transaction.ImportBatchId = batchId;
                    transaction.RecurringPaymentId = null;
                    transaction.RecurringOccurrenceDate = null;
                    Finance.ApplySuggestions(data, transaction, suggestCategory: false);
                    data.Transactions.Add(transaction);
                    selected.Add(transaction);
                }
                data.ImportBatches.Add(new ImportBatch { Id = batchId, AccountId = upload.Request.AccountId, FileName = upload.Name, RowCount = selected.Count });
                var index = data.ImportProfiles.FindIndex(p => p.Id == upload.Profile!.Id);
                if (index >= 0) data.ImportProfiles[index] = upload.Profile!; else data.ImportProfiles.Add(upload.Profile!);
            });
            var observations = upload.Rows!.Select(r => new StatementBalanceObservation(r.RowNumber, r.Date, r.Amount, r.Transaction?.StatementBalance)).ToList();
            var check = ImportBalance.Check(snapshot.Data, upload.Request!.AccountId, observations, today);
            Remove(entry.Key);
            return new(batchId, request.Rows.Length, snapshot.Revision, check);
        }
    }

    public PreviewRow Adjust(AdjustImportRequest request)
    {
        if (request.CounterAccountId != null)
            throw new InvalidOperationException("Imports boeken alleen op de gekozen rekening; een tegenrekening koppelen is niet toegestaan.");
        lock (_gate)
        {
            Cleanup();
            var upload = _uploads.Values.FirstOrDefault(u => u.PreviewId == request.PreviewId && request.PreviewId != Guid.Empty)
                ?? throw new InvalidOperationException("Het importvoorbeeld is verlopen. Verwerk het opnieuw.");
            var row = upload.Rows!.SingleOrDefault(r => r.RowNumber == request.RowNumber)
                ?? throw new InvalidOperationException("Onbekende voorbeeldregel.");
            if (request.SetCategory) row.CategoryId = request.CategoryId;
            return new(row.RowNumber, row.Date, row.Description, row.Amount, row.Transaction?.StatementBalance,
                row.IsValid, row.Include, row.Note, row.CategoryId, null, row.Kind);
        }
    }

    public void Dispose()
    {
        _cleanup.Dispose();
        lock (_gate) foreach (var id in _uploads.Keys.ToList()) Remove(id);
    }
}
