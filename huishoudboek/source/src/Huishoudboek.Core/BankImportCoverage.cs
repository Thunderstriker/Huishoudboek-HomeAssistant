namespace Huishoudboek.Core;

/// <summary>Only explicitly confirmed, complete statements establish coverage. Quiet days are valid; gaps are not.</summary>
public static class BankImportCoverage
{
    public static bool Covers(HouseholdData data, Guid accountId, DateOnly from, DateOnly through)
    {
        if (through < from) return false;
        var next = from;
        foreach (var batch in data.ImportBatches.Where(b => b.AccountId == accountId && !b.Undone &&
                     b.CoverageFrom != null && b.CoverageThrough != null).OrderBy(b => b.CoverageFrom))
        {
            if (batch.CoverageThrough < next) continue;
            if (batch.CoverageFrom > next) return false;
            if (batch.CoverageThrough >= through) return true;
            next = batch.CoverageThrough!.Value.AddDays(1);
        }
        return false;
    }

    public static DateOnly? ConfirmedThrough(HouseholdData data, Guid accountId) => data.ImportBatches
        .Where(b => b.AccountId == accountId && !b.Undone).Select(b => b.CoverageThrough).Max();

    public static void Invalidate(HouseholdData data, Guid? batchId)
    {
        if (data.ImportBatches.FirstOrDefault(b => b.Id == batchId) is not { } batch) return;
        batch.CoverageFrom = null;
        batch.CoverageThrough = null;
    }
}
