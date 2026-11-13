using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;
using Microsoft.AspNetCore.Components.Authorization;

namespace Huishoudboek.Service;

/// <summary>Private to one authenticated Blazor circuit. At most one bounded upload is held in memory.</summary>
public sealed class ReceiptAdministration(AdministrationCoordinator coordinator, ReceiptRecognition recognition,
    AuthenticationStateProvider authentication, WebsiteSessions sessions) : IReceiptAdministration, IDisposable
{
    private sealed record Upload(Guid Id, Guid AdministrationId, DateTimeOffset Expires, ReceiptDocument Document);
    private Upload? _upload;
    private Timer? _expiry;
    private bool _disposed;

    private async Task AuthorizeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!sessions.Valid((await authentication.GetAuthenticationStateAsync()).User)) throw new UnauthorizedAccessException();
        if (_upload?.Expires < DateTimeOffset.UtcNow) ClearUpload();
    }

    public async Task<ReceiptScan> RecognizeAsync(Stream source, string name, CancellationToken cancellationToken)
    {
        await AuthorizeAsync();
        ClearUpload();
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + count > PurchaseReceipts.MaximumDocumentBytes) throw new InvalidDataException("Kies een bonbestand van maximaal 8 MB.");
            await memory.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        var document = ReceiptDocuments.Create(Guid.Empty, name, memory.ToArray());
        var result = await recognition.ReadAsync(document, cancellationToken);
        await AuthorizeAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var parsed = ReceiptTextParser.Parse(result.Text);
        var upload = new Upload(Guid.NewGuid(), coordinator.AdministrationId, DateTimeOffset.UtcNow.AddMinutes(15), document);
        _upload = upload;
        _expiry = new Timer(static state =>
        {
            var (owner, id) = ((ReceiptAdministration Owner, Guid Id))state!;
            var current = owner._upload;
            if (current?.Id == id) Interlocked.CompareExchange(ref owner._upload, null, current);
        }, (this, upload.Id), TimeSpan.FromMinutes(15), Timeout.InfiniteTimeSpan);
        return new(upload.Id, document.Name, document.ContentType, result.Text, parsed, result.State);
    }

    public async Task SaveAsync(string revision, PurchaseReceipt receipt, IReadOnlyList<PurchaseReceiptItem> items,
        bool keepDocument, Guid? uploadId, CancellationToken cancellationToken)
    {
        await AuthorizeAsync();
        ReceiptDocument? document = null;
        if (keepDocument && uploadId is { } id)
        {
            if (_upload is not { } upload || upload.Id != id || upload.AdministrationId != coordinator.AdministrationId || upload.Expires < DateTimeOffset.UtcNow)
                throw new InvalidOperationException("De tijdelijke bonupload is verlopen. Upload opnieuw; de ingevulde regels blijven staan.");
            document = upload.Document with { ReceiptId = receipt.Id };
        }
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => coordinator.SaveReceipt(revision, receipt, items, keepDocument, document), cancellationToken);
        ClearUpload();
    }

    private void ClearUpload() { _expiry?.Dispose(); _expiry = null; _upload = null; }
    public void DiscardUpload(Guid uploadId) { if (_upload?.Id == uploadId) ClearUpload(); }
    public void Dispose() { _disposed = true; ClearUpload(); }
}
