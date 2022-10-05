using System;
using System.Data;

namespace DomainBlocks.Projections.Sql;

public sealed class SqlColumnDefinition
{
    internal SqlColumnDefinition(string name,
        DbType dataType,
        bool isInPrimaryKey,
        bool isNullable,
        string @default)
    {
        if (isInPrimaryKey && isNullable)
        {
            throw new ArgumentException("Column must not be nullable if it is in the primary key");
        }

        Name = name ?? throw new ArgumentNullException(nameof(name));
        DataType = dataType;
        IsInPrimaryKey = isInPrimaryKey;
        IsNullable = isNullable;
        Default = @default;
    }

    public string Name { get; }
    public DbType DataType { get; }
    public bool IsInPrimaryKey { get; }
    public bool IsNullable { get; }
    public string Default { get; }
}