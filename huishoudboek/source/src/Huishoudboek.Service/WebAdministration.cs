using Huishoudboek.Core;
using Huishoudboek.Infrastructure;
using Huishoudboek.Web;
using Microsoft.AspNetCore.Components.Authorization;

namespace Huishoudboek.Service;

/// <summary>Authenticated in-process adapter. No editor secret ever goes to a browser.</summary>
public sealed class WebAdministration(AdministrationCoordinator coordinator, ImportWorkspace imports,
    AuthenticationStateProvider authentication, WebsiteSessions sessions) : IWebAdministration
{
    private async Task AuthorizeAsync()
    {
        if (!sessions.Valid((await authentication.GetAuthenticationStateAsync()).User)) throw new UnauthorizedAccessException();
    }
    public async Task<AdministrationSnapshot> ReadAsync() { await AuthorizeAsync(); return await Task.Run(coordinator.Read); }
    public async Task<AdministrationSnapshot> UpdateAsync(string revision, Action<HouseholdData> change)
    { await AuthorizeAsync(); return await Task.Run(() => coordinator.Update(revision, change)); }
    public async Task<UploadResponse> UploadAsync(Stream source, string name, CancellationToken cancellationToken)
    { await AuthorizeAsync(); return await imports.UploadAsync(source, name, cancellationToken); }
    public async Task<PreviewResponse> PreviewAsync(PreviewRequest request, string revision)
    {
        await AuthorizeAsync();
        return await Task.Run(() =>
        {
            var snapshot = coordinator.Read();
            if (snapshot.Revision != revision) throw new AdministrationConflictException();
            return imports.Preview(request, snapshot);
        });
    }
    public async Task<PreviewRow> AdjustAsync(AdjustImportRequest request) { await AuthorizeAsync(); return await Task.Run(() => imports.Adjust(request)); }
    public async Task<CommitImportResponse> CommitAsync(CommitImportRequest request, string revision)
    { await AuthorizeAsync(); return await Task.Run(() => imports.Commit(request, revision, coordinator, DateOnly.FromDateTime(DateTime.Today))); }
    public async Task<string> RestoreAsync(Stream source, string revision, CancellationToken cancellationToken)
    {
        await AuthorizeAsync();
        var path = Path.Combine(coordinator.DirectoryPath, $"restore-{Guid.NewGuid():N}.db");
        try
        {
            await using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920]; long size = 0; int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    size += count;
                    if (size > 64L * 1024 * 1024) throw new InvalidDataException("De back-up is groter dan 64 MB.");
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }
            await AuthorizeAsync();
            return await Task.Run(() => coordinator.Restore(revision, path), cancellationToken);
        }
        finally { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
    }
}
