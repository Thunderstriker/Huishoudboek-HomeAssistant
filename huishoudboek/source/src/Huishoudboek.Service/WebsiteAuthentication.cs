using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace Huishoudboek.Service;

public sealed class WebsiteOptions
{
    public bool Enabled { get; init; }
    public string Username { get; init; } = "huishouden";
    public string PasswordHash { get; init; } = "";
    public string SessionVersion { get; init; } = "1";
    public bool IngressEnabled { get; internal set; }
    public bool PreviewEnabled { get; internal set; }
    public void Validate()
    {
        if (!Enabled || IngressEnabled || PreviewEnabled) return;
        if (string.IsNullOrWhiteSpace(Username) || PasswordHash.Length < 50 || string.IsNullOrWhiteSpace(SessionVersion))
            throw new InvalidOperationException("Stel Website:Username, PasswordHash en SessionVersion in. Maak de hash met --hash-password.");
        try { _ = Convert.FromBase64String(PasswordHash); }
        catch (FormatException) { throw new InvalidOperationException("Website:PasswordHash is geen geldige wachtwoordhash."); }
    }
    public string Stamp => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Username + "\n" + PasswordHash + "\n" + SessionVersion)));
}

/// <summary>Separate from the financial schema. One small, revocable record per browser session.</summary>
public sealed class WebsiteSessions
{
    public const string Scheme = "Website";
    private readonly WebsiteOptions _options;
    private readonly string _directory;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private sealed record Session(DateTimeOffset Expires, string Stamp);
    public WebsiteSessions(WebsiteOptions options, ServiceOptions service, TimeProvider? clock = null)
    {
        _options = options; _clock = clock ?? TimeProvider.System;
        _directory = Path.Combine(service.DataDirectory, "website-auth", "sessions");
        if (options.Enabled && !options.IngressEnabled && !options.PreviewEnabled) Directory.CreateDirectory(_directory);
    }
    public ClaimsPrincipal Create(bool remember)
    {
        if (!_options.Enabled || _options.IngressEnabled || _options.PreviewEnabled) throw new UnauthorizedAccessException();
        var id = Guid.NewGuid().ToString("N");
        var expires = _clock.GetUtcNow().Add(remember ? TimeSpan.FromDays(90) : TimeSpan.FromHours(12));
        lock (_gate)
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _)) continue;
                var previous = Read(file);
                if (previous == null || previous.Expires <= _clock.GetUtcNow()) File.Delete(file);
            }
            File.WriteAllText(Path.Combine(_directory, id + ".json"), JsonSerializer.Serialize(new Session(expires, _options.Stamp)));
        }
        return new(new ClaimsIdentity([new(ClaimTypes.Name, _options.Username), new("sid", id), new("stamp", _options.Stamp),
            new("expires", expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))], Scheme));
    }
    private static Session? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Session>(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    public bool Valid(ClaimsPrincipal user)
    {
        if (_options.PreviewEnabled) return _options.Enabled && LocalPreviewAuthentication.Valid(user);
        if (_options.IngressEnabled) return _options.Enabled && HomeAssistantIngress.Valid(user, _clock.GetUtcNow());
        if (!_options.Enabled || user.Identity?.IsAuthenticated != true || user.FindFirst("stamp")?.Value != _options.Stamp ||
            !Guid.TryParseExact(user.FindFirst("sid")?.Value, "N", out var id) ||
            !long.TryParse(user.FindFirst("expires")?.Value, CultureInfo.InvariantCulture, out var seconds) || seconds <= _clock.GetUtcNow().ToUnixTimeSeconds()) return false;
        lock (_gate)
        {
            var session = Read(Path.Combine(_directory, id.ToString("N") + ".json"));
            return session != null && session.Stamp == _options.Stamp && session.Expires > _clock.GetUtcNow();
        }
    }
    public void Revoke(ClaimsPrincipal user, bool all = false)
    {
        lock (_gate)
        {
            if (all)
            {
                foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
                    if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _)) File.Delete(file);
            }
            else if (Guid.TryParseExact(user.FindFirst("sid")?.Value, "N", out var id)) File.Delete(Path.Combine(_directory, id.ToString("N") + ".json"));
        }
    }
}

public sealed class WebsiteAuthenticationState(ILoggerFactory loggerFactory, WebsiteSessions sessions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);
    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
        => Task.FromResult(sessions.Valid(authenticationState.User));
}

