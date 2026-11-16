using System.Globalization;

namespace Huishoudboek.Web;

/// <summary>Keeps decimal input exactly as typed, including an unfinished sign or decimal separator.</summary>
public sealed class ReceiptNumberDraft
{
    private readonly int decimalPlaces;
    private readonly decimal minimum, maximum;
    private readonly bool emptyAsZero;
    private string text = "";

    public ReceiptNumberDraft(decimal? value, CultureInfo culture, int decimalPlaces = 2,
        decimal minimum = -1_000_000_000m, decimal maximum = 1_000_000_000m, bool emptyAsZero = false)
    {
        this.decimalPlaces = decimalPlaces; this.minimum = minimum; this.maximum = maximum; this.emptyAsZero = emptyAsZero;
        Text = value?.ToString(culture) ?? "";
    }

    public string Text
    {
        get => text;
        set
        {
            text = value;
            IsValid = TryGetValue(out var parsed);
            Value = IsValid ? parsed : null;
        }
    }
    public bool IsValid { get; private set; }
    public decimal? Value { get; private set; }

    private bool TryGetValue(out decimal? parsed)
    {
        parsed = null;
        var text = Text.Trim();
        if (text.Length == 0)
        {
            parsed = emptyAsZero ? 0m : null;
            return true;
        }
        // Accept either decimal separator, never interpret grouping, exponents or currency symbols.
        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number) || number < minimum || number > maximum ||
            decimal.Round(number, decimalPlaces) != number) return false;
        parsed = number;
        return true;
    }
}
