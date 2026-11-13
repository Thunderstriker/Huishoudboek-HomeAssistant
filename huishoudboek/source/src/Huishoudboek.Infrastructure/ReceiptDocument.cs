using System.Buffers.Binary;
using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

public sealed record ReceiptDocument(Guid ReceiptId, string Name, string ContentType, byte[] Content);

public static class ReceiptDocuments
{
    public static ReceiptDocument Create(Guid receiptId, string name, byte[] content)
    {
        if (content.LongLength is <= 0 or > PurchaseReceipts.MaximumDocumentBytes)
            throw new InvalidDataException("Kies een bonbestand van maximaal 8 MB.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        string type;
        if (extension == ".pdf" && content.AsSpan().StartsWith("%PDF-"u8)) type = "application/pdf";
        else if (extension == ".png" && content.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && content.Length >= 24)
        {
            CheckDimensions(BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(16, 4)), BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(20, 4)));
            type = "image/png";
        }
        else if (extension is ".jpg" or ".jpeg" && content.Length >= 4 && content[0] == 255 && content[1] == 216)
        {
            var found = false;
            for (var index = 2; index + 8 < content.Length;)
            {
                if (content[index++] != 255) continue;
                var marker = content[index++];
                if (marker is 0 or 255 or 216 or 217 || marker is >= 208 and <= 215) continue;
                if (index + 2 > content.Length) break;
                var length = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(index, 2));
                if (length < 2 || index + length > content.Length) break;
                if (marker is 192 or 193 or 194 or 195 or 197 or 198 or 199 or 201 or 202 or 203 or 205 or 206 or 207)
                {
                    CheckDimensions(BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(index + 5, 2)), BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(index + 3, 2)));
                    found = true; break;
                }
                index += length;
            }
            if (!found) throw new InvalidDataException("De JPEG-afbeelding is niet leesbaar.");
            type = "image/jpeg";
        }
        else throw new InvalidDataException("Kies een echte JPEG-, PNG- of PDF-bon; bestandstype en inhoud moeten overeenkomen.");
        var safeName = new string(name.Replace('\\', '/').Split('/').Last().Where(c => !char.IsControl(c) && c is not '"' and not '<' and not '>').ToArray());
        if (safeName.Length > 180) safeName = safeName[..160] + extension;
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "bon" + extension;
        return new(receiptId, safeName, type, content);
    }

    private static void CheckDimensions(uint width, uint height)
    {
        if (width is 0 or > 20000 || height is 0 or > 20000 || (ulong)width * height > 30_000_000)
            throw new InvalidDataException("De bonafbeelding is te groot: maximaal 30 megapixels en 20.000 pixels per zijde.");
    }
}
