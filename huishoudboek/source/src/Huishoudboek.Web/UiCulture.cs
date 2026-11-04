using System.Globalization;

namespace Huishoudboek.Web;

public static class UiCulture
{
    public static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "nl";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "en" ? "en-GB" : "nl-NL");

    public static string Euro(decimal amount)
    {
        var format = (NumberFormatInfo)Culture.NumberFormat.Clone();
        format.CurrencySymbol = "€";
        return amount.ToString("C2", format);
    }
}
