using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

public static class EnergyPdf
{
    // The supplier's downloads can start with a UTF-8 BOM. Keep the original bytes if requested.
    public static bool HasSignature(ReadOnlySpan<byte> content) => content[HeaderOffset(content)..].StartsWith("%PDF-"u8);
    private static int HeaderOffset(ReadOnlySpan<byte> content) => content.StartsWith("\ufeff"u8) ? 3 : 0;
    public static byte[] ForExtraction(byte[] content) => HeaderOffset(content) == 0 ? content : content[3..];

    public static EnergyTariffDocument Create(Guid tariffId, string name, byte[] content)
    {
        if (content.Length > PurchaseReceipts.MaximumDocumentBytes || !HasSignature(content))
            throw new InvalidDataException("Kies een geldige PDF-tariefkaart van maximaal 8 MB.");
        var checkedDocument = ReceiptDocuments.Create(tariffId, name, ForExtraction(content));
        return new(tariffId, checkedDocument.Name, content);
    }
}
