using Huishoudboek.Core;

namespace Huishoudboek.Infrastructure;

public enum ImportDuplicateStatus { New, AlreadyImported, NeedsReview }

/// <summary>
/// Matches occurrences, not distinct values: two identical bank payments remain two payments.
/// Bank text, date, signed amount and (when available on both sides) running balance are used;
/// categories, personal descriptions and planning links never establish bank identity.
/// </summary>
public static class ImportDuplicates
{
    private readonly record struct BankKey(DateOnly Date, decimal Amount, string Description);
    private readonly record struct BalanceKey(decimal? Balance);
    private readonly record struct ReviewKey(DateOnly Date, decimal Amount, string Description);
    private readonly record struct RunningBalanceKey(DateOnly Date, decimal Amount, decimal Balance);

    public static void Apply(HouseholdData data, Guid accountId, IReadOnlyList<ImportRow> rows)
    {
        var valid = rows.Where(r => r.IsValid).ToList();
        if (valid.Count == 0) return;
        var from = valid.Min(r => r.Transaction!.Date);
        var through = valid.Max(r => r.Transaction!.Date);
        var batches = data.ImportBatches.Where(b => !b.Undone).Select(b => b.Id).ToHashSet();
        var existing = data.Transactions.Where(t => t.AccountId == accountId && t.Date >= from && t.Date <= through &&
            (t.ImportBatchId == null || batches.Contains(t.ImportBatchId.Value))).OrderBy(t => t.Id).ToList();
        var imported = existing.Where(t => t.ImportBatchId != null).GroupBy(Key).ToDictionary(g => g.Key, g => new Occurrences(g));
        var manual = existing.Where(t => t.ImportBatchId == null).GroupBy(Key).ToDictionary(g => g.Key, g => new Occurrences(g));
        var similarText = existing.GroupBy(t => new ReviewKey(t.Date, t.Amount, Normalize(t.Description)))
            .ToDictionary(g => g.Key, g => g.Select(t => t.Description.Trim()).ToHashSet(StringComparer.Ordinal));
        var similarBalance = existing.Where(t => t.StatementBalance != null)
            .GroupBy(t => new RunningBalanceKey(t.Date, t.Amount, t.StatementBalance!.Value))
            .ToDictionary(g => g.Key, g => g.Select(t => t.Description.Trim()).ToHashSet(StringComparer.Ordinal));

        // Match rows carrying balances first so a row without a balance cannot consume
        // the only occurrence that a more specific row can match. Source order is unchanged.
        foreach (var row in valid.OrderByDescending(r => r.Transaction!.StatementBalance != null))
        {
            var transaction = row.Transaction!;
            var key = Key(transaction);
            var match = imported.GetValueOrDefault(key)?.Take(transaction.StatementBalance);
            if (match != null)
            {
                row.DuplicateStatus = ImportDuplicateStatus.AlreadyImported;
                row.MatchedTransactionId = match.Id;
                row.Include = false;
                continue;
            }
            var manualMatch = manual.GetValueOrDefault(key)?.Take(transaction.StatementBalance);
            var normalized = new ReviewKey(transaction.Date, transaction.Amount, Normalize(transaction.Description));
            var differentText = similarText.TryGetValue(normalized, out var descriptions) &&
                (descriptions.Count > 1 || !descriptions.Contains(key.Description));
            var differentBalanceText = transaction.StatementBalance is { } balance &&
                similarBalance.TryGetValue(new(transaction.Date, transaction.Amount, balance), out var balanceDescriptions) &&
                (balanceDescriptions.Count > 1 || !balanceDescriptions.Contains(key.Description));
            if (manualMatch != null || differentText || differentBalanceText)
            {
                row.DuplicateStatus = ImportDuplicateStatus.NeedsReview;
                row.MatchedTransactionId = manualMatch?.Id;
                row.Include = false;
            }
        }
    }

    private static BankKey Key(Transaction t) => new(t.Date, t.Amount, t.Description.Trim());
    private static string Normalize(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private sealed class Occurrences
    {
        private readonly Dictionary<BalanceKey, Queue<Transaction>> balances;
        private readonly Queue<Transaction> all;
        private readonly HashSet<Guid> used = [];

        public Occurrences(IEnumerable<Transaction> entries)
        {
            var transactions = entries.ToList();
            balances = transactions.GroupBy(t => new BalanceKey(t.StatementBalance)).ToDictionary(g => g.Key, g => new Queue<Transaction>(g));
            all = new(transactions);
        }

        public Transaction? Take(decimal? balance)
        {
            var match = Next(balances.GetValueOrDefault(new(balance)));
            if (match == null) match = Next(balance == null ? all : balances.GetValueOrDefault(new(null)));
            if (match != null) used.Add(match.Id);
            return match;
        }

        private Transaction? Next(Queue<Transaction>? queue)
        {
            if (queue == null) return null;
            while (queue.TryPeek(out var entry))
            {
                queue.Dequeue();
                if (!used.Contains(entry.Id)) return entry;
            }
            return null;
        }
    }
}
