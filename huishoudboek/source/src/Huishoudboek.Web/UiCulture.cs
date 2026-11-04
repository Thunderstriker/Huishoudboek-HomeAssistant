using System.Globalization;
using System.Resources;

namespace Huishoudboek.Web;

public static class UiCulture
{
    private static readonly ResourceManager Resources = new(typeof(UiResources));
    public static string Text(string key, params object[] arguments)
        => string.Format(Culture, Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key, arguments);

    public static string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "nl";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "en" ? "en-GB" : "nl-NL");

    public static string Euro(decimal amount)
    {
        var format = (NumberFormatInfo)Culture.NumberFormat.Clone();
        format.CurrencySymbol = "€";
        return amount.ToString("C2", format);
    }
}
