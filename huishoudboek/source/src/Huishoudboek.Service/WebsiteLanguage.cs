using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Localization;

namespace Huishoudboek.Service;

public static class WebsiteLanguage
{
    public static void Register(IServiceCollection services)
    {
        services.AddLocalization();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture("nl-NL").AddSupportedCultures("nl-NL", "en-GB").AddSupportedUICultures("nl-NL", "en-GB");
            // An explicit browser preference only; browser headers must not change the Dutch default.
            options.RequestCultureProviders = [new CookieRequestCultureProvider()];
        });
    }

    public static void Map(WebApplication app)
    {
        app.MapPost("/website/language", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            var form = await context.Request.ReadFormAsync();
            var culture = form["language"].ToString() switch { "nl" => "nl-NL", "en" => "en-GB", _ => null };
            if (culture == null) return Results.BadRequest();
            // Return paths are relative to the app, including when Supervisor supplies a PathBase.
            var path = form["returnPath"].ToString();
            if (!IsSafeReturnPath(path)) return Results.BadRequest();
            context.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)), new CookieOptions
                {
                    Path = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "/",
                    Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true,
                    SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps, IsEssential = true
                });
            return Results.LocalRedirect(context.Request.PathBase + "/" + path);
        }).RequireAuthorization();
    }

    public static bool IsSafeReturnPath(string path) => path.Length <= 2048 &&
        !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':') &&
        !path.Any(char.IsControl) && !path.Contains('%');
}
