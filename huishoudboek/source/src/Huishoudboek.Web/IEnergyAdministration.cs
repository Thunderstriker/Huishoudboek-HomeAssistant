using Huishoudboek.Core;
using Huishoudboek.Infrastructure;

namespace Huishoudboek.Web;

public sealed record EnergyPublishedDay(DateOnly Day, EnergyTariff Tariff, IReadOnlyList<EnergyPriceSlot> Slots);
public sealed record EnergyView(string Revision, EnergySettings Settings, Guid? ContractId, EnergyCostReport? Report,
    IReadOnlyList<EnergyPublishedDay> Published, bool CanFetchHistorical);
public sealed record EnergyPdfDraft(Guid UploadId, string FileName, LuminusCard? Card, ReceiptRecognitionState State);

public interface IEnergyAdministration
{
    Task<EnergyView> ReadAsync(Guid? contractId, DateOnly month, CancellationToken token);
    Task SaveAsync(string revision, EnergyContract contract, EnergyTariff? tariff, Guid? uploadId, bool keepPdf, CancellationToken token);
    Task SaveIndexAsync(string revision, EnergyIndexValue value, CancellationToken token);
    Task<EnergyImportPreview> PreviewAsync(Stream source, CancellationToken token);
    Task CommitAsync(EnergyImportPreview preview, bool replace, CancellationToken token);
    Task<EnergyPdfDraft> ReadPdfAsync(Stream source, string name, CancellationToken token);
    void DiscardPdf(Guid id);
    Task<int> FetchPricesAsync(string revision, Guid contractId, DateOnly month, bool current, CancellationToken token);
}
