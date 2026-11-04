using Huishoudboek.Core;

namespace Huishoudboek.Web.Components;

public abstract class EditPageBase : PageBase
{
    public EditorDraft? Draft { get; set; }
    public ConfirmationOperation? Confirmation { get; set; }
    protected void Ask(string title, string text, Action<HouseholdData> action, string? label = null)
        => Confirmation = new(title, text, label ?? UiCulture.Text("Confirm"), State.Revision, action);
    public void CloseDraft() => Draft = null;
    public void CloseConfirmation() { if (!State.Busy) Confirmation = null; }
    public async Task ConfirmAsync()
    {
        if (Confirmation is not { } operation) return;
        if (await State.RunAsync(() => State.SaveAsync(operation.Action, operation.Revision), UiCulture.Text("ChangesSaved"))) Confirmation = null;
    }
}
public sealed record ConfirmationOperation(string Title, string Text, string Label, string Revision, Action<HouseholdData> Action);
