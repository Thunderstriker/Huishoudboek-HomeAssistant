using System.Net;
using System.Text.Json;
using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;
using Microsoft.AspNetCore.Components.Authorization;

namespace Huishoudboek.Service;

public sealed class EnergyAdministration(AdministrationCoordinator coordinator, ReceiptRecognition recognition, BelgianMarketClient market,
    AuthenticationStateProvider authentication, WebsiteSessions sessions) : IEnergyAdministration, IDisposable
{
    private sealed record Upload(Guid Id, DateTimeOffset Expires, EnergyTariffDocument Document);
    private Upload? _upload;
    private Timer? _expiry;
    private bool _disposed;

    private async Task AuthorizeAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!sessions.Valid((await authentication.GetAuthenticationStateAsync()).User)) throw new UnauthorizedAccessException();
        if (_upload?.Expires < DateTimeOffset.UtcNow) ClearUpload();
    }

    public async Task<EnergyView> ReadAsync(Guid? contractId, DateOnly month, CancellationToken token)
    {
        await AuthorizeAsync(token);
        return await Task.Run(() => coordinator.ReadEnergy(contractId, month, BelgianEnergyTime.Today), token);
    }

    public async Task SaveAsync(string revision, EnergyContract contract, EnergyTariff? tariff, Guid? uploadId, bool keepPdf, CancellationToken token)
    {
        await AuthorizeAsync(token);
        EnergyTariffDocument? document = null;
        if (tariff != null && keepPdf && uploadId is { } id)
        {
            if (_upload is not { } upload || upload.Id != id || upload.Expires < DateTimeOffset.UtcNow)
                throw new InvalidOperationException(UiCulture.Text("EnergyPdfExpired"));
            document = new(tariff.Id, upload.Document.Name, upload.Document.Content);
        }
        await Task.Run(() => coordinator.SaveEnergy(revision, contract, tariff, document, keepPdf), token);
        ClearUpload();
    }

    public async Task SaveIndexAsync(string revision, EnergyIndexValue value, CancellationToken token)
    {
        await AuthorizeAsync(token);
        await Task.Run(() => coordinator.SaveEnergyIndex(revision, value), token);
    }

    public async Task<EnergyImportPreview> PreviewAsync(Stream source, CancellationToken token)
    {
        await AuthorizeAsync(token);
        using var buffer = await ReadLimitedAsync(source, EnergyCsv.MaximumBytes, token);
        var parsed = await Task.Run(() => EnergyCsv.Read(buffer, token), token);
        await AuthorizeAsync(token);
        return await Task.Run(() => coordinator.PreviewEnergy(parsed), token);
    }

    public async Task CommitAsync(EnergyImportPreview preview, bool replace, CancellationToken token)
    {
        await AuthorizeAsync(token);
        await Task.Run(() => coordinator.CommitEnergy(preview, replace), token);
    }

    public async Task<EnergyQuarterClearPreview> PreviewClearQuarterHoursAsync(string ean, DateOnly from, DateOnly through, CancellationToken token)
    {
        await AuthorizeAsync(token);
        return await Task.Run(() => coordinator.PreviewClearEnergyQuarterHours(ean, from, through), token);
    }

    public async Task<int> ClearQuarterHoursAsync(EnergyQuarterClearPreview preview, CancellationToken token)
    {
        await AuthorizeAsync(token);
        return await Task.Run(() => coordinator.ClearEnergyQuarterHours(preview), token);
    }

    public async Task<EnergyPdfDraft> ReadPdfAsync(Stream source, string name, CancellationToken token)
    {
        await AuthorizeAsync(token);
        ClearUpload();
        using var bytes = await ReadLimitedAsync(source, PurchaseReceipts.MaximumDocumentBytes, token);
        var content = bytes.ToArray();
        if (!EnergyPdf.HasSignature(content)) throw new InvalidDataException(UiCulture.Text("EnergyPdfOnly"));
        var document = EnergyPdf.Create(Guid.Empty, name, content);
        var result = await recognition.ReadTariffPagesAsync(EnergyPdf.ForExtraction(document.Content), token);
        var card = LuminusTariffCard.Parse(result.Pages);
        await AuthorizeAsync(token);
        var upload = new Upload(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(15), document);
        _upload = upload;
        _expiry = new Timer(static state =>
        {
            var (owner, id) = ((EnergyAdministration Owner, Guid Id))state!;
            var current = owner._upload;
            if (current?.Id == id) Interlocked.CompareExchange(ref owner._upload, null, current);
        }, (this, upload.Id), TimeSpan.FromMinutes(15), Timeout.InfiniteTimeSpan);
        return new(upload.Id, document.Name, card, result.State);
    }

    public async Task<int> FetchPricesAsync(string revision, Guid contractId, DateOnly month, bool current, CancellationToken token)
    {
        await AuthorizeAsync(token);
        var ranges = coordinator.PlanEnergyFetch(revision, contractId, month, current, BelgianEnergyTime.Today);
        var count = 0;
        foreach (var range in ranges)
        {
            var prices = await market.GetAsync(range.From, range.Through, token);
            await AuthorizeAsync(token);
            await Task.Run(() => coordinator.SaveEnergyPrices(revision, prices), token);
            count += prices.Count;
        }
        return count;
    }

    public void DiscardPdf(Guid id) { if (_upload?.Id == id) ClearUpload(); }
    private void ClearUpload() { _expiry?.Dispose(); _expiry = null; _upload = null; }
    public void Dispose() { _disposed = true; ClearUpload(); }

    internal static async Task<MemoryStream> ReadLimitedAsync(Stream source, long maximum, CancellationToken token)
    {
        var output = new MemoryStream();
        var buffer = new byte[81920];
        try
        {
            int count;
            while ((count = await source.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + count > maximum) throw new InvalidDataException(UiCulture.Text("EnergyFileLimit"));
                await output.WriteAsync(buffer.AsMemory(0, count), token);
            }
            output.Position = 0;
            return output;
        }
        catch { output.Dispose(); throw; }
    }
}

public sealed class BelgianMarketClient : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new(EnergyChartsPrices.SourceUrl), Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _retryAfter;

    public async Task<IReadOnlyList<EnergyMarketPrice>> GetAsync(DateOnly from, DateOnly through, CancellationToken token)
    {
        if (!await _gate.WaitAsync(0, token)) throw new InvalidOperationException(UiCulture.Text("EnergyPriceBusy"));
        try
        {
            if (_retryAfter > DateTimeOffset.UtcNow) throw new InvalidOperationException(UiCulture.Text("EnergyPriceLimit"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await _http.GetAsync(EnergyChartsPrices.Path(from, through), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return [];
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var header = response.Headers.RetryAfter;
                _retryAfter = header?.Date ?? DateTimeOffset.UtcNow.Add(header?.Delta ?? TimeSpan.FromMinutes(1));
                throw new InvalidOperationException(UiCulture.Text("EnergyPriceLimit"));
            }
            response.EnsureSuccessStatusCode();
            using var content = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var limited = await EnergyAdministration.ReadLimitedAsync(content, 2 * 1024 * 1024, timeout.Token);
            using var parsed = await JsonDocument.ParseAsync(limited, cancellationToken: timeout.Token);
            return EnergyChartsPrices.Parse(parsed.RootElement, from, through, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !token.IsCancellationRequested))
        { throw new InvalidOperationException(UiCulture.Text("EnergyPriceFailed")); }
        finally { _gate.Release(); }
    }

    public void Dispose() { _http.Dispose(); _gate.Dispose(); }
}
