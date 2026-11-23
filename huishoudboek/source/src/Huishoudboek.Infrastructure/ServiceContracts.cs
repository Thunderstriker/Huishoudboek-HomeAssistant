using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

public sealed record AdministrationSnapshot(string Revision, HouseholdData Data);
public sealed record CategorizeRequest(Guid[] TransactionIds, Guid? CategoryId, string? RememberText = null,
    Guid[]? MatchingUncategorizedIds = null, string? UserDescription = null);
public sealed record PreviewRequest(Guid UploadId, Guid AccountId, DateOnly From, DateOnly Through,
    ImportProfile? Profile = null, string? Worksheet = null, bool Windows1252 = false);
// CounterAccountId is retained for older clients, but imports reject non-null values.
public sealed record ImportSelection(int RowNumber, Guid? CategoryId, Guid? CounterAccountId, bool AllowDuplicate = false);
public sealed record AdjustImportRequest(Guid PreviewId, int RowNumber, Guid? CounterAccountId, Guid? CategoryId, bool SetCategory);
public sealed record CommitImportRequest(Guid PreviewId, ImportSelection[] Rows, bool ConfirmCompletePeriod = false);
public sealed record UploadResponse(Guid UploadId, IReadOnlyList<string> Worksheets);
public sealed record PreviewRow(int RowNumber, DateOnly? Date, string Description, decimal? Amount,
    decimal? StatementBalance, bool IsValid, bool Include, string Note, Guid? CategoryId, Guid? CounterAccountId, string Kind,
    string RecognizedName = "", Guid? RecurringPaymentId = null, DateOnly? RecurringOccurrenceDate = null, bool HasPlannedIdentity = false,
    bool AmbiguousIdentity = false, ImportDuplicateStatus DuplicateStatus = ImportDuplicateStatus.New, Guid? MatchedTransactionId = null);
public sealed record PreviewResponse(Guid PreviewId, string Revision, ImportProfile Profile,
    IReadOnlyList<string> Headers, IReadOnlyList<ImportCategoryChoice> Categories, IReadOnlyList<PreviewRow> Rows);
public sealed record CommitImportResponse(Guid BatchId, int Count, string Revision, ImportBalanceCheck Balance);
