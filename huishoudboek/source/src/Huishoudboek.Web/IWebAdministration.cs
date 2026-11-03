using Huishoudboek.Core;
using Huishoudboek.Infrastructure;

namespace Huishoudboek.Web;

public interface IWebAdministration
{
    Task<AdministrationSnapshot> ReadAsync();
    Task<AdministrationSnapshot> UpdateAsync(string revision, Action<HouseholdData> change);
    Task<UploadResponse> UploadAsync(Stream source, string name, CancellationToken cancellationToken);
    Task<PreviewResponse> PreviewAsync(PreviewRequest request, string revision);
    Task<PreviewRow> AdjustAsync(AdjustImportRequest request);
    Task<CommitImportResponse> CommitAsync(CommitImportRequest request, string revision);
    Task<string> RestoreAsync(Stream source, string revision, CancellationToken cancellationToken);
}
