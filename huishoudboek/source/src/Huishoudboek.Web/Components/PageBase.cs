using Microsoft.AspNetCore.Components;

namespace Huishoudboek.Web.Components;

public abstract class PageBase : ComponentBase, IDisposable
{
    [Inject] protected WebState State { get; set; } = null!;
    protected override void OnInitialized() => State.Changed += Changed;
    protected virtual void OnStateChanged() { }
    private void Changed() => _ = InvokeAsync(() => { OnStateChanged(); StateHasChanged(); });
    public virtual void Dispose() => State.Changed -= Changed;
}
