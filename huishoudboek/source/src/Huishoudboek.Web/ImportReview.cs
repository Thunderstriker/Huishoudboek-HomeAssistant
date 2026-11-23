using Huishoudboek.Infrastructure;

namespace Huishoudboek.Web;

public sealed class ImportReviewRow(PreviewRow source)
{
    public PreviewRow Source { get; set; } = source;
    public int Number => Source.RowNumber;
    public bool Include { get; set; } = source.Include && source.IsValid;
    public bool AccountsForCoverage => Source.IsValid && (Include || Source.DuplicateStatus == ImportDuplicateStatus.AlreadyImported);
    public bool NeedsReview(DateOnly today) => !Source.IsValid || Source.DuplicateStatus == ImportDuplicateStatus.NeedsReview ||
        Source.DuplicateStatus == ImportDuplicateStatus.AlreadyImported && Include ||
        Source.DuplicateStatus != ImportDuplicateStatus.AlreadyImported && (!Source.IsValid || !Include || Source.CategoryId == null ||
        Source.AmbiguousIdentity || Source.HasPlannedIdentity && Source.RecurringPaymentId == null || Source.Date > today);
}
