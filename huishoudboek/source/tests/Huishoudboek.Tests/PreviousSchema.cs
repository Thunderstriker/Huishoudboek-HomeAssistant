namespace Huishoudboek.Tests;

internal static class PreviousSchema
{
    public const string RemoveSchema9Columns = "ALTER TABLE [RecurringPayment] DROP COLUMN [AmountMode];" +
        "ALTER TABLE [ImportBatch] DROP COLUMN [CoverageFrom]; ALTER TABLE [ImportBatch] DROP COLUMN [CoverageThrough];";
}
