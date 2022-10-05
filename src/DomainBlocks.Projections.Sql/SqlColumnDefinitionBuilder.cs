using System;
using System.Data;

namespace DomainBlocks.Projections.Sql;

public sealed class SqlColumnDefinitionBuilder
{
    private string _name;
    private DbType _dataType = DbType.String;
    private bool _isInPrimaryKey;
    private bool _isNullable = true;
    private string _default;

    public SqlColumnDefinitionBuilder Name(string name)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        return this;
    }

    public SqlColumnDefinitionBuilder Type(DbType dbType)
    {
        _dataType = dbType;
        return this;
    }

    public SqlColumnDefinitionBuilder NotNull()
    {
        _isNullable = false;
        return this;
    }

    public SqlColumnDefinitionBuilder Null()
    {
        _isNullable = true;
        return this;
    }

    public SqlColumnDefinitionBuilder PrimaryKey()
    {
        _isInPrimaryKey = true;
        _isNullable = false;
        return this;
    }

    public SqlColumnDefinitionBuilder Default(string defaultValue)
    {
        _default = defaultValue;
        return this;
    }

    public SqlColumnDefinitionBuilder Default<T>(T defaultValue)
    {
        _default = defaultValue.ToString();
        return this;
    }

    public SqlColumnDefinition Build()
    {
        if (string.IsNullOrEmpty(_name))
        {
            throw new InvalidOperationException("Name must be supplied for a SQL Column");
        }

        return new SqlColumnDefinition(_name, _dataType, _isInPrimaryKey, _isNullable, _default);
    }
}