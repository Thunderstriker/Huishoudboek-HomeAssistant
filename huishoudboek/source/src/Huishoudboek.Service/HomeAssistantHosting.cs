using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Huishoudboek.Service;

public sealed class HomeAssistantOptions
{
    public bool Enabled { get; init; }
    // Override only for an explicitly trusted test proxy. Never trust a request header for this address.
    public string TrustedProxyAddress { get; init; } = "172.30.32.2";
    public IPAddress ProxyAddress => IPAddress.Parse(TrustedProxyAddress);
    public void Validate()
    {
        if (Enabled && (!IPAddress.TryParse(TrustedProxyAddress, out var address) ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)))
            throw new InvalidOperationException("Stel één expliciet vertrouwd Home Assistant-proxyadres in.");
    }
}

/// <summary>Separate, opt-in hosting mode. The normal Windows service retains its existing security policy.</summary>
public static partial class HomeAssistantIngress
{
    public const string Scheme = "HomeAssistantIngress";
    private static readonly object TrustedRequest = new();
    [GeneratedRegex("^/api/hassio_ingress/[A-Za-z0-9_-]{1,256}/?$", RegexOptions.CultureInvariant)]
    private static partial Regex IngressPath();

    public static async Task PrepareAsync(HttpContext context, HomeAssistantOptions options, RequestDelegate next)
    {
        if (!Equals(context.Connection.RemoteIpAddress?.MapToIPv6(), options.ProxyAddress.MapToIPv6()))
        { context.Response.StatusCode = 403; return; }
        // Watchdog exposes no financial information, and is still restricted to the Supervisor.
        if (context.Request.Path == "/health" && HttpMethods.IsGet(context.Request.Method))
        { await next(context); return; }
        var path = context.Request.Headers["X-Ingress-Path"].ToString();
        var user = context.Request.Headers["X-Remote-User-Id"].ToString();
        if (!IngressPath().IsMatch(path) || string.IsNullOrWhiteSpace(user) || user.Length > 256 || user.Any(char.IsControl))
        { context.Response.StatusCode = 403; return; }
        // The Supervisor strips this prefix before proxying. Do not remove it from Request.Path again.
        context.Request.PathBase = path.TrimEnd('/');
        context.Items[TrustedRequest] = true;
        await next(context);
    }

    public static ClaimsPrincipal? Principal(HttpContext context)
    {
        if (!context.Items.ContainsKey(TrustedRequest)) return null;
        return new(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, context.Request.Headers["X-Remote-User-Id"].ToString()),
            new(ClaimTypes.Name, "Home Assistant"),
            new("expires", DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
        ], Scheme));
    }

    public static bool Valid(ClaimsPrincipal user, DateTimeOffset now) =>
        user.Identity is { IsAuthenticated: true, AuthenticationType: Scheme } &&
        !string.IsNullOrWhiteSpace(user.FindFirst(ClaimTypes.NameIdentifier)?.Value) &&
        long.TryParse(user.FindFirst("expires")?.Value, CultureInfo.InvariantCulture, out var expires) &&
        expires > now.ToUnixTimeSeconds();
}

public sealed class HomeAssistantAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = HomeAssistantIngress.Principal(Context);
        return Task.FromResult(principal == null ? AuthenticateResult.NoResult() :
            AuthenticateResult.Success(new AuthenticationTicket(principal, HomeAssistantIngress.Scheme)));
    }
}

public static class HomeAssistantBootstrap
{
    public static string ReadTimeZone(string root)
    {
        var path = Path.Combine(root, "options.json");
        var zone = "Europe/Amsterdam";
        if (File.Exists(path))
        {
            using var options = JsonDocument.Parse(File.ReadAllText(path));
            if (options.RootElement.TryGetProperty("time_zone", out var configured)) zone = configured.GetString() ?? "";
        }
        if (zone.Length is 0 or > 128) throw new InvalidDataException("Kies een geldige tijdzone, bijvoorbeeld Europe/Amsterdam.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new InvalidDataException("De ingestelde tijdzone wordt niet ondersteund. Gebruik bijvoorbeeld Europe/Amsterdam."); }
        return zone;
    }

    public static string Prepare(string root)
    {
        root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        var configuration = Path.Combine(root, "home-assistant.json");
        var directory = Path.Combine(root, "administration");
        if (File.Exists(configuration))
        {
            // A missing active database must never look like a fresh installation.
            if (!File.Exists(Path.Combine(directory, "huishoudboek.db")) ||
                !File.Exists(Path.Combine(directory, Huishoudboek.Infrastructure.AdministrationLease.ServiceMarker)))
                throw new InvalidDataException("De bestaande Home Assistant-administratie is onvolledig. Herstel een back-up; er is niets opnieuw aangemaakt.");
            return configuration;
        }
        if (Directory.EnumerateFileSystemEntries(root).Any(p => Path.GetFileName(p) != "options.json"))
            throw new InvalidDataException("De Home Assistant-gegevensmap is niet leeg maar heeft geen geldige configuratie. Bestaande bestanden worden niet overgenomen of gewist.");

        // Only a genuinely new installation receives an empty administration, never fictional finances.
        using (var administration = new AdministrationCoordinator(directory, initialize: true)) { }
        var settings = new
        {
            Urls = "http://0.0.0.0:8099", AllowedHosts = "*",
            Huishoudboek = new { DataDirectory = directory, EditorToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ReaderToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) },
            Website = new { Enabled = true }, HomeAssistant = new { Enabled = true }
        };
        using (var file = new FileStream(configuration, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(file, settings, new JsonSerializerOptions { WriteIndented = true });
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(configuration, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return configuration;
    }
}
