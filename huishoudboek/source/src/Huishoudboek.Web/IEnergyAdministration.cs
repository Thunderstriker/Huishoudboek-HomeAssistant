using Huishoudboek.Core;
using Huishoudboek.Infrastructure;

namespace Huishoudboek.Web;

public sealed record EnergyPublishedDay(DateOnly Day, EnergyTariff Tariff, IReadOnlyList<EnergyPriceSlot> Slots);
public sealed record EnergyMonthReport(DateOnly Month, EnergyCostReport Costs, bool CanFetchHistorical);
public sealed record EnergyView(string Revision, EnergySettings Settings, Guid? ContractId, DateOnly Month,
    IReadOnlyList<EnergyMonthReport> Months, IReadOnlyList<EnergyPublishedDay> Published, IReadOnlyList<string> ReadingConnections)
{
    private EnergyMonthReport? Selection => Months.FirstOrDefault(m => m.Month == Month);
    public EnergyCostReport? Report => Selection?.Costs;
    public bool CanFetchHistorical => Selection?.CanFetchHistorical == true;
}
public sealed record EnergyReadingsClearPreview(string Revision, string Ean, int Count);
public sealed record EnergyPdfDraft(Guid UploadId, string FileName, LuminusCard? Card, ReceiptRecognitionState State);

public interface IEnergyAdministration
{
    Task<EnergyView> ReadAsync(Guid? contractId, DateOnly month, CancellationToken token);
    Task SaveAsync(string revision, EnergyContract contract, EnergyTariff? tariff, Guid? uploadId, bool keepPdf, CancellationToken token);
    Task SaveIndexAsync(string revision, EnergyIndexValue value, CancellationToken token);
    Task SaveMonthlyChargeAsync(string revision, string ean, DateOnly month, decimal? amount, CancellationToken token);
    Task SaveSurchargeRatesAsync(string revision, EnergySurchargeRates rates, bool remove, CancellationToken token);
    Task<EnergyImportPreview> PreviewAsync(Stream source, CancellationToken token);
    Task CommitAsync(EnergyImportPreview preview, bool replace, CancellationToken token);
    Task<EnergyReadingsClearPreview> PreviewClearReadingsAsync(string ean, CancellationToken token);
    Task<int> ClearReadingsAsync(EnergyReadingsClearPreview preview, CancellationToken token);
    Task<EnergyPdfDraft> ReadPdfAsync(Stream source, string name, CancellationToken token);
    void DiscardPdf(Guid id);
    Task<int> FetchPricesAsync(string revision, Guid contractId, DateOnly month, bool current, CancellationToken token);
}
