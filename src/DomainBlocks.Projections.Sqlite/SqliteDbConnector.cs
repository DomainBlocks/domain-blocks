using DomainBlocks.Common;
using DomainBlocks.Projections.Sql;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace DomainBlocks.Projections.Sqlite
{
    public sealed class SqliteDbConnector : IDbConnector
    {
        public static readonly ILogger<SqliteDbConnector> Log = Logger.CreateFor<SqliteDbConnector>();

        public SqliteDbConnector(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(connectionString));
            }

            Connection = new SqliteConnection(connectionString);
        }

        public IDbConnection Connection { get; }
        
        public void BindParameters<TEvent>(IDbCommand command,
                                           TEvent @event,
                                           SqlColumnDefinitions columnDefinitions,
                                           ISqlParameterBindingMap<TEvent> parameterBindingMap)
        {
            try
            {
                foreach (var (name, value) in parameterBindingMap.GetParameterNamesAndValues(@event))
                {
                    // Note that we don't need to set DB types here. This is because of SQLite's dynamic type system.
                    // See: https://docs.microsoft.com/en-us/dotnet/standard/data/sqlite/types#column-types
                    // There is also an open issue where some types are not mapped correctly.
                    // See: https://github.com/dotnet/efcore/issues/27213, https://github.com/dotnet/efcore/pull/27291
                    SqliteParameter parameter;
                    if (columnDefinitions.TryGetValue(name, out var sqlColumnDefinition))
                    {
                        parameter = new SqliteParameter($"@{sqlColumnDefinition.Name}", value);
                    }
                    else
                    {
                        // Parameter may not be in column definitions if it is custom sql.
                        // In this case, we don't bind the DbTyp for the parameter
                        parameter = new SqliteParameter($"@{name}", value);
                    }
     
                    command.Parameters.Add(parameter);
                }
            }
            catch (Exception ex)
            {
                Log.LogCritical(ex,
                                "Unknown exception occurred while trying to bind parameters for event {EventName} {Event}",
                                @event.GetType().FullName,
                                @event);
                throw;
            }
        }
        
        
    }
}