public static class WebsiteAuthentication
{
    public static void Register(WebApplicationBuilder builder, WebsiteOptions options, ServiceOptions service)
    {
        options.Validate();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<WebsiteSessions>();
        var protection = builder.Services.AddDataProtection().SetApplicationName("Huishoudboek.Website.v1");
        if (options.Enabled && !options.IngressEnabled)
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(service.DataDirectory, "website-auth", "keys")));
            if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }
        if (options.PreviewEnabled)
            builder.Services.AddAuthentication(LocalPreviewAuthentication.SchemeName)
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, LocalPreviewAuthentication>(LocalPreviewAuthentication.SchemeName, _ => { });
        else if (options.IngressEnabled)
            builder.Services.AddAuthentication(HomeAssistantIngress.Scheme)
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, HomeAssistantAuthentication>(HomeAssistantIngress.Scheme, _ => { });
        else builder.Services.AddAuthentication(WebsiteSessions.Scheme).AddCookie(WebsiteSessions.Scheme, o =>
        {
            o.LoginPath = "/login"; o.AccessDeniedPath = "/login";
            o.Cookie.Name = "Huishoudboek.Session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
            // HTTP is rejected outside loopback before cookie issuance.
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromDays(90); o.SlidingExpiration = false;
            o.Events.OnValidatePrincipal = async c =>
            {
                if (!c.HttpContext.RequestServices.GetRequiredService<WebsiteSessions>().Valid(c.Principal!))
                { c.RejectPrincipal(); await c.HttpContext.SignOutAsync(WebsiteSessions.Scheme); }
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<AuthenticationStateProvider, WebsiteAuthenticationState>();
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync("Te veel aanmeldpogingen. Wacht één minuut en probeer opnieuw.", cancellationToken);
            };
            o.AddPolicy("website-login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Path == "/login" && HttpMethods.IsPost(context.Request.Method)
                    ? RateLimitPartition.GetFixedWindowLimiter("login", _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("other"));
        });
    }

    public static void Map(WebApplication app, WebsiteOptions options)
    {
        if (options.PreviewEnabled)
        {
            app.MapGet("/login", () => Results.Redirect("/"));
            app.MapGet("/signout", () => Results.Redirect("/"));
            return;
        }
        if (options.IngressEnabled)
        {
            app.MapGet("/login", (HttpContext context) => Results.Redirect(context.Request.PathBase + "/"));
            app.MapGet("/signout", () => Results.Content("U gebruikt de aanmelding van Home Assistant. Meld daar af en sluit dit tabblad.", "text/plain; charset=utf-8")).RequireAuthorization();
            return;
        }
        app.MapGet("/login", (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!options.Enabled) return Results.Content("De website is niet ingesteld. Stel Website:Enabled, Username en PasswordHash in. Zie SERVICE.md.", "text/plain; charset=utf-8", statusCode: 503);
            var tokens = antiforgery.GetAndStoreTokens(context);
            var error = context.Request.Query.ContainsKey("error") ? "<p role=\"alert\" class=\"error\">Gebruikersnaam of wachtwoord onjuist.</p>" : "";
            var e = HtmlEncoder.Default;
            return Results.Content($$"""
                <!doctype html><html lang="nl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Aanmelden · Huishoudboek</title><link rel="stylesheet" href="/_content/Huishoudboek.Web/site.css"></head>
                <body class="login-body"><main class="login-card"><h1>huishoudboek</h1><p>Rust in uw financiën · lokaal · EUR</p><h2>Aanmelden</h2>{{error}}
                <form method="post" action="/login"><input type="hidden" name="{{e.Encode(tokens.FormFieldName)}}" value="{{e.Encode(tokens.RequestToken!)}}">
                <label>Gebruikersnaam<input name="username" autocomplete="username" required maxlength="200" autofocus></label>
                <label>Wachtwoord<input name="password" type="password" autocomplete="current-password" required maxlength="1024"></label>
                <label class="check"><input type="checkbox" name="remember" value="true">Dit apparaat 90 dagen onthouden</label><p class="muted">Gebruik dit alleen op een eigen, beveiligd apparaat. Zonder vinkje eindigt de sessie uiterlijk na 12 uur.</p>
                <button class="primary" type="submit">Aanmelden</button></form></main></body></html>
                """, "text/html; charset=utf-8");
        });
        app.MapPost("/login", async (HttpContext context, IAntiforgery antiforgery, WebsiteSessions sessions) =>
        {
            if (!options.Enabled) return Results.StatusCode(503);
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest("De aanmeldpagina is verlopen. Open deze opnieuw."); }
            var form = await context.Request.ReadFormAsync();
            var username = form["username"].ToString(); var password = form["password"].ToString();
            var valid = false;
            if (password.Length is > 0 and <= 1024 && username.Length <= 200)
            {
                try { valid = new PasswordHasher<string>().VerifyHashedPassword(options.Username, options.PasswordHash, password) != PasswordVerificationResult.Failed; }
                catch (FormatException) { }
            }
            if (!valid || username != options.Username) return Results.Redirect("/login?error=1");
            var principal = sessions.Create(form["remember"] == "true");
            await context.SignInAsync(WebsiteSessions.Scheme, principal, new AuthenticationProperties
            {
                IsPersistent = form["remember"] == "true", AllowRefresh = false,
                ExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(long.Parse(principal.FindFirst("expires")!.Value, CultureInfo.InvariantCulture))
            });
            return Results.Redirect("/");
        }).RequireRateLimiting("website-login");
        app.MapGet("/signout", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context); var e = HtmlEncoder.Default;
            return Results.Content($$"""
                <!doctype html><html lang="nl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Afmelden · Huishoudboek</title><link rel="stylesheet" href="/_content/Huishoudboek.Web/site.css"></head><body class="login-body"><main class="login-card"><h1>Afmelden</h1>
                <form method="post" action="/logout"><input type="hidden" name="{{e.Encode(tokens.FormFieldName)}}" value="{{e.Encode(tokens.RequestToken!)}}"><p>Afmelden op dit apparaat trekt de huidige sessie direct in.</p>
                <label class="check"><input type="checkbox" name="all" value="true">Ook alle andere apparaten afmelden</label><p class="muted">Andere open schermen sluiten binnen één minuut; nieuwe lees- en schrijfhandelingen worden direct geweigerd.</p>
                <button type="submit" class="primary">Afmelden</button><a href="/">Annuleren</a></form></main></body></html>
                """, "text/html; charset=utf-8");
        }).RequireAuthorization();
        app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery, WebsiteSessions sessions) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest("De pagina is verlopen. Vernieuw de pagina."); }
            var form = await context.Request.ReadFormAsync();
            sessions.Revoke(context.User, form["all"] == "true");
            await context.SignOutAsync(WebsiteSessions.Scheme);
            return Results.Redirect("/login");
        }).RequireAuthorization();
    }
}
