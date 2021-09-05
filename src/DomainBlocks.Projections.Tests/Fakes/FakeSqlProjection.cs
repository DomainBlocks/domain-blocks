using System.Data;
using DomainBlocks.Projections.Sqlite;

namespace DomainBlocks.Projections.Sql.Tests.Fakes
{
    public class FakeSqlProjection : ISqlProjection
    {
        public IDbConnector DbConnector { get; }
        public ISqlDialect SqlDialect { get; } = new SqliteSqlDialect();
        public string TableName { get; } = "MyTable";

        public FakeSqlProjection(SqlContextSettings sqlContextSettings)
        {
            DbConnector = new FakeDbConnector(sqlContextSettings);
        }

        public SqlColumnDefinitions Columns { get; } = new()
        {
            {"Col1", new SqlColumnDefinitionBuilder().Name("Col1").Type(DbType.Int32).PrimaryKey().Build()},
            {"Col2", new SqlColumnDefinitionBuilder().Name("Col2").Type(DbType.Int32).Build()},
            {"Col3", new SqlColumnDefinitionBuilder().Name("Col3").Type(DbType.String).Build()},
        };
    }
}