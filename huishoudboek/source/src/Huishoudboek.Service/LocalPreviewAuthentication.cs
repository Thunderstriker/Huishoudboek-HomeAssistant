using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Huishoudboek.Service;

/// <summary>No login UI, exclusively for the explicit --preview process and loopback requests.</summary>
public sealed class LocalPreviewAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "LocalPreview";

    public static bool IsLocalRequest(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote.MapToIPv6()) &&
        (string.Equals(context.Request.Host.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
         IPAddress.TryParse(context.Request.Host.Host.Trim('[', ']'), out var host) && IPAddress.IsLoopback(host.MapToIPv6()));

    public static bool Valid(ClaimsPrincipal user) => user.Identity?.AuthenticationType == SchemeName &&
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value == "local-demo";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!IsLocalRequest(Context)) return Task.FromResult(AuthenticateResult.Fail("Local preview only."));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "local-demo")], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
