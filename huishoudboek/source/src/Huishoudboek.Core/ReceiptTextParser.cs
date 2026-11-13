using System.Globalization;
using System.Text.RegularExpressions;

namespace Huishoudboek.Core;

public sealed record ParsedReceipt(PurchaseReceipt Receipt, IReadOnlyList<PurchaseReceiptItem> Items);

/// <summary>Conservative, local suggestions. Original item text survives edits; totals are never invented.</summary>
public static class ReceiptTextParser
{
    public const int MaximumTextLength = 128_000;
    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    private static readonly Regex Price = Pattern(@"(?<![\d.,])(?<amount>-?\s*(?:\d{1,3}(?:[., ]\d{3})+|\d+)[,.]\d{2})\s*(?<minus>-)?\s*(?:€|EUR)?\s*[A-D*]?\s*$");
    private static readonly Regex Total = Pattern(@"^(?:grand\s+total|totaal(?:bedrag|\s+te\s+betalen)?|te\s+betalen|eindtotaal|total(?:\s+due)?|amount\s+due)\b");
    private static readonly Regex Ignore = Pattern(@"^(?:sub\s*tota[al]+|sub\s*total|btw\b|vat\b|tax\b|basis\b|maatstaf\b|netto\b|bruto\b|totaal\s+(?:korting|voordeel|bespaar|artikelen|aantal)|total\s+(?:discount|saving|items)|u\s+bespaart|bespaard|voordeel\s+totaal|betaald\b|betaling\b|payment\b|pin\b|pinnen\b|bancontact\b|maestro\b|mastercard\b|visa\b|kaart\b|card\b|cash\b|contant\b|wisselgeld\b|change\b|terug\b|saldo\b|kassa\b|kassier\b|terminal\b|klant\b|klantenkaart\b|loyalty\b|tel\b|telefoon\b|www\.|https?://|bonnummer\b|bon\s*(?:nr|nummer)|factuur\b|invoice\b|datum\b|date\b|tijd\b|time\b|omschrijving\b|description\b|artikel\s+(?:prijs|aantal)|aantal\s+artikelen)");
    private static readonly Regex Discount = Pattern(@"\b(?:korting|bonus|promo|discount|coupon|waardebon|actievoordeel)\b");
    private static readonly Regex Deposit = Pattern(@"\b(?:statiegeld|leeggoed|deposit|pfand)\b");
    private static readonly Regex Quantity = Pattern(@"^(?<quantity>\d+(?:[,.]\d{1,4})?)\s*(?<unit>kg|g|l|ml|st|stuk|pcs)?\s*(?:x|×|\*)\s*(?:€\s*)?(?<price>\d+(?:[,.]\d{1,4})?)(?:\s*(?:€|EUR|/\s*(?:kg|g|l|ml|st)))?\s*$");
    private static readonly Regex DatePattern = Pattern(@"\b(?<day>\d{1,2})[-/.](?<month>\d{1,2})[-/.](?<year>\d{4}|\d{2})\b");
    private static readonly Regex IsoDate = Pattern(@"\b(?<year>20\d{2}|19\d{2})-(?<month>\d{2})-(?<day>\d{2})\b");
    private static readonly Regex Invoice = Pattern(@"(?:factuur(?:nummer|nr)?|invoice(?:\s*(?:number|no))?|bonnummer|bon\s*(?:nr|nummer))\s*[:.#-]?\s*(?<number>[\w/-]{2,100})");

