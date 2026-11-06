using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Service;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Huishoudboek.Tests;

public sealed class LocalPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "opencode", $"huishoudboek-preview-{Guid.NewGuid():N}");
    private string Administration => Path.Combine(_root, "administration");

    [Fact]
    public void FirstSetupCreatesDemoDataWithoutWebsiteCredentials()
    {
        var path = LocalPreviewBootstrap.Prepare(_root);
        var json = File.ReadAllText(path);
        using var settings = JsonDocument.Parse(json);
        var service = settings.RootElement.GetProperty("Huishoudboek").Deserialize<ServiceOptions>()!;
        service.Validate();
        Assert.Equal(Administration, service.DataDirectory);
        Assert.NotEqual(service.EditorToken, service.ReaderToken);
        Assert.DoesNotContain("Password", json);
        Assert.DoesNotContain("Username", json);
        using var administration = new AdministrationCoordinator(Administration);
        var data = administration.Read().Data;
        Validation.Validate(data);
        Assert.Equal(2, data.Owners.Count);
        Assert.Equal(3, data.Accounts.Count);
        Assert.NotEmpty(data.Transactions);
        Assert.NotEmpty(data.RecurringPayments);
        Assert.Contains(data.Categories, c => c.MonthlyBudget > 0);
    }

    [Fact]
    public void RestartsPreserveConfigurationAndEditsWithoutReseeding()
    {
        var path = LocalPreviewBootstrap.Prepare(_root);
        var before = File.ReadAllText(path);
        Guid id;
        using (var administration = new AdministrationCoordinator(Administration))
        {
            id = administration.AdministrationId;
            var snapshot = administration.Read();
            administration.Update(snapshot.Revision, data => {
                data.Owners.Add(new Owner { Name = "My preview owner" });
                data.Transactions.Clear();
            });
        }
        Assert.Equal(path, LocalPreviewBootstrap.Prepare(_root));
        Assert.Equal(before, File.ReadAllText(path));
        using var reopened = new AdministrationCoordinator(Administration);
        Assert.Equal(id, reopened.AdministrationId);
        Assert.Contains(reopened.Read().Data.Owners, o => o.Name == "My preview owner");
        Assert.Empty(reopened.Read().Data.Transactions);
    }

    [Theory]
    [InlineData("huishoudboek.db")]
    [InlineData(AdministrationLease.ServiceMarker)]
    public void MissingExistingFilesAreNotRecreated(string file)
    {
        var path = LocalPreviewBootstrap.Prepare(_root);
        var before = File.ReadAllText(path);
        var missing = Path.Combine(Administration, file);
        File.Delete(missing);
        Assert.Throws<InvalidDataException>(() => LocalPreviewBootstrap.Prepare(_root));
        Assert.False(File.Exists(missing));
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void UnconfiguredNonEmptyDirectoriesAreNeverAdopted()
    {
        Directory.CreateDirectory(_root);
        var existing = Path.Combine(_root, "important.txt");
        File.WriteAllText(existing, "Keep me");
        Assert.Throws<InvalidDataException>(() => LocalPreviewBootstrap.Prepare(_root));
        Assert.Equal("Keep me", File.ReadAllText(existing));
        Assert.Single(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void PreviewCannotBeRedirectedToAnotherAdministration()
    {
        var path = LocalPreviewBootstrap.Prepare(_root);
        var settings = JsonNode.Parse(File.ReadAllText(path))!;
        settings["Huishoudboek"]!["DataDirectory"] = Path.Combine(_root, "another-administration");
        File.WriteAllText(path, settings.ToJsonString());
        Assert.Throws<InvalidDataException>(() => LocalPreviewBootstrap.Prepare(_root));
        Assert.False(Directory.Exists(Path.Combine(_root, "another-administration")));
    }

    [Fact]
    public void PreviewUpgradesOnlyItsCopyWithASafetyBackup()
    {
        LocalPreviewBootstrap.Prepare(_root);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(Administration, "huishoudboek.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = PreviousSchema.RemoveSchema10Columns + "PRAGMA user_version=9;";
            command.ExecuteNonQuery();
        }
        LocalPreviewBootstrap.Prepare(_root);
        Assert.Single(Directory.EnumerateFiles(Administration, "*.voor-versie-*.db"));
        using var administration = new AdministrationCoordinator(Administration);
        Assert.NotEmpty(administration.Read().Data.Transactions);
    }

    [Fact]
    public void PreviewRespectsTheWriterLease()
    {
        LocalPreviewBootstrap.Prepare(_root);
        using var administration = new AdministrationCoordinator(Administration);
        Assert.Throws<InvalidOperationException>(() => LocalPreviewBootstrap.Prepare(_root));
    }

    [Theory]
    [InlineData(5099)]
    [InlineData(5100)]
    public void PreviewArgumentsBindOnlyToLoopback(int port)
    {
        var path = LocalPreviewBootstrap.Prepare(_root);
        Assert.Equal(["--config", path, "--urls", $"http://127.0.0.1:{port}"], LocalPreviewBootstrap.Arguments(_root, port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void InvalidPortsDoNotCreateAnyFiles(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalPreviewBootstrap.Arguments(_root, port));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void RelativePathsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => LocalPreviewBootstrap.Prepare("relative-preview"));
    }

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1:5099", true)]
    [InlineData("::1", "[::1]:5099", true)]
    [InlineData("::ffff:127.0.0.1", "localhost:5099", true)]
    [InlineData("127.0.0.1", "attacker.example:5099", false)]
    [InlineData("192.168.1.25", "localhost:5099", false)]
    [InlineData("::ffff:192.168.1.25", "127.0.0.1:5099", false)]
    public void PreviewRejectsRemotePeersAndDnsRebinding(string remote, string host, bool allowed)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Host = new HostString(host);
        context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        Assert.Equal(allowed, LocalPreviewAuthentication.IsLocalRequest(context));
    }

    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2024, 2, 29)]
    [InlineData(2026, 12, 31)]
    public void DemoDataIsValidAcrossMonthBoundariesAndNeverUsesFutureTransactions(int year, int month, int day)
    {
        var today = new DateOnly(year, month, day);
        var data = LocalPreviewData.Create(today);
        Validation.Validate(data);
        Assert.All(data.Transactions, t => Assert.True(t.Date <= today));
        Assert.Equal(13, data.Transactions.Select(t => (t.Date.Year, t.Date.Month)).Distinct().Count());
        Assert.All(data.ImportBatches, batch => Assert.Equal(batch.RowCount, data.Transactions.Count(t => t.ImportBatchId == batch.Id)));
        Assert.Equal(12, data.ImportBatches.Count(b => b.AccountId == data.Accounts[0].Id && b.CoverageThrough < new DateOnly(year, month, 1)));
    }

    [Fact]
    public void NormalHostingCannotEnablePasswordlessPreviewThroughConfiguration()
    {
        var path = LocalPreviewBootstrap.Prepare(_root);
        var settings = JsonNode.Parse(File.ReadAllText(path))!;
        settings["Website"] = new JsonObject { ["Enabled"] = true, ["PreviewEnabled"] = true };
        File.WriteAllText(path, settings.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => ServiceApplication.Build(["--config", path]));
        Assert.Throws<InvalidOperationException>(() => new WebsiteOptions { Enabled = true }.Validate());
    }

    [Fact]
    public void HomeAssistantStillStartsWithAnEmptyAdministration()
    {
        var path = HomeAssistantBootstrap.Prepare(_root);
        Assert.True(File.Exists(path));
        using var administration = new AdministrationCoordinator(Administration);
        Assert.Empty(administration.Read().Data.Accounts);
        Assert.Empty(administration.Read().Data.Transactions);
    }

    [Fact]
    public async Task LocalPreviewOpensWithoutLoginAndServesTheAppAndBrowserAssets()
    {
        var arguments = LocalPreviewBootstrap.Arguments(_root, 5099);
        // Let Kestrel allocate an unused loopback port for this test.
        arguments[3] = "http://127.0.0.1:0";
        await using var app = ServiceApplication.Build([.. arguments, "--environment", "Development"], localPreview: true);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var dashboard = await home.Content.ReadAsStringAsync();
        Assert.Contains("Huishoudboek", dashboard);
        Assert.Contains("(demo)", dashboard);
        Assert.DoesNotContain("href=\"signout\"", dashboard);
        var sessions = app.Services.GetRequiredService<WebsiteSessions>();
        Assert.True(sessions.Valid(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "local-demo")], LocalPreviewAuthentication.SchemeName))));
        Assert.Throws<UnauthorizedAccessException>(() => sessions.Create(false));
        foreach (var asset in new[] { "/_content/Huishoudboek.Web/site.css", "/_content/Huishoudboek.Web/site.js",
            "/_content/Huishoudboek.Web/Huishoudboek.Web.bundle.scp.css", "/_framework/blazor.web.js" })
        {
            using var response = await client.GetAsync(asset);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var login = await client.GetAsync("/login");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location!.OriginalString);
        using var negotiation = await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", new StringContent(""));
        Assert.Equal(HttpStatusCode.OK, negotiation.StatusCode);
        Assert.Contains("connectionToken", await negotiation.Content.ReadAsStringAsync());
        using var api = await client.GetAsync("/api/v1/status");
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        using var rebound = new HttpRequestMessage(HttpMethod.Get, "/");
        rebound.Headers.Host = "attacker.example";
        using var blocked = await client.SendAsync(rebound);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
