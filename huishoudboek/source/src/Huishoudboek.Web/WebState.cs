using Huishoudboek.Core;
using Huishoudboek.Infrastructure;

namespace Huishoudboek.Web;

/// <summary>Scoped to one Blazor circuit: never share filters, drafts or revisions between visitors.</summary>
public sealed class WebState(IWebAdministration backend)
{
    public HouseholdData Data { get; private set; } = HouseholdData.CreateEmpty();
    public string Revision { get; private set; } = "";
    public int Year { get; set; } = DateTime.Today.Year;
    public int Month { get; set; } = DateTime.Today.Month;
    public Guid? OwnerId { get; set; }
    public Guid? ReviewBatchId { get; set; }
    public bool Loaded { get; private set; }
    public bool Busy { get; private set; }
    public string Message { get; set; } = "";
    public bool IsError { get; private set; }
    public event Action? Changed;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    public DateOnly AsOf => Today < new DateOnly(Year, Month, 1).AddMonths(1).AddDays(-1) ? Today : new DateOnly(Year, Month, 1).AddMonths(1).AddDays(-1);
    public HashSet<Guid> Scope => Data.Accounts.Where(a => OwnerId == null || a.OwnerIds.Contains(OwnerId.Value)).Select(a => a.Id).ToHashSet();
    public string AccountName(Guid? id) => Data.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? "—";
    public string CategoryName(Guid? id) => Data.Categories.FirstOrDefault(c => c.Id == id)?.Name ?? "Zonder categorie";
    public void Notify() => Changed?.Invoke();
    public async Task LoadAsync()
    {
        var snapshot = await backend.ReadAsync();
        Data = snapshot.Data; Revision = snapshot.Revision; Loaded = true;
        if (OwnerId != null && !Data.Owners.Any(o => o.Id == OwnerId)) OwnerId = null;
        Notify();
    }
    public async Task<bool> RunAsync(Func<Task> action, string? success = null)
    {
        if (Busy) return false;
        Busy = true; IsError = false; Message = ""; Notify();
        try { await action(); if (success != null) Message = success; return true; }
        catch (AdministrationConflictException) { IsError = true; Message = "De administratie is elders gewijzigd. Uw wijziging is niet opgeslagen. Vernieuw en controleer uw invoer; verwerk een importvoorbeeld opnieuw."; }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or FormatException or ArgumentException)
        { IsError = true; Message = ex.Message; }
        catch (IOException) { IsError = true; Message = "Het bestand kon niet worden verwerkt. Controleer de bestandsgrootte, beschikbare schijfruimte en toegang tot de servicemap."; }
        catch (UnauthorizedAccessException) { IsError = true; Message = "Uw sessie is verlopen of ingetrokken. Meld u opnieuw aan."; }
        catch (Exception) { IsError = true; Message = "De handeling is niet voltooid. Controleer de servicestatus en vernieuw voordat u opnieuw probeert."; }
        finally { Busy = false; Notify(); }
        return false;
    }
    public async Task SaveAsync(Action<HouseholdData> change, string? revision = null)
    {
        var snapshot = await backend.UpdateAsync(revision ?? Revision, change);
        Data = snapshot.Data; Revision = snapshot.Revision; Notify();
    }
    public static string Money(decimal amount) => UiCulture.Euro(amount);
    public static string Kind(AccountKind kind) => kind switch { AccountKind.Current => "Betaalrekening", AccountKind.Savings => "Spaarrekening", _ => "Effectenrekening" };
}
