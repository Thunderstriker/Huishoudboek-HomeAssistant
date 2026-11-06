using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Huishoudboek.Core;
using Microsoft.Data.Sqlite;

namespace Huishoudboek.Infrastructure;

/// <summary>
/// A versioned, single-writer aggregate store. Each entity has its own typed SQLite table.
/// Money is stored as invariant decimal TEXT, never floating point. A save is one atomic
/// transaction. The desktop holds a per-database process mutex for its lifetime.
/// </summary>
public sealed class SqliteHouseholdStore : IAdministrationStore
{
    private const int SchemaVersion = 10;
    private readonly string _path;
    public string DatabasePath => _path;

    public SqliteHouseholdStore(string path, bool allowCreate = true, bool allowUpgrade = true)
    {
        _path = Path.GetFullPath(path);
        if (!allowCreate && !File.Exists(_path))
            throw new InvalidDataException("De ingestelde database bestaat niet. Initialiseer de administratie eerst expliciet.");
        if (!allowCreate)
        {
            // Validate an adopted database without changing journal settings or touching an unsupported schema.
            using var existing = Open(_path, true);
            CheckDatabase(existing, allowPrevious: allowUpgrade);
            ReadData(existing, Version(existing));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var connection = Open(_path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            if (!allowCreate) throw new InvalidDataException("De ingestelde database is leeg; automatisch initialiseren is uitgeschakeld.");
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, "CREATE TABLE app_metadata (id INTEGER PRIMARY KEY CHECK(id=1), application TEXT NOT NULL, mortgage TEXT NULL, insights TEXT NULL)");
            Execute(connection, transaction, "INSERT INTO app_metadata(id,application) VALUES(1,'Huishoudboek')");
            foreach (var type in EntityTypes) CreateTable(connection, transaction, type);
            Execute(connection, transaction, "CREATE INDEX ix_transactions_date_account ON [Transaction] ([Date], [AccountId])");
            Execute(connection, transaction, $"PRAGMA user_version={SchemaVersion}");
            transaction.Commit();
            Save(HouseholdData.CreateEmpty());
        }
        else
        {
            CheckDatabase(connection, allowPrevious: allowUpgrade);
            if (Version(connection) < SchemaVersion) UpgradePreviousVersion(connection);
        }
    }

    private static readonly Type[] EntityTypes = [typeof(Owner), typeof(Account), typeof(Category),
        typeof(BudgetOverride), typeof(Transaction), typeof(RecurringPayment), typeof(CategorizationRule),
        typeof(InvestmentValuation), typeof(ImportBatch), typeof(ImportProfile), typeof(TransferMatch),
        typeof(TransactionAllocation), typeof(ReimbursementClaim), typeof(AccountReview)];

