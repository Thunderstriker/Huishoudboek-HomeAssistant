using Microsoft.JSInterop;

namespace Huishoudboek.Web;

/// <summary>Browser-local display preferences, scoped to one Blazor circuit, never to the household database.</summary>
public sealed class UiPreferences(IJSRuntime js)
{
    private Task? load;
    public bool Loaded { get; private set; }
    public bool HideWeeklyReview { get; private set; }
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
}
