using System.Text;
using System.Text.RegularExpressions;

namespace Huishoudboek.Core;

public sealed record RecognitionChange(Transaction Before, Transaction After, string PlanningName = "");

/// <summary>One deterministic recognition setup: identity and category do not depend on a scheduled occurrence.</summary>
public static class TransactionRecognition
{
    public static string Normalize(string text) => string.Join(" ", Regex.Matches(text.Normalize(NormalizationForm.FormKC),
        @"[\p{L}\p{N}]+").Select(m => m.Value)).ToUpperInvariant();

    public static bool Contains(string description, string text, bool normalize = false) => !string.IsNullOrWhiteSpace(text) &&
        (description.Contains(text, StringComparison.OrdinalIgnoreCase) || normalize &&
            Normalize(text) is { Length: > 0 } normalized && (" " + Normalize(description) + " ").Contains(" " + normalized + " ", StringComparison.Ordinal));

    public static bool Matches(HouseholdData data, CategorizationRule rule, Transaction transaction) => new RecognitionMatcher(data).Matches(rule, transaction);

    public static string RuleName(HouseholdData data, CategorizationRule rule) => rule.Name.Length > 0 ? rule.Name :
        data.RecurringPayments.FirstOrDefault(r => r.Id == rule.RecurringPaymentId)?.Name ?? "";

    public static CategorizationRule? Identity(HouseholdData data, Transaction transaction) => new RecognitionMatcher(data).Identity(transaction);

    public static string DisplayName(HouseholdData data, Transaction transaction) => transaction.UserDescription.Length > 0
        ? transaction.UserDescription : transaction.RecognizedName.Length > 0 ? transaction.RecognizedName :
        data.RecurringPayments.FirstOrDefault(r => r.Id == transaction.RecurringPaymentId)?.Name ?? transaction.Description;

    public static string SearchText(HouseholdData data, Transaction transaction) =>
        $"{transaction.Description} {transaction.UserDescription} {transaction.RecognizedName} {DisplayName(data, transaction)}";

    public static void ApplyName(HouseholdData data, Transaction transaction, RecognitionMatcher? matcher = null)
    {
        if (transaction.Kind == TransactionKind.Transfer || (matcher ?? new RecognitionMatcher(data)).Identity(transaction) is not { } identity) return;
        transaction.RecognizedName = RuleName(data, identity);
        transaction.RecognitionRuleId = identity.Id;
    }

    public static bool RecognizesPlan(HouseholdData data, RecurringPayment payment, Transaction transaction)
    {
        var rule = data.Rules.FirstOrDefault(r => r.RecurringPaymentId == payment.Id);
        // The schedule already has an indexed set of exact linked examples; do not scan history here.
        return rule != null ? (rule.AccountId == null || rule.AccountId == transaction.AccountId) &&
            (rule.Direction == PaymentDirection.Any || (int)rule.Direction == Math.Sign(transaction.Amount)) &&
            Contains(transaction.Description, rule.Contains, rule.NormalizeText) : Contains(transaction.Description, payment.MatchText, normalize: true);
    }

    /// <summary>Add plan definitions after the existing category rules. Never merge rules or edit bank entries.</summary>
    public static void UpgradeLegacy(HouseholdData data)
    {
        foreach (var payment in data.RecurringPayments.Where(p => !data.Rules.Any(r => r.RecurringPaymentId == p.Id)))
            data.Rules.Add(new CategorizationRule { Name = payment.Name, Contains = payment.MatchText, CategoryId = payment.CategoryId,
                AccountId = payment.AccountId, Direction = (PaymentDirection)Math.Sign(payment.Amount), RecurringPaymentId = payment.Id,
                NormalizeText = true });
    }

    public static void DeleteRule(HouseholdData data, Guid ruleId)
    {
        if (data.Rules.Any(r => r.Id == ruleId && r.RecurringPaymentId != null))
            throw new InvalidOperationException("Deze herkenning hoort bij een planning. Bewerk de herkenning of verwijder de planning via Vaste posten.");
        data.Rules.RemoveAll(r => r.Id == ruleId);
        foreach (var transaction in data.Transactions.Where(t => t.RecognitionRuleId == ruleId)) transaction.RecognitionRuleId = null;
    }

