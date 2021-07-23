using System;
using DomainLib.Common;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Threading.Tasks;

namespace DomainLib.Projections.Sql
{
    public sealed class SqlProjectionContext : IProjectionContext
    {
        private static readonly ILogger<SqlProjectionContext> Log = Logger.CreateFor<SqlProjectionContext>();
        private readonly ISqlDialect _sqlDialect;
        private readonly HashSet<ISqlProjection> _projections = new();
        private readonly SqlContextSettings _settings;

        private readonly StringBuilder _schemaStringBuilder = new();
        private IDbTransaction _activeTransaction;
        private bool _isProcessingLiveEvents;

        public SqlProjectionContext(IDbConnector connector, ISqlDialect sqlDialect)
        {
            if (connector == null) throw new ArgumentNullException(nameof(connector));
            _sqlDialect = sqlDialect ?? throw new ArgumentNullException(nameof(sqlDialect));
            _settings = connector.ContextSettings;
            Connection = connector.Connection;
        }

        public IDbConnection Connection { get; }

        public async Task OnSubscribing()
        {
            try
            {
                if (Connection.State == ConnectionState.Closed)
                {
                    if (Connection is DbConnection concreteConnection)
                    {
                        await concreteConnection.OpenAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        Connection.Open();
                    }

                    await CreateSchema().ConfigureAwait(false);
                }

                if (_settings.UseTransactionBeforeCaughtUp)
                {
                    await BeginTransaction();
                }

                _isProcessingLiveEvents = false;
            }
            catch (Exception ex)
            {
                Log.LogCritical(ex, "Exception occurred attempting to handle subscribing to event stream");
                throw;
            }
        }

        public Task OnCaughtUp()
        {
            try
            {
                if (_settings.UseTransactionBeforeCaughtUp)
                {
                    if (_activeTransaction != null)
                    {
                        _activeTransaction.Commit();
                        _activeTransaction = null;
                    }
                    else
                    {
                        Log.LogWarning("Caught up to live event stream, but no transaction was found.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogCritical(ex, "Exception occurred attempting to handle live event stream starting");
                throw;
            }

            _isProcessingLiveEvents = true;
            return Task.CompletedTask;
        }

        public async Task OnBeforeHandleEvent()
        {
            if (_isProcessingLiveEvents)
            {
                if (_settings.HandleLiveEventsInTransaction)
                {
                    await BeginTransaction();
                }
            }
        }

        public Task OnAfterHandleEvent()
        {
            if (_isProcessingLiveEvents)
            {
                if (_settings.HandleLiveEventsInTransaction)
                {
                    if (_activeTransaction != null)
                    {
                        _activeTransaction.Commit();
                        _activeTransaction = null;
                    }
                    else
                    {
                        Log.LogWarning("Expected to be in a transaction when handling event, but none was found.");
                    }
                }
            }

            return Task.CompletedTask;
        }

        public void RegisterProjection(ISqlProjection projection)
        {
            if (_projections.Add(projection))
            {
                var createTableSql = string.IsNullOrEmpty(projection.CustomCreateTableSql)
                                         ? _sqlDialect.BuildCreateTableSql(projection.TableName, projection.Columns.Values)
                                         : projection.CustomCreateTableSql;

                createTableSql = string.Concat(createTableSql, " ", projection.AfterCreateTableSql, " ");

                _schemaStringBuilder.Append(createTableSql);
            }
        }

        private async Task BeginTransaction()
        {
            if (Connection is DbConnection concreteConnection)
            {
                _activeTransaction = await concreteConnection.BeginTransactionAsync().ConfigureAwait(false);
            }
            else
            {
                _activeTransaction = Connection.BeginTransaction();
            }
        }

        private async Task CreateSchema()
        {
            try
            {
                var createSchemaCommand = Connection.CreateCommand();
            
                createSchemaCommand.CommandText = _schemaStringBuilder.ToString();

                if (createSchemaCommand is DbCommand concreteCommand)
                {
                    await concreteCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                else
                {
                    createSchemaCommand.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Log.LogCritical(ex, "Unable to build SQL table schema");
                throw;
            }
        }
    }
}