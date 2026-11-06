using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;
using Huishoudboek.Web.Components;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;

namespace Huishoudboek.Service;

public static class ServiceApplication
{
    private sealed record EditorOnly;

    public static WebApplication Build(string[] args, bool localPreview = false)
    {
        var initialize = args.Contains("--initialize");
        var claim = args.Contains("--claim");
        var configIndex = Array.IndexOf(args, "--config");
        var config = configIndex < 0 ? null : args.ElementAtOrDefault(configIndex + 1)
            ?? throw new InvalidOperationException("Geef een configuratiebestand op.");
        var hostArgs = args.Where((a, i) => a != "--initialize" && a != "--claim" && i != configIndex && (configIndex < 0 || i != configIndex + 1)).ToArray();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = hostArgs, ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(ServiceApplication).Assembly.GetName().Name });
        if (config != null) builder.Configuration.AddJsonFile(Path.GetFullPath(config), optional: false, reloadOnChange: false);
        builder.Services.AddWindowsService(o => o.ServiceName = "Huishoudboek");
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64L * 1024 * 1024);
        var options = builder.Configuration.GetSection("Huishoudboek").Get<ServiceOptions>()
            ?? throw new InvalidOperationException("Stel Huishoudboek:DataDirectory, EditorToken en ReaderToken in.");
        options.Validate();
        builder.Services.AddSingleton(options);
        var homeAssistant = builder.Configuration.GetSection("HomeAssistant").Get<HomeAssistantOptions>() ?? new HomeAssistantOptions();
        homeAssistant.Validate();
        if (localPreview && homeAssistant.Enabled) throw new InvalidOperationException("Lokale preview kan niet met Home Assistant-ingress worden gecombineerd.");
        builder.Services.AddSingleton(homeAssistant);
        builder.Services.AddSingleton(new WebHostSettings(homeAssistant.Enabled, localPreview));
        var website = localPreview ? new WebsiteOptions { Enabled = true, PreviewEnabled = true } :
            builder.Configuration.GetSection("Website").Get<WebsiteOptions>() ?? new WebsiteOptions();
        website.IngressEnabled = homeAssistant.Enabled;
        if (homeAssistant.Enabled && !website.Enabled) throw new InvalidOperationException("Schakel de website in voor Home Assistant.");
        WebsiteAuthentication.Register(builder, website, options);
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        WebsiteLanguage.Register(builder.Services);
        builder.Services.AddScoped<IWebAdministration, WebAdministration>();
        builder.Services.AddScoped<WebState>();
        builder.Services.AddScoped<UiPreferences>();
        builder.Services.AddSingleton(_ => new AdministrationCoordinator(options.DataDirectory, initialize, claim, allowUpgrade: homeAssistant.Enabled));
        builder.Services.AddSingleton(sp => new ImportWorkspace(sp.GetRequiredService<AdministrationCoordinator>().DirectoryPath));
        var app = builder.Build();
        try { Configure(app, options, website, homeAssistant); return app; }
        catch { app.DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
    }

    private static void Configure(WebApplication app, ServiceOptions options, WebsiteOptions website, HomeAssistantOptions homeAssistant)
    {
        // Acquire the lifetime writer lease and validate the administration before accepting requests.
        var coordinator = app.Services.GetRequiredService<AdministrationCoordinator>();
        app.Services.GetRequiredService<ImportWorkspace>();
        if (website.PreviewEnabled)
            app.Use(async (context, next) =>
            {
                // Check both the peer and Host to reject LAN access and browser DNS rebinding, even over HTTPS.
                if (!LocalPreviewAuthentication.IsLocalRequest(context)) { context.Response.StatusCode = 403; return; }
                await next(context);
            });
        if (homeAssistant.Enabled)
        {
            app.Use((context, next) => HomeAssistantIngress.PrepareAsync(context, homeAssistant, _ => next()));
            var forwarding = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
                ForwardLimit = 1
            };
            forwarding.KnownIPNetworks.Clear(); forwarding.KnownProxies.Clear();
            forwarding.KnownProxies.Add(homeAssistant.ProxyAddress);
            app.UseForwardedHeaders(forwarding);
        }
        app.UseRouting();
        app.UseRequestLocalization();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors " +
                (homeAssistant.Enabled ? "'self'" : "'none'") + "; base-uri 'self'; form-action 'self'; object-src 'none'";
            if ((context.Request.Path == "/login" || context.Request.Path == "/logout") && HttpMethods.IsPost(context.Request.Method))
            {
                if (context.Request.ContentLength > 16 * 1024) { context.Response.StatusCode = 413; return; }
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = 16 * 1024;
            }
            if (!homeAssistant.Enabled && !context.Request.IsHttps && !(context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip)))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { error = "Gebruik HTTPS voor toegang vanaf een andere machine." });
                return;
            }
            // CORS does not protect WebSockets. Reject cross-origin browser circuits explicitly.
            if (context.Request.Path.StartsWithSegments("/_blazor") && context.Request.Headers.Origin is { Count: > 0 } origins &&
                (!Uri.TryCreate(origins.ToString(), UriKind.Absolute, out var origin) ||
                 !string.Equals(origin.GetLeftPart(UriPartial.Authority), context.Request.Scheme + "://" + context.Request.Host, StringComparison.OrdinalIgnoreCase)))
            { context.Response.StatusCode = 403; return; }
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                // Ingress grants website access, never the desktop/editor API. No external API port in this app.
                if (homeAssistant.Enabled) { context.Response.StatusCode = 404; return; }
                var authorization = context.Request.Headers.Authorization.ToString();
                var token = authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : "";
                static bool Equal(string a, string b) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
                var editor = Equal(token, options.EditorToken);
                if (!editor && !Equal(token, options.ReaderToken)) { context.Response.StatusCode = 401; return; }
                if (!editor && context.GetEndpoint()?.Metadata.GetMetadata<EditorOnly>() != null) { context.Response.StatusCode = 403; return; }
            }
            try { await next(context); }
            catch (Exception ex) when (ex is AdministrationConflictException or MissingRevisionException or InvalidOperationException or InvalidDataException or FormatException or ArgumentException)
            {
                context.Response.StatusCode = ex switch { AdministrationConflictException => 412, MissingRevisionException => 428, _ => 400 };
                await context.Response.WriteAsJsonAsync(new { error = ex.Message });
            }
            catch (BadHttpRequestException ex) { context.Response.StatusCode = ex.StatusCode; }
            catch (Exception)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { error = "De handeling is niet voltooid. Controleer de servicestatus; er worden geen foutdetails met financiële gegevens gelogd." });
            }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseAntiforgery();
        app.UseStaticFiles();
        app.MapStaticAssets();
        app.MapGet("/favicon.ico", (HttpContext context) => Results.Redirect(context.Request.PathBase + "/_content/Huishoudboek.Web/favicon.svg"));
        if (homeAssistant.Enabled) app.MapGet("/health", () => Results.Text("ok"));
        WebsiteAuthentication.Map(app, website);
        WebsiteLanguage.Map(app);
        app.MapGet("/website/backup", () => Results.File(coordinator.Backup(), "application/octet-stream", $"huishoudboek-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db")).RequireAuthorization();
        app.MapGet("/website/export", () =>
        {
            var path = Path.Combine(coordinator.DirectoryPath, $"export-{Guid.NewGuid():N}.csv");
            try { ImportService.Export(path, coordinator.Read().Data); return Results.File(File.ReadAllBytes(path), "text/csv; charset=utf-8", $"huishoudboek-transacties-{DateTime.Now:yyyyMMdd}.csv"); }
            finally { File.Delete(path); }
        }).RequireAuthorization();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode().RequireAuthorization();
        var api = app.MapGroup("/api/v1");
        static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);
        static string? Revision(HttpContext c) => c.Request.Headers.IfMatch.FirstOrDefault();
        api.MapGet("/status", () => new { apiVersion = 1, administrationId = coordinator.AdministrationId, currency = "EUR" });
        api.MapGet("/accounts", () => Reporting.Accounts(coordinator.Read().Data, Today()));
        api.MapGet("/months/{period}", (string period) =>
        {
            if (!DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidOperationException("Gebruik een maand in de vorm jjjj-mm.");
            return Reporting.Month(coordinator.Read().Data, date.Year, date.Month, Today());
        });
        api.MapGet("/reporting-periods", () =>
        {
            var data = coordinator.Read().Data;
            var earliest = data.Accounts.Select(a => a.OpeningDate).Concat(data.Transactions.Select(t => t.Date)).Append(Today()).Min();
            var start = new DateOnly(Math.Clamp(earliest.Year, 1900, 2200), earliest.Month, 1);
            var current = new DateOnly(Today().Year, Today().Month, 1);
            var periods = new List<string>();
            for (var date = start; date <= current; date = date.AddMonths(1)) periods.Add(date.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            periods.Reverse();
            return periods;
        });
        var edit = api.MapGroup("").WithMetadata(new EditorOnly());
        edit.MapGet("/administration", (HttpContext c) =>
        {
            var snapshot = coordinator.Read(); c.Response.Headers.ETag = snapshot.Revision; return snapshot;
        });
        edit.MapPut("/administration", (HttpContext c, HouseholdData data) =>
        {
            var snapshot = coordinator.Replace(Revision(c), data); c.Response.Headers.ETag = snapshot.Revision; return Results.NoContent();
        });
        edit.MapPost("/categorization", (HttpContext c, CategorizeRequest request) =>
        {
            var snapshot = coordinator.Update(Revision(c), d => Categorization.Assign(d, request.TransactionIds.ToHashSet(), request.CategoryId,
                request.RememberText, request.MatchingUncategorizedIds?.ToHashSet(), request.UserDescription));
            c.Response.Headers.ETag = snapshot.Revision; return new { revision = snapshot.Revision };
        });
        edit.MapPost("/imports/upload", async (HttpContext c, string name, ImportWorkspace workspace) =>
            await workspace.UploadAsync(c.Request.Body, name, c.RequestAborted));
        edit.MapPost("/imports/preview", (PreviewRequest request, ImportWorkspace workspace) => workspace.Preview(request, coordinator.Read()));
        edit.MapPost("/imports/adjust", (AdjustImportRequest request, ImportWorkspace workspace) => workspace.Adjust(request));
        edit.MapPost("/imports/commit", (HttpContext c, CommitImportRequest request, ImportWorkspace workspace) =>
            workspace.Commit(request, Revision(c), coordinator, Today()));
        edit.MapPost("/imports/{id:guid}/undo", (HttpContext c, Guid id) =>
        {
            var snapshot = coordinator.Update(Revision(c), data => HouseholdEdits.UndoImport(data, id));
            return new { revision = snapshot.Revision };
        });
        edit.MapGet("/backup", () => Results.File(coordinator.Backup(), "application/octet-stream", "huishoudboek-backup.db"));
        edit.MapPost("/restore", async (HttpContext c) =>
        {
            var path = Path.Combine(coordinator.DirectoryPath, $"restore-{Guid.NewGuid():N}.db");
            try
            {
                await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await c.Request.Body.CopyToAsync(stream, c.RequestAborted);
                return new { revision = coordinator.Restore(Revision(c), path) };
            }
            finally { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
        });
    }
}
