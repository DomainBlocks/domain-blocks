using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using DomainBlocks.Common;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.Projections.Sql
{
    public sealed class SqlProjectionBuilder<TEvent, TSqlProjection> : IProjectionBuilder
        where TSqlProjection : ISqlProjection
    {
        public static readonly ILogger<SqlProjectionBuilder<TEvent, TSqlProjection>> Log =
            Logger.CreateFor<SqlProjectionBuilder<TEvent, TSqlProjection>>();
        private readonly TSqlProjection _sqlProjection;
        private readonly IDbConnector _connector;
        private readonly ISqlDialect _sqlDialect;
        private readonly SqlProjectionContext _projectionContext;
        private bool _executesUpsert;
        private bool _executesDelete;
        private string _customSqlCommandText;

        private ISqlParameterBindingMap<TEvent> _parameterBindingMap;

        public SqlProjectionBuilder(EventProjectionBuilder<TEvent> builder, TSqlProjection sqlProjection) 
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            _sqlProjection = sqlProjection ?? throw new ArgumentNullException(nameof(sqlProjection));
            _connector = sqlProjection.DbConnector;
            _sqlDialect = sqlProjection.SqlDialect;
            _projectionContext = SqlContextProvider.GetOrCreateContext(sqlProjection.DbConnector, sqlProjection.SqlDialect);
            _projectionContext.RegisterProjection(_sqlProjection);
            builder.RegisterProjectionBuilder(this);
            builder.RegisterContextForEvent(_projectionContext);
            _parameterBindingMap = CreateReflectedParameterBindingMap();
        }

        public SqlProjectionBuilder<TEvent, TSqlProjection> ExecutesUpsert()
        {
            _executesUpsert = true;
            return this;
        }

        public SqlProjectionBuilder<TEvent, TSqlProjection> ExecutesDelete()
        {
            _executesDelete = true;
            return this;
        }

        public SqlProjectionBuilder<TEvent, TSqlProjection> ExecutesCustomSql(string sqlCommand)
        {
            _customSqlCommandText = sqlCommand;
            return this;
        }

        public SqlProjectionBuilder<TEvent, TSqlProjection> ParameterMappings(
            params (string parameterName, Func<TEvent, object> getParameterValue)[] mappings)
        {
            Dictionary<string, Func<TEvent, object>> mappingsDictionary;
            try
            {
                mappingsDictionary = mappings.ToDictionary(x => x.parameterName, x => x.getParameterValue);
            }
            catch (InvalidOperationException ex)
            {
                Log.LogError(ex, "Unable to create parameter binding map for parameter {parameterName}. " +
                                 "Check you haven't mapped the same parameter more than once");
                throw;
            }
            
            _parameterBindingMap = new ParameterBindingMap<TEvent>(mappingsDictionary);
            return this;
        }

        public SqlProjectionBuilder<TEvent, TSqlProjection> CustomParameterBindings(
            GetParameterBindings<TEvent> getBindings)
        {
            _parameterBindingMap = new ParameterBindingMap<TEvent>(getBindings);
            return this;
        }

        IEnumerable<(Type eventType, Type projectionType, RunProjection func)> IProjectionBuilder.BuildProjections()
        {
            return EnumerableEx.Return((typeof(TEvent), typeof(TSqlProjection), BuildDbCommandProjection()));
        }

        private RunProjection BuildDbCommandProjection()
        {
            if (_executesUpsert && _executesDelete)
            {
                throw new InvalidOperationException("An event cannot perform both an upsert " +
                                                    "and a delete on the same projection.");
            }

            var commandTextBuilder = new StringBuilder();
            var columnsInGeneratedSql =
                new SqlColumnDefinitions(_sqlProjection.Columns.Where(x => _parameterBindingMap.GetParameterNames()
                                                                          .Contains(x.Key)));


            if (_executesUpsert)
            {
                commandTextBuilder.Append(_sqlDialect.BuildUpsertCommandText(_sqlProjection.TableName,
                                                                             columnsInGeneratedSql));
                commandTextBuilder.Append(" ");
            }

            if (_executesDelete)
            {
                ValidateDeleteCommand();
                commandTextBuilder.Append(_sqlDialect.BuildDeleteCommandText(_sqlProjection.TableName,
                                                                             columnsInGeneratedSql));
                commandTextBuilder.Append(" ");
            }

            if (!string.IsNullOrWhiteSpace(_customSqlCommandText))
            {
                commandTextBuilder.Append(_customSqlCommandText);
            }

            var dbCommand = _projectionContext.Connection.CreateCommand();
            dbCommand.CommandText = commandTextBuilder.ToString();

            return async @event =>
            {
                _connector.BindParameters(dbCommand, (TEvent) @event, _sqlProjection.Columns, _parameterBindingMap);

                if (Log.IsEnabled(LogLevel.Trace))
                {
                    Log.LogTrace("Executing SQL command. Command text {CommandText} for event {EventType} in " +
                                 "projection {ProjectionName}. Parameters {Parameters}",
                                 dbCommand.CommandText,
                                 typeof(TEvent),
                                 typeof(TSqlProjection),
                                 dbCommand.Parameters.ToFormattedString());
                }

                int rowsAffected;
                try
                {
                    if (dbCommand is DbCommand concreteCommand)
                    {
                        rowsAffected = await concreteCommand.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        rowsAffected = dbCommand.ExecuteNonQuery();
                    }
                }
                catch (DbException ex)
                {
                    Log.LogError(ex,
                                 "Error executing database command {CommandText} for event {EventType} in" +
                                 "projection {ProjectionName}. Parameters {parameters}",
                                 dbCommand.CommandText,
                                 typeof(TEvent),
                                 typeof(TSqlProjection),
                                 dbCommand.Parameters.ToFormattedString());
                    throw;
                }

                if (rowsAffected == 0)
                {
                    Log.LogWarning("No rows affected by database command {CommandText} for event {EventType} in " +
                                   "projection {ProjectionName}. Please check this is intended",
                                   dbCommand.CommandText,
                                   typeof(TEvent),
                                   typeof(TSqlProjection));
                }
            };
        }

        private void ValidateDeleteCommand()
        {
            var primaryKeyColumns = _sqlProjection.Columns.Where(c => c.Value.IsInPrimaryKey)
                                                  .Select(kvp => kvp.Key);
            var parameterNames = _parameterBindingMap.GetParameterNames();

            if (!primaryKeyColumns.All(pk => parameterNames.Contains(pk)))
            {
                throw new InvalidOperationException("All primary key columns must be present in event. " +
                                                    $"{typeof(TEvent).FullName} cannot be used to delete " +
                                                    $"from table {_sqlProjection.TableName} as it can't identify " +
                                                    "a single row. If you wish to delete multiple rows from this " +
                                                    "event, you will need to use a custom command instead");
            }

        }

        private ParameterBindingMap<TEvent> CreateReflectedParameterBindingMap()
        {
            var parameterBindings = new Dictionary<string, Func<TEvent, object>>();
            var eventProperties = typeof(TEvent).GetProperties();

            foreach (var key in _sqlProjection.Columns.Keys)
            {
                var propertyInfo = eventProperties.FirstOrDefault(x => x.Name == key);
                if (propertyInfo != null)
                {
                    parameterBindings.Add(key, @event => propertyInfo.GetValue(@event));
                }
            }

            return new ParameterBindingMap<TEvent>(parameterBindings);
        }
    }
}