    public static ParsedReceipt Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumTextLength) throw new InvalidDataException("De herkende bontekst is te lang (maximaal 128.000 tekens).");
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        if (lines.Length > 2000) throw new InvalidDataException("De bon bevat te veel tekstregels.");
        var receipt = new PurchaseReceipt();
        var items = new List<PurchaseReceiptItem>();
        string? pending = null;
        var afterTotal = false;
        foreach (var original in lines)
        {
            var line = original.Replace('\u00a0', ' ');
            if (receipt.PurchaseDate == null)
            {
                var match = IsoDate.Match(line);
                if (!match.Success) match = DatePattern.Match(line);
                if (match.Success && int.TryParse(match.Groups["year"].Value, out var year) &&
                    int.TryParse(match.Groups["month"].Value, out var month) && int.TryParse(match.Groups["day"].Value, out var day))
                {
                    if (year < 100) year += year < 80 ? 2000 : 1900;
                    if (year is >= 1900 and <= 2200 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                        receipt.PurchaseDate = new(year, month, day);
                }
            }
            if (receipt.InvoiceNumber.Length == 0 && Invoice.Match(line) is { Success: true } invoice)
                receipt.InvoiceNumber = invoice.Groups["number"].Value;
            var price = Price.Match(line);
            if (Total.IsMatch(line) && !Ignore.IsMatch(line) && price.Success && TryNumber(price.Groups["amount"].Value, out var total) && total >= 0)
            {
                receipt.Total = total;
                afterTotal = true;
                pending = null;
                continue;
            }
            if (afterTotal || Ignore.IsMatch(line) || line.All(c => !char.IsLetterOrDigit(c))) { pending = null; continue; }
            if (Quantity.Match(line) is { Success: true } quantityOnly)
            {
                if (items.LastOrDefault() is { Kind: ReceiptLineKind.Product } previous && pending == null)
                {
                    ReadQuantity(previous, quantityOnly);
                    previous.OriginalDescription = Limit(previous.OriginalDescription + "\n" + original, 2000);
                }
                else if (pending != null)
                {
                    // Quantity × unit price is not proof of the final paid line amount (promotions/rounding).
                    var item = new PurchaseReceiptItem { ReceiptId = receipt.Id, Position = items.Count, Name = Limit(pending, 300),
                        OriginalDescription = Limit(pending + "\n" + original, 2000) };
                    ReadQuantity(item, quantityOnly);
                    items.Add(item);
                    if (items.Count > PurchaseReceipts.MaximumItems) throw new InvalidDataException("De bon bevat meer dan 500 mogelijke productregels.");
                }
                pending = null;
                continue;
            }
            if (!price.Success)
            {
                if (receipt.Merchant.Length == 0 && line.Any(char.IsLetter) && !line.Any(char.IsDigit)) receipt.Merchant = Limit(line, 200);
                pending = line.Any(char.IsLetter) && line.Length <= 300 ? line : null;
                continue;
            }
            var description = line[..price.Index].Trim(' ', '\t', '€', ':');
            var quantity = Quantity.Match(description);
            if (quantity.Success && pending != null) description = pending;
            if (description.Length == 0 || !description.Any(char.IsLetter) || quantity.Success && pending == null)
            { pending = null; continue; }
            if (!TryNumber(price.Groups["amount"].Value, out var amount)) { pending = null; continue; }
            if (price.Groups["minus"].Success) amount = -Math.Abs(amount);
            var kind = Discount.IsMatch(description) ? ReceiptLineKind.Discount : Deposit.IsMatch(description) ? ReceiptLineKind.Deposit : ReceiptLineKind.Product;
            if (kind == ReceiptLineKind.Discount) amount = -Math.Abs(amount);
            var result = new PurchaseReceiptItem { ReceiptId = receipt.Id, Position = items.Count, Name = Limit(description, 300),
                OriginalDescription = Limit(quantity.Success ? pending + "\n" + original : original, 2000), Amount = amount, Kind = kind };
            if (quantity.Success) ReadQuantity(result, quantity);
            items.Add(result);
            pending = null;
            if (items.Count > PurchaseReceipts.MaximumItems) throw new InvalidDataException("De bon bevat meer dan 500 mogelijke productregels.");
        }
        return new(receipt, items);
    }

    private static void ReadQuantity(PurchaseReceiptItem item, Match match)
    {
        if (TryNumber(match.Groups["quantity"].Value, out var quantity) && quantity is > 0 and <= 1_000_000) item.Quantity = quantity;
        if (TryNumber(match.Groups["price"].Value, out var price) && price is >= 0 and <= 1_000_000_000) item.UnitPrice = price;
        item.Unit = match.Groups["unit"].Value;
    }

    private static bool TryNumber(string text, out decimal value)
    {
        var normalized = text.Replace(" ", "");
        var separator = Math.Max(normalized.LastIndexOf(','), normalized.LastIndexOf('.'));
        if (separator >= 0) normalized = normalized[..separator].Replace(".", "").Replace(",", "") + "." + normalized[(separator + 1)..];
        return decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) &&
            value is >= -1_000_000_000 and <= 1_000_000_000;
    }

    private static string Limit(string text, int length) => text.Length <= length ? text : text[..length];
}
