using System.Text.Json;
using Huishoudboek.Core;
using Microsoft.AspNetCore.DataProtection;

namespace Huishoudboek.Service;

/// <summary>Installation-local runtime data, deliberately outside the financial database and its restore path.</summary>
public sealed class InvestmentMarketStore
{
    private readonly object _gate = new();
    private readonly string _directory, _path;
    private readonly IDataProtector _protector;
    private MarketState _state;
    private bool _preserveInvalidFile;

    public sealed class MarketState
    {
        public InvestmentQuoteSettings Settings { get; set; } = new();
        public string ProtectedKey { get; set; } = "";
        public List<InvestmentQuote> Quotes { get; set; } = [];
        public List<InvestmentFxRate> Rates { get; set; } = [];
        public DateOnly BudgetDayUtc { get; set; }
        public int RequestsToday { get; set; }
        public List<DateTime> RecentRequestsUtc { get; set; } = [];
        public DateTime? LastAttemptUtc { get; set; }
        public DateTime? LastSuccessUtc { get; set; }
        public DateTime? NextRefreshUtc { get; set; }
        public InvestmentProviderLimit? ProviderLimit { get; set; }
        // Codes, never raw provider messages, URLs, keys or exception text.
        public string Status { get; set; } = "NotConfigured";
        public InvestmentMarketReport? Report { get; set; }
    }

    public InvestmentMarketStore(string administrationDirectory)
    {
        _directory = Path.Combine(administrationDirectory, "market-data");
        Directory.CreateDirectory(_directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _path = Path.Combine(_directory, "state.json");
        var keys = Path.Combine(_directory, "keys");
        Directory.CreateDirectory(keys);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _protector = DataProtectionProvider.Create(new DirectoryInfo(keys), builder =>
        {
            builder.SetApplicationName("Huishoudboek.InvestmentKey.v1");
            if (OperatingSystem.IsWindows()) builder.ProtectKeysWithDpapi();
        })
            .CreateProtector("AlphaVantage");
        try
        {
            _state = File.Exists(_path) ? JsonSerializer.Deserialize<MarketState>(File.ReadAllText(_path))
                ?? throw new InvalidOperationException() : new();
            _state.Settings.Validate();
            if (_state.ProtectedKey == null || _state.Quotes == null || _state.Rates == null || _state.RecentRequestsUtc == null || _state.RequestsToday < 0)
                throw new InvalidOperationException();
            if (_state.Report is { } storedReport && (storedReport.Requests == null || storedReport.Requests.Any(r => r == null)))
                throw new InvalidOperationException();
            // Legacy BlockedUntilUtc is deliberately ignored: a guessed 24-hour lock must not survive the upgrade.
            if (_state.Status == "ProviderLimit" && _state.ProviderLimit == null && _state.LastAttemptUtc is { } attempt)
                _state.ProviderLimit = new(attempt, attempt.AddMinutes(1), false);
            // A restart must not leave the previous action looking as if it is still running.
            if (_state.Report is { CompletedUtc: null } report)
                _state.Report = report with { CompletedUtc = DateTime.UtcNow, Status = "Interrupted", ErrorCode = "Interrupted",
                    Requests = report.Requests.Select(r => r.Status is "Pending" or "Requesting" ? r with { Status = "NotRequested" } : r).ToArray() };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NullReferenceException)
        {
            // Fail closed without taking the entire household app offline or silently granting a new quota.
            // Preserve the damaged file on the next explicit write, for local recovery.
            _state = new() { Status = "ConfigurationInvalid", BudgetDayUtc = DateOnly.FromDateTime(DateTime.UtcNow), RequestsToday = 25 };
            _preserveInvalidFile = true;
        }
    }

    public T Read<T>(Func<MarketState, T> read)
    {
        lock (_gate) return read(Clone(_state));
    }

    public void Update(Action<MarketState> update)
    {
        lock (_gate)
        {
            var candidate = Clone(_state);
            update(candidate);
            var temporary = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(temporary, options))
                { JsonSerializer.Serialize(stream, candidate); stream.Flush(flushToDisk: true); }
                if (_preserveInvalidFile && File.Exists(_path))
                    File.Copy(_path, Path.Combine(_directory, "state.invalid-" + Guid.NewGuid().ToString("N") + ".json"));
                CommitFile(temporary, _path);
                _state = candidate;
                _preserveInvalidFile = false;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private static void CommitFile(string temporary, string target)
    {
        // Windows scanners can briefly hold a just-written file without delete sharing.
        // Retry only this atomic LOCAL rename, never an outbound API request or a quota reservation.
        for (var attempt = 0; ; attempt++)
        {
            try { File.Move(temporary, target, overwrite: true); return; }
            catch (Exception ex) when (OperatingSystem.IsWindows() && attempt < 5 && ex is IOException or UnauthorizedAccessException)
            { Thread.Sleep(25 * (attempt + 1)); }
        }
    }

    public string Protect(string key) => _protector.Protect(key);
    public string Key()
    {
        try { return Read(s => s.ProtectedKey.Length == 0 ? "" : _protector.Unprotect(s.ProtectedKey)); }
        catch (System.Security.Cryptography.CryptographicException) { throw new MarketDataException("KeyUnavailable"); }
    }
    private static MarketState Clone(MarketState state) => JsonSerializer.Deserialize<MarketState>(JsonSerializer.SerializeToUtf8Bytes(state))!;

    public InvestmentMarketSnapshot Snapshot(DateTime now) => Read(s => new InvestmentMarketSnapshot(s.Settings, s.ProtectedKey.Length > 0,
        s.Quotes.ToArray(), s.Rates.ToArray(), s.BudgetDayUtc == DateOnly.FromDateTime(now) ? s.RequestsToday : 0,
        s.LastAttemptUtc, s.LastSuccessUtc, s.NextRefreshUtc, s.Status)
        {
            Report = s.Report, ProviderLimit = s.ProviderLimit,
            DailyBudgetResetUtc = s.BudgetDayUtc == DateOnly.FromDateTime(now) && s.Settings.DailyLimit > 0 && s.RequestsToday >= s.Settings.DailyLimit
                ? now.Date.AddDays(1) : null
        });
}

public sealed class MarketDataException(string code, int? httpStatusCode = null, DateTime? retryAfterUtc = null) : Exception
{
    public string Code { get; } = code;
    public int? HttpStatusCode { get; } = httpStatusCode;
    public DateTime? RetryAfterUtc { get; } = retryAfterUtc;
}
