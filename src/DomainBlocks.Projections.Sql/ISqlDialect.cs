using System.Collections.Generic;

namespace DomainBlocks.Projections.Sql;

public interface ISqlDialect
{
    string DialectKey { get; }
    string BuildCreateTableSql(string tableName, IEnumerable<SqlColumnDefinition> columnDefinitions);
    string BuildUpsertCommandText(string tableName, SqlColumnDefinitions eventPropertyMap);
    string BuildDeleteCommandText(string tableName, SqlColumnDefinitions eventPropertyMap);
}