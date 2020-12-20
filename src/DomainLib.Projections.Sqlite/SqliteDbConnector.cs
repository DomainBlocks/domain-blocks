using DomainLib.Common;
using DomainLib.Projections.Sql;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Data.SQLite;

namespace DomainLib.Projections.Sqlite
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

            Connection = new SQLiteConnection(connectionString);
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
                    SQLiteParameter parameter;
                    if (columnDefinitions.TryGetValue(name, out var sqlColumnDefinition))
                    {
                        parameter = new SQLiteParameter($"@{sqlColumnDefinition.Name}", sqlColumnDefinition.DataType)
                            { Value = value };
                    }
                    else 
                    {
                        // Parameter may not be in column definitions if it is custom sql.
                        // In this case, we don't bind the DbTyp for the parameter
                        parameter = new SQLiteParameter($"@{name}")
                            { Value = value };
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
