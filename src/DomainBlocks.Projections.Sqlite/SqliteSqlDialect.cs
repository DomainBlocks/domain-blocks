using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using DomainBlocks.Projections.Sql;

namespace DomainBlocks.Projections.Sqlite;

public sealed class SqliteSqlDialect : ISqlDialect
{
    private static readonly string SqlValueSeparator = $", {Environment.NewLine}";
    private static readonly string SqlPredicateSeparator = $" AND{Environment.NewLine}";

    public string DialectKey { get; } = "Sqlite3";

    public string BuildCreateTableSql(string tableName, IEnumerable<SqlColumnDefinition> columnDefinitions)
    {
        var columnStrings = new List<string>();

        foreach (var column in columnDefinitions)
        {
            var nullableText = column.IsNullable ? "NULL" : "NOT NULL";
            var primaryKeyText = column.IsInPrimaryKey ? "PRIMARY KEY" : string.Empty;
            var defaultText = string.IsNullOrWhiteSpace(column.Default)
                ? string.Empty
                : $"DEFAULT {column.Default}";

            var columnString = string.Join(" ",
                column.Name,
                GetDataTypeName(column.DataType),
                nullableText,
                primaryKeyText,
                defaultText);

            columnStrings.Add(columnString);
        }

        var columnsText = string.Join(SqlValueSeparator, columnStrings);


        var createTableSql = $@"
CREATE TABLE IF NOT EXISTS {tableName} (
{columnsText}
);
";
        return RemoveUnnecessarySpaces(createTableSql);
    }

    public string BuildUpsertCommandText(string tableName, SqlColumnDefinitions eventPropertyMap)
    {
        var columns = string.Join(SqlValueSeparator, eventPropertyMap.Select(x => x.Value.Name));
        var parameterNames = string.Join(SqlValueSeparator, eventPropertyMap.Select(x => $"@{x.Value.Name}"));

        var commandText = $@"
INSERT OR REPLACE INTO {tableName} (
{columns}
)
VALUES (
{parameterNames}
);
";
        return RemoveUnnecessarySpaces(commandText);
    }

    public string BuildDeleteCommandText(string tableName, SqlColumnDefinitions eventPropertyMap)
    {
        var primaryKeyColumns = eventPropertyMap.Where(x => x.Value.IsInPrimaryKey)
            .Select(x => $"{x.Value.Name} = @{x.Value.Name}");

        var primaryKeysSql = string.Join(SqlPredicateSeparator, primaryKeyColumns);

        var commandText = $@"
DELETE FROM {tableName}
WHERE
{primaryKeysSql}
;
";
        return RemoveUnnecessarySpaces(commandText);
    }

    private string RemoveUnnecessarySpaces(string input)
    {
        return input.Replace("  ", " ")
            .Replace(" , ", ", ")
            .Replace($" {Environment.NewLine}", Environment.NewLine);

    }

    private static string GetDataTypeName(DbType dbType)
    {
        switch (dbType)
        {
            case DbType.Double:
            case DbType.Single:
            case DbType.VarNumeric:
                return "NUMERIC";
            case DbType.Binary:
            case DbType.Boolean:
            case DbType.Byte:
            case DbType.Int16:
            case DbType.Int32:
            case DbType.Int64:
            case DbType.SByte:
            case DbType.UInt16:
            case DbType.UInt32:
            case DbType.UInt64:
                return "INTEGER";
            case DbType.Object:
                return "BLOB";
            case DbType.AnsiString:
            case DbType.AnsiStringFixedLength:
            case DbType.Currency:
            case DbType.Date:
            case DbType.DateTime:
            case DbType.DateTime2:
            case DbType.DateTimeOffset:
            case DbType.Decimal:
            case DbType.Guid:
            case DbType.String:
            case DbType.StringFixedLength:
            case DbType.Time:
            case DbType.Xml:
                return "TEXT";
            default:
                throw new ArgumentOutOfRangeException(nameof(dbType), dbType, null);
        }
    }
}