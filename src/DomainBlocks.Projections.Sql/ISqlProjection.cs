namespace DomainBlocks.Projections.Sql
{
    public interface ISqlProjection
    {
        IDbConnector DbConnector { get; }
        ISqlDialect SqlDialect { get; }
        string CustomCreateTableSql => string.Empty;
        string AfterCreateTableSql => string.Empty;
        string TableName { get; }
        SqlColumnDefinitions Columns { get; }
    }
}