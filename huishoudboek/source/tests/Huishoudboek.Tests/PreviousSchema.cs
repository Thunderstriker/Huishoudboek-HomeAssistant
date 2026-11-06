namespace Huishoudboek.Tests;

internal static class PreviousSchema
{
    public const string RemoveSchema11Tables = "DROP TABLE [AccountStatementPeriod];";
    public const string RemoveSchema10Columns = RemoveSchema11Tables + "ALTER TABLE [Category] DROP COLUMN [IsEssential]; ALTER TABLE [Category] DROP COLUMN [IsExceptional];" +
        "ALTER TABLE [Account] DROP COLUMN [IncludeInFreedom]; ALTER TABLE [Account] DROP COLUMN [EarmarkedAmount]; ALTER TABLE app_metadata DROP COLUMN insights;";
    // Older fixtures must remove all columns introduced since their target schema.
    public const string RemoveSchema9Columns = RemoveSchema10Columns + "ALTER TABLE [RecurringPayment] DROP COLUMN [AmountMode];" +
        "ALTER TABLE [ImportBatch] DROP COLUMN [CoverageFrom]; ALTER TABLE [ImportBatch] DROP COLUMN [CoverageThrough];";
}
