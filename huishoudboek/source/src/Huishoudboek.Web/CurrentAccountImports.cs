using Huishoudboek.Core;

namespace Huishoudboek.Web;

/// <summary>Upload cadence only. A recent upload does not prove recent or complete bank data.</summary>
public sealed record CurrentAccountImport(HomeAccount Account, DateOnly Today)
{
    public int? AgeDays => Account.LastImportedAt is { } imported && DateOnly.FromDateTime(imported) <= Today
        ? Today.DayNumber - DateOnly.FromDateTime(imported).DayNumber : null;
    public string Band => AgeDays switch
    {
        null => "unknown", <= 7 => "fresh", <= 14 => "due", <= 21 => "late", <= 30 => "old", _ => "very-old"
    };
    public string LabelKey => AgeDays switch
    {
        null => "ImportStatusUnknown", <= 7 => "ImportStatusFresh", <= 14 => "ImportStatusDue",
        <= 21 => "ImportStatusLate", <= 30 => "ImportStatusOld", _ => "ImportStatusVeryOld"
    };

    // Unknown/future dates rank first. Never let a recent import hide another account.
    public static IReadOnlyList<CurrentAccountImport> Create(HomeOverview overview) => overview.Accounts
        .Where(a => a.Account.Kind == AccountKind.Current)
        .Select(a => new CurrentAccountImport(a, overview.Today))
        .OrderBy(a => a.AgeDays.HasValue).ThenByDescending(a => a.AgeDays).ThenBy(a => a.Account.Account.Name).ToArray();
}