    private static SqliteConnection Open(string path, bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 10
        }.ToString());
        connection.Open();
        if (!readOnly)
        {
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
            pragma.ExecuteNonQuery();
        }
        return connection;
    }

    private static void Execute(SqliteConnection c, SqliteTransaction tx, string sql)
    {
        using var command = c.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int Version(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void CheckDatabase(SqliteConnection connection, bool allowPrevious = false)
    {
        using var command = connection.CreateCommand();
        var version = Version(connection);
        if (version != SchemaVersion && !(allowPrevious && version is >= 1 and < SchemaVersion))
            throw new InvalidDataException("Deze databaseversie wordt niet ondersteund. Gebruik de bijbehorende appversie.");
        command.CommandText = "SELECT application FROM app_metadata WHERE id=1";
        if (!Equals(command.ExecuteScalar(), "Huishoudboek"))
            throw new InvalidDataException("Dit is geen Huishoudboek-database.");
        command.CommandText = "PRAGMA quick_check";
        if (!Equals(command.ExecuteScalar(), "ok")) throw new InvalidDataException("De SQLite-integriteitscontrole is mislukt.");
    }

    private void UpgradePreviousVersion(SqliteConnection connection)
    {
        // Validate first, then take a SQLite-aware backup (including WAL) before any schema change.
        var version = Version(connection);
        ReadData(connection, version);
        Backup(_path + $".voor-versie-{SchemaVersion}-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.db");
        using var tx = connection.BeginTransaction();
        if (version == 1)
        {
            Execute(connection, tx, "ALTER TABLE [ImportProfile] ADD COLUMN [CounterAccountReferenceColumn] TEXT NOT NULL DEFAULT ''");
            Execute(connection, tx, "UPDATE [ImportProfile] SET [CounterAccountReferenceColumn]='Rekening tegenpartij' WHERE [AccountReferenceColumn]='Rekening opdrachtgever'");
        }
        if (version < 3) Execute(connection, tx, "ALTER TABLE [Transaction] ADD COLUMN [UserDescription] TEXT NOT NULL DEFAULT ''");
        if (version < 4) CreateTable(connection, tx, typeof(TransferMatch));
        if (version < 5)
        {
            Execute(connection, tx, "ALTER TABLE [Account] ADD COLUMN [MinimumCashBuffer] TEXT NOT NULL DEFAULT '0'");
            Execute(connection, tx, "ALTER TABLE [Transaction] ADD COLUMN [RecurringOccurrenceDate] TEXT NULL");
            Execute(connection, tx, "ALTER TABLE [RecurringPayment] ADD COLUMN [Frequency] INTEGER NOT NULL DEFAULT 0");
            Execute(connection, tx, "ALTER TABLE [RecurringPayment] ADD COLUMN [MatchTolerance] TEXT NOT NULL DEFAULT '0.01'");
            Execute(connection, tx, "ALTER TABLE [RecurringPayment] ADD COLUMN [MatchWindowDays] INTEGER NOT NULL DEFAULT 5");
        }
        if (version < 6)
        {
            CreateTable(connection, tx, typeof(TransactionAllocation));
            CreateTable(connection, tx, typeof(ReimbursementClaim));
        }
        if (version < 7) CreateTable(connection, tx, typeof(AccountReview));
        if (version < 8) Execute(connection, tx, "ALTER TABLE [Category] ADD COLUMN [Purpose] INTEGER NOT NULL DEFAULT 0");
        if (version < 9)
        {
            Execute(connection, tx, "ALTER TABLE [RecurringPayment] ADD COLUMN [AmountMode] INTEGER NOT NULL DEFAULT 0");
            Execute(connection, tx, "ALTER TABLE [ImportBatch] ADD COLUMN [CoverageFrom] TEXT NULL");
            Execute(connection, tx, "ALTER TABLE [ImportBatch] ADD COLUMN [CoverageThrough] TEXT NULL");
        }
        Execute(connection, tx, "ALTER TABLE [Category] ADD COLUMN [IsEssential] INTEGER NOT NULL DEFAULT 0");
        Execute(connection, tx, "ALTER TABLE [Category] ADD COLUMN [IsExceptional] INTEGER NOT NULL DEFAULT 0");
        Execute(connection, tx, "ALTER TABLE [Account] ADD COLUMN [IncludeInFreedom] INTEGER NOT NULL DEFAULT 1");
        Execute(connection, tx, "ALTER TABLE [Account] ADD COLUMN [EarmarkedAmount] TEXT NOT NULL DEFAULT '0'");
        Execute(connection, tx, "ALTER TABLE app_metadata ADD COLUMN insights TEXT NULL");
        Execute(connection, tx, $"PRAGMA user_version={SchemaVersion}");
        tx.Commit();
        CheckDatabase(connection);
    }

    private static void CreateTable(SqliteConnection c, SqliteTransaction tx, Type type)
    {
        var columns = type.GetProperties().Select(p => $"[{p.Name}] {(IsInteger(p.PropertyType) ? "INTEGER" : "TEXT")}{(p.Name == "Id" ? " PRIMARY KEY NOT NULL" : "")}");
        Execute(c, tx, $"CREATE TABLE [{type.Name}] ({string.Join(",", columns)})");
    }

    private static bool IsInteger(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(int) || type == typeof(bool) || type.IsEnum;
    }

    public HouseholdData Load()
    {
        using var connection = Open(_path, true);
        CheckDatabase(connection);
        return ReadData(connection);
    }

    private static HouseholdData ReadData(SqliteConnection connection, int version = SchemaVersion)
    {
        using var tx = connection.BeginTransaction(deferred: true);
        var data = new HouseholdData
        {
            Owners = Read<Owner>(connection, tx), Accounts = Read<Account>(connection, tx, version),
            Categories = Read<Category>(connection, tx, version), BudgetOverrides = Read<BudgetOverride>(connection, tx),
            Transactions = Read<Transaction>(connection, tx, version), RecurringPayments = Read<RecurringPayment>(connection, tx, version),
            Rules = Read<CategorizationRule>(connection, tx), Valuations = Read<InvestmentValuation>(connection, tx),
            ImportBatches = Read<ImportBatch>(connection, tx, version), ImportProfiles = Read<ImportProfile>(connection, tx, version),
            TransferMatches = version >= 4 ? Read<TransferMatch>(connection, tx) : [],
            TransactionAllocations = version >= 6 ? Read<TransactionAllocation>(connection, tx) : [],
            ReimbursementClaims = version >= 6 ? Read<ReimbursementClaim>(connection, tx) : [],
            AccountReviews = version >= 7 ? Read<AccountReview>(connection, tx) : []
        };
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = version >= 10 ? "SELECT mortgage, insights FROM app_metadata WHERE id=1" : "SELECT mortgage FROM app_metadata WHERE id=1";
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                if (!reader.IsDBNull(0)) data.Mortgage = JsonSerializer.Deserialize<Mortgage>(reader.GetString(0));
                if (version >= 10 && !reader.IsDBNull(1))
                    data.InsightSettings = JsonSerializer.Deserialize<InsightSettings>(reader.GetString(1)) ?? new();
            }
        }
        tx.Commit();
        Validation.Validate(data);
        return data;
    }

    public void Save(HouseholdData data)
    {
        Validation.Validate(data);
        using var connection = Open(_path);
        using var tx = connection.BeginTransaction();
        Write(connection, tx, data.Owners); Write(connection, tx, data.Accounts);
        Write(connection, tx, data.Categories); Write(connection, tx, data.BudgetOverrides);
        Write(connection, tx, data.Transactions); Write(connection, tx, data.RecurringPayments);
        Write(connection, tx, data.Rules); Write(connection, tx, data.Valuations);
        Write(connection, tx, data.ImportBatches); Write(connection, tx, data.ImportProfiles);
        Write(connection, tx, data.TransferMatches);
        Write(connection, tx, data.TransactionAllocations); Write(connection, tx, data.ReimbursementClaims);
        Write(connection, tx, data.AccountReviews);
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "UPDATE app_metadata SET mortgage=$mortgage, insights=$insights WHERE id=1";
        command.Parameters.AddWithValue("$mortgage", data.Mortgage == null ? DBNull.Value : JsonSerializer.Serialize(data.Mortgage));
        command.Parameters.AddWithValue("$insights", JsonSerializer.Serialize(data.InsightSettings));
        command.ExecuteNonQuery();
        tx.Commit();
    }

    private static List<T> Read<T>(SqliteConnection c, SqliteTransaction tx, int version = SchemaVersion) where T : new()
    {
        using var command = c.CreateCommand();
        command.Transaction = tx;
        var properties = typeof(T).GetProperties();
        command.CommandText = $"SELECT {string.Join(",", properties.Select(p =>
            PreviousDefault(typeof(T), p.Name, version) is { } fallback ? $"{fallback} AS [{p.Name}]" : $"[{p.Name}]"))} FROM [{typeof(T).Name}]";
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read())
        {
            var item = new T();
            for (var i = 0; i < properties.Length; i++)
            {
                var p = properties[i];
                if (reader.IsDBNull(i)) { p.SetValue(item, null); continue; }
                var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                object value = type == typeof(string) ? reader.GetString(i) :
                    type == typeof(Guid) ? Guid.Parse(reader.GetString(i)) :
                    type == typeof(DateOnly) ? DateOnly.ParseExact(reader.GetString(i), "yyyy-MM-dd", CultureInfo.InvariantCulture) :
                    type == typeof(DateTime) ? DateTime.Parse(reader.GetString(i), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) :
                    type == typeof(decimal) ? decimal.Parse(reader.GetString(i), CultureInfo.InvariantCulture) :
                    type == typeof(bool) ? reader.GetInt64(i) != 0 :
                    type == typeof(int) ? reader.GetInt32(i) :
                    type.IsEnum ? Enum.ToObject(type, reader.GetInt32(i)) :
                    type == typeof(List<Guid>) ? JsonSerializer.Deserialize<List<Guid>>(reader.GetString(i))! :
                    throw new NotSupportedException(type.Name);
                p.SetValue(item, value);
            }
            result.Add(item);
        }
        return result;
    }

    private static string? PreviousDefault(Type type, string property, int version)
    {
        if (version < 10 && type == typeof(Category) && property is nameof(Category.IsEssential) or nameof(Category.IsExceptional)) return "0";
        if (version < 10 && type == typeof(Account) && property == nameof(Account.IncludeInFreedom)) return "1";
        if (version < 10 && type == typeof(Account) && property == nameof(Account.EarmarkedAmount)) return "'0'";
        if (version < 9 && type == typeof(RecurringPayment) && property == nameof(RecurringPayment.AmountMode)) return "0";
        if (version < 9 && type == typeof(ImportBatch) && property is nameof(ImportBatch.CoverageFrom) or nameof(ImportBatch.CoverageThrough)) return "NULL";
        if (version < 8 && type == typeof(Category) && property == nameof(Category.Purpose)) return "0";
        if (version == 1 && type == typeof(ImportProfile) && property == nameof(ImportProfile.CounterAccountReferenceColumn) ||
            version < 3 && type == typeof(Transaction) && property == nameof(Transaction.UserDescription)) return "''";
        if (version >= 5) return null;
        if (type == typeof(Account) && property == nameof(Account.MinimumCashBuffer)) return "'0'";
        if (type == typeof(Transaction) && property == nameof(Transaction.RecurringOccurrenceDate)) return "NULL";
        if (type == typeof(RecurringPayment)) return property switch
        {
            nameof(RecurringPayment.Frequency) => "0", nameof(RecurringPayment.MatchTolerance) => "'0.01'",
            nameof(RecurringPayment.MatchWindowDays) => "5", _ => null
        };
        return null;
    }

    private static void Write<T>(SqliteConnection c, SqliteTransaction tx, IEnumerable<T> items)
    {
        Execute(c, tx, $"DELETE FROM [{typeof(T).Name}]");
        var properties = typeof(T).GetProperties();
        using var command = c.CreateCommand();
        command.Transaction = tx;
        command.CommandText = $"INSERT INTO [{typeof(T).Name}] ({string.Join(",", properties.Select(p => $"[{p.Name}]"))}) VALUES ({string.Join(",", properties.Select((_, i) => $"$p{i}"))})";
        for (var i = 0; i < properties.Length; i++) command.Parameters.Add(new SqliteParameter($"$p{i}", DBNull.Value));
        command.Prepare();
        foreach (var item in items)
        {
            for (var i = 0; i < properties.Length; i++)
            {
                var value = properties[i].GetValue(item);
                command.Parameters[i].Value = value switch
                {
                    null => DBNull.Value,
                    bool boolean => boolean ? 1 : 0,
                    Enum enumeration => Convert.ToInt32(enumeration, CultureInfo.InvariantCulture),
                    int number => number,
                    decimal money => money.ToString(CultureInfo.InvariantCulture),
                    DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
                    List<Guid> ids => JsonSerializer.Serialize(ids),
                    _ => value.ToString()!
                };
            }
            command.ExecuteNonQuery();
        }
    }

    private void CheckDifferentPath(string other)
    {
        if (string.Equals(_path, Path.GetFullPath(other), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Kies een ander bestand dan de actieve database.");
    }

    public void Backup(string destination)
    {
        CheckDifferentPath(destination);
        using var source = Open(_path, true);
        using var target = Open(destination);
        source.BackupDatabase(target);
    }

    public void Restore(string source)
    {
        CheckDifferentPath(source);
        Save(ReadBackup(source));
    }

    public static HouseholdData ReadBackup(string source)
    {
        // Validate ALL entity references before touching the active database.
        using var candidate = Open(source, true);
        CheckDatabase(candidate, allowPrevious: true);
        var version = Version(candidate);
        var data = ReadData(candidate, version);
        if (version == 1)
            foreach (var profile in data.ImportProfiles.Where(p => p.AccountReferenceColumn == "Rekening opdrachtgever"))
                profile.CounterAccountReferenceColumn = "Rekening tegenpartij";
        return data;
    }

    public static void ValidateBackup(string source)
    {
        ReadBackup(source);
    }
}
