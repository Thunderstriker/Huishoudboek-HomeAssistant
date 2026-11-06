using Microsoft.JSInterop;

namespace Huishoudboek.Web;

/// <summary>Browser-local display preferences, scoped to one Blazor circuit, never to the household database.</summary>
public sealed class UiPreferences(IJSRuntime js)
{
    private Task? load;
    public bool Loaded { get; private set; }
    public bool HideWeeklyReview { get; private set; }
    // Null means the default (all current accounts); an empty list deliberately hides every card.
    public IReadOnlyList<Guid>? DashboardAccountIds { get; private set; }
    public bool CanPersist { get; private set; } = true;
    public event Action? Changed;

    // Called after the first interactive render, when browser storage is available.
    public Task LoadAsync() => load ??= ReadAsync();

    private async Task ReadAsync()
    {
        try
        {
            var hidden = await js.InvokeAsync<bool?>("huishoudboek.preferences.readWeeklyReviewHidden");
            HideWeeklyReview = hidden ?? false;
            CanPersist = hidden.HasValue;
        }
        catch (JSException) { CanPersist = false; }
        try
        {
            var saved = await js.InvokeAsync<DashboardAccountsPreference?>("huishoudboek.preferences.readDashboardAccounts");
            CanPersist &= saved?.Available == true;
            if (saved?.AccountIds is { } ids && ids.All(id => Guid.TryParse(id, out _)))
                DashboardAccountIds = ids.Select(Guid.Parse).Distinct().ToArray();
        }
        catch (JSException) { CanPersist = false; }
        Loaded = true;
        Changed?.Invoke();
    }

    public async Task SetWeeklyReviewHiddenAsync(bool hidden)
    {
        await LoadAsync();
        HideWeeklyReview = hidden;
        try { CanPersist = await js.InvokeAsync<bool>("huishoudboek.preferences.writeWeeklyReviewHidden", hidden); }
        catch (JSException) { CanPersist = false; }
        Changed?.Invoke();
    }

    public async Task SetDashboardAccountsAsync(IEnumerable<Guid>? accountIds)
    {
        await LoadAsync();
        DashboardAccountIds = accountIds?.Distinct().ToArray();
        try { CanPersist = await js.InvokeAsync<bool>("huishoudboek.preferences.writeDashboardAccounts", new object?[] { DashboardAccountIds }); }
        catch (JSException) { CanPersist = false; }
        Changed?.Invoke();
    }
}

public sealed record DashboardAccountsPreference(bool Available, string[]? AccountIds);
