using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

public sealed class HttpHouseholdStore : IAdministrationStore, IDisposable
{
    private readonly HttpClient _client;
    private string? _revision;
    public string DatabasePath { get; }
    public bool IsRemote => true;

    public HttpHouseholdStore(Uri address, string token)
    {
        if (!address.IsAbsoluteUri || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0 ||
            address.AbsolutePath != "/" || (address.Scheme != "https" && !(address.Scheme == "http" && address.IsLoopback)))
            throw new InvalidOperationException("Gebruik een HTTPS-serviceadres zonder pad; HTTP is alleen lokaal toegestaan.");
        DatabasePath = address.GetLeftPart(UriPartial.Authority);
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public HouseholdData Load()
    {
        using var response = _client.GetAsync("api/v1/administration").GetAwaiter().GetResult();
        Check(response);
        var snapshot = response.Content.ReadFromJsonAsync<AdministrationSnapshot>().GetAwaiter().GetResult()
            ?? throw new InvalidDataException("De service gaf geen administratie terug.");
        _revision = snapshot.Revision;
        return snapshot.Data;
    }

    public void Save(HouseholdData data)
    {
        Validation.Validate(data);
        using var request = new HttpRequestMessage(HttpMethod.Put, "api/v1/administration") { Content = JsonContent.Create(data) };
        request.Headers.TryAddWithoutValidation("If-Match", _revision);
        using var response = _client.SendAsync(request).GetAwaiter().GetResult();
        Check(response);
        _revision = response.Headers.ETag?.ToString() ?? throw new InvalidDataException("De service gaf geen revisie terug.");
    }

    public void Backup(string destination)
    {
        using var response = _client.GetAsync("api/v1/backup", HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        Check(response);
        using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        response.Content.CopyToAsync(target).GetAwaiter().GetResult();
    }

    public void Restore(string source)
    {
        using var content = new StreamContent(File.OpenRead(source));
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/restore") { Content = content };
        request.Headers.TryAddWithoutValidation("If-Match", _revision);
        using var response = _client.SendAsync(request).GetAwaiter().GetResult();
        Check(response);
    }

    private static void Check(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict) throw new AdministrationConflictException();
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Geen toegang tot de service. Controleer het adres en de bewerksleutel.");
        throw new InvalidOperationException($"De service kon deze handeling niet uitvoeren (HTTP {(int)response.StatusCode}). Controleer uw gegevens en de servicestatus.");
    }

    public void Dispose() => _client.Dispose();
}