    public static IReadOnlyList<RecognitionChange> PreviewHistory(HouseholdData data, DateOnly today, Guid? ruleId = null,
        IReadOnlySet<Guid>? scope = null)
    {
        var rule = ruleId == null ? null : data.Rules.Single(r => r.Id == ruleId);
        var matcher = new RecognitionMatcher(data);
        var transfers = TransferMatching.ConfirmedTransactionIds(data);
        var originals = data.Transactions.Where(t => t.Date <= today && t.Kind != TransactionKind.Transfer && !transfers.Contains(t.Id) &&
            (scope == null || scope.Contains(t.AccountId)) && (rule == null || matcher.Matches(rule, t) || t.RecurringPaymentId != null && t.RecurringPaymentId == rule.RecurringPaymentId)).ToDictionary(t => t.Id);
        var copies = originals.Values.Select(t => t with { }).ToList();
        Finance.ApplySuggestions(data, copies);
        return copies.Where(t => t != originals[t.Id]).Select(t => new RecognitionChange(originals[t.Id], t,
                data.RecurringPayments.FirstOrDefault(p => p.Id == t.RecurringPaymentId)?.Name ?? ""))
            .OrderByDescending(c => c.Before.Date).ThenBy(c => c.Before.Id).ToList();
    }

    public static void ApplyHistory(HouseholdData data, IReadOnlySet<Guid> selected, DateOnly today, Guid? ruleId = null,
        IReadOnlySet<Guid>? scope = null)
    {
        // Recompute the whole preview, not just the selection: deselecting a competitor must not make a link unambiguous.
        var changes = PreviewHistory(data, today, ruleId, scope).Where(c => selected.Contains(c.Before.Id)).ToList();
        if (changes.Count != selected.Count) throw new InvalidOperationException("Een geselecteerde herkenning is gewijzigd. Vernieuw het voorbeeld.");
        // These are annotations from our own preview, not bank edits. Do not pass them
        // through the bank editor's trimming/normalization of original descriptions.
        foreach (var change in changes) data.Transactions[data.Transactions.FindIndex(t => t.Id == change.Before.Id)] = change.After;
    }
}

/// <summary>Per-operation indexes keep recognition practical for multi-year histories and hundreds of rules.</summary>
public sealed class RecognitionMatcher
{
    private readonly HouseholdData _data;
    private readonly Dictionary<Guid, HashSet<string>> _known;
    private readonly Dictionary<string, string> _normalized = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _texts;
    private readonly Dictionary<Guid, string> _names;

    public RecognitionMatcher(HouseholdData data)
    {
        _data = data;
        _known = data.Transactions.Where(t => t.RecurringPaymentId != null).GroupBy(t => t.RecurringPaymentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(t => Normalized(t.Description)).ToHashSet());
        _texts = data.Rules.ToDictionary(r => r.Id, r => r.NormalizeText ? Normalized(r.Contains) : "");
        _names = data.Rules.ToDictionary(r => r.Id, r => TransactionRecognition.RuleName(data, r));
    }

    private string Normalized(string text)
    {
        if (!_normalized.TryGetValue(text, out var value)) _normalized[text] = value = TransactionRecognition.Normalize(text);
        return value;
    }

    public bool Matches(CategorizationRule rule, Transaction transaction)
    {
        if (transaction.Kind == TransactionKind.Transfer || rule.AccountId is { } account && account != transaction.AccountId ||
            rule.Direction != PaymentDirection.Any && (int)rule.Direction != Math.Sign(transaction.Amount)) return false;
        if (!string.IsNullOrWhiteSpace(rule.Contains) && transaction.Description.Contains(rule.Contains, StringComparison.OrdinalIgnoreCase)) return true;
        if (rule.NormalizeText && (_texts.GetValueOrDefault(rule.Id) ?? Normalized(rule.Contains)) is { Length: > 0 } text &&
            (" " + Normalized(transaction.Description) + " ").Contains(" " + text + " ", StringComparison.Ordinal)) return true;
        return rule.RecurringPaymentId is { } payment && _known.TryGetValue(payment, out var examples) && examples.Contains(Normalized(transaction.Description));
    }

    public CategorizationRule? Category(Transaction transaction) => _data.Rules.FirstOrDefault(r => r.CategoryId != null && Matches(r, transaction));

    public IReadOnlyList<CategorizationRule> NamedMatches(Transaction transaction) => _data.Rules.Where(r => _names[r.Id].Length > 0 && Matches(r, transaction)).ToList();

    public bool Ambiguous(Transaction transaction) => transaction.RecurringPaymentId == null &&
        NamedMatches(transaction).Select(r => (r.RecurringPaymentId, Name: _names[r.Id].ToUpperInvariant())).Distinct().Skip(1).Any();

    public CategorizationRule? Identity(Transaction transaction)
    {
        if (transaction.RecurringPaymentId is { } payment)
            return _data.Rules.FirstOrDefault(r => r.RecurringPaymentId == payment);
        var matches = NamedMatches(transaction);
        return matches.Select(r => (r.RecurringPaymentId, Name: _names[r.Id].ToUpperInvariant())).Distinct().Count() == 1 ? matches[0] : null;
    }
}
