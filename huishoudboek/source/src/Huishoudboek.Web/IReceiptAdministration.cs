using Huishoudboek.Core;

namespace Huishoudboek.Web;

public enum ReceiptRecognitionState { Recognized, NoText, Unavailable, Failed, TimedOut, Busy }
public sealed record ReceiptScan(Guid UploadId, string FileName, string ContentType, string Text, ParsedReceipt Parsed, ReceiptRecognitionState State);

public interface IReceiptAdministration
{
    Task<ReceiptScan> RecognizeAsync(Stream source, string name, CancellationToken cancellationToken);
    Task SaveAsync(string revision, PurchaseReceipt receipt, IReadOnlyList<PurchaseReceiptItem> items,
        bool keepDocument, Guid? uploadId, CancellationToken cancellationToken);
    void DiscardUpload(Guid uploadId);
}
