using System;
using System.Collections.Concurrent;

namespace DomainBlocks.Projections.Sql;

public static class SqlContextProvider
{
    private static readonly ConcurrentDictionary<SqlContextKey, SqlProjectionContext> SqlContexts = new();

    public static SqlProjectionContext GetOrCreateContext(IDbConnector connector, ISqlDialect sqlDialect)
    {
        return SqlContexts.GetOrAdd(new SqlContextKey(connector, sqlDialect),
            key => new SqlProjectionContext(key.DbConnector, key.SqlDialect));
    }

    private readonly struct SqlContextKey : IEquatable<SqlContextKey>
    {
        private readonly string _sqlDialectKey;

        public SqlContextKey(IDbConnector dbConnector, ISqlDialect sqlDialect)
        {
            DbConnector = dbConnector;
            SqlDialect = sqlDialect;
            _sqlDialectKey = sqlDialect.DialectKey;
        }

        public IDbConnector DbConnector { get; }
        public ISqlDialect SqlDialect { get; }

        public bool Equals(SqlContextKey other)
        {
            return _sqlDialectKey == other._sqlDialectKey && Equals(DbConnector, other.DbConnector);
        }

        public override bool Equals(object obj)
        {
            return obj is SqlContextKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_sqlDialectKey, DbConnector);
        }

        public static bool operator ==(SqlContextKey left, SqlContextKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SqlContextKey left, SqlContextKey right)
        {
            return !left.Equals(right);
        }
    }
}