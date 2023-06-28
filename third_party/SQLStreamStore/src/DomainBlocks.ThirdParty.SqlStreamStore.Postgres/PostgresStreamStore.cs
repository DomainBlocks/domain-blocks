using System.Data;
using DomainBlocks.ThirdParty.SqlStreamStore.Infrastructure;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres.PgSqlScripts;
using DomainBlocks.ThirdParty.SqlStreamStore.Subscriptions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DomainBlocks.ThirdParty.SqlStreamStore.Postgres
{
    /// <summary>
    ///     Represents a PostgreSQL stream store implementation.
    /// </summary>
    public partial class PostgresStreamStore : StreamStoreBase
    {
        private readonly PostgresStreamStoreSettings _settings;
        private readonly Func<NpgsqlConnection> _createConnection;
        private readonly Schema _schema;
        private readonly Lazy<IStreamStoreNotifier> _streamStoreNotifier;

        public const int CurrentVersion = 1;

        /// <summary>
        ///     Initializes a new instance of <see cref="PostgresStreamStore"/>
        /// </summary>
        /// <param name="settings">A settings class to configure this instance.</param>
        public PostgresStreamStore(PostgresStreamStoreSettings settings)
            : base(settings.GetUtcNow, settings.LogName)
        {
            _settings = settings;
            _schema = new Schema(_settings.Schema);

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_settings.ConnectionString);
            dataSourceBuilder.MapComposite<PostgresNewStreamMessage>(_schema.NewStreamMessage);

            // TODO: Consider proper multihost support?
            // https://www.npgsql.org/doc/api/Npgsql.NpgsqlDataSourceBuilder.html#Npgsql_NpgsqlDataSourceBuilder_BuildMultiHost
            // Multihost data sources have built-in load balancing and failover support. 
            // We are using Build() which is for single hosts. 
            // We need to understand the implications of having a MultiHost data source.
            var dataSource = dataSourceBuilder.Build();

            _createConnection = () => dataSource.CreateConnection();
            _streamStoreNotifier = new Lazy<IStreamStoreNotifier>(() =>
            {
                if(_settings.CreateStreamStoreNotifier == null)
                {
                    throw new InvalidOperationException(
                        "Cannot create notifier because supplied createStreamStoreNotifier was null");
                }

                return settings.CreateStreamStoreNotifier.Invoke(this);
            });
        }

        private async Task<NpgsqlConnection> OpenConnection(CancellationToken cancellationToken)
        {
            var connection = _createConnection();

            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await connection.ReloadTypesAsync();

            if (!_settings.ExplainAnalyze) return connection;
            
            await using var command = new NpgsqlCommand(_schema.EnableExplainAnalyze, connection);
            
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            return connection;
        }

        /// <summary>
        ///     Creates a scheme that will hold streams and messages, if the schema does not exist.
        ///     Calls to this should part of an application's deployment/upgrade process and
        ///     not every time your application boots up.
        /// </summary>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task CreateSchemaIfNotExists(CancellationToken cancellationToken = default)
        {
            await using var connection = _createConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            
            await using(var command = BuildCommand($"CREATE SCHEMA IF NOT EXISTS {_settings.Schema}", transaction))
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using(var command = BuildCommand(_schema.Definition, transaction))
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Drops all tables related to this store instance.
        /// </summary>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task DropAll(CancellationToken cancellationToken = default)
        {
            GuardAgainstDisposed();

            await using var connection = _createConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = BuildCommand(_schema.DropAll, transaction);
            
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Checks the store schema for the correct version.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>A <see cref="CheckSchemaResult"/> representing the result of the operation.</returns>
        public async Task<CheckSchemaResult> CheckSchema(CancellationToken cancellationToken = default)
        {
            await using(var connection = _createConnection())
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using(var transaction = await connection.BeginTransactionAsync(cancellationToken))
                await using(var command = BuildFunctionCommand(_schema.ReadSchemaVersion, transaction))
                {
                    var result = (int) (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;

                    return new CheckSchemaResult(result, CurrentVersion);
                }
            }
        }

        private Func<CancellationToken, Task<string>> GetJsonData(PostgresqlStreamId streamId, int version)
            => async cancellationToken =>
            {
                await using var connection = await OpenConnection(cancellationToken);
                await using(var transaction = await connection.BeginTransactionAsync(cancellationToken))
                await using(var command = BuildFunctionCommand(
                                _schema.ReadJsonData,
                                transaction,
                                Parameters.StreamId(streamId),
                                Parameters.Version(version)))
                await using(var reader = await command
                                .ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken)
                                .ConfigureAwait(false))
                {
                    if(!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || reader.IsDBNull(0))
                    {
                        return null;
                    }

                    using(var textReader = await reader.GetTextReaderAsync(0, cancellationToken))
                    {
                        return await textReader.ReadToEndAsync().ConfigureAwait(false);
                    }
                }
            };

        private static NpgsqlCommand BuildFunctionCommand(
            string function,
            NpgsqlTransaction transaction,
            params NpgsqlParameter[] parameters)
        {
            string sql = $"SELECT * FROM {function}()";
            
            var sqlParams = string.Join(", $", Enumerable.Range(1, parameters.Length));
            if (parameters.Length > 0)
            {
                sql = $@"SELECT * FROM {function}(${sqlParams})";
            }
            
            var command = new NpgsqlCommand(sql, transaction.Connection, transaction)
            {
                CommandType = CommandType.Text,
            };

            foreach(var parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            return command;
        }

        private static NpgsqlCommand BuildCommand(
            string commandText,
            NpgsqlTransaction transaction) => new NpgsqlCommand(commandText, transaction.Connection, transaction);

        internal async Task<int> TryScavenge(
            StreamIdInfo streamIdInfo,
            CancellationToken cancellationToken)
        {
            if(streamIdInfo.PostgresqlStreamId == PostgresqlStreamId.Deleted)
            {
                return -1;
            }

            try
            {
                await using(var connection = await OpenConnection(cancellationToken))
                await using(var transaction = connection.BeginTransaction())
                {
                    var deletedMessageIds = new List<Guid>();
                    await using(var command = BuildFunctionCommand(
                                    _schema.Scavenge,
                                    transaction,
                                    Parameters.StreamId(streamIdInfo.PostgresqlStreamId)))
                    await using(var reader = await command
                                    .ExecuteReaderAsync(cancellationToken)
                                    .ConfigureAwait(false))
                    {
                        while(await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            deletedMessageIds.Add(reader.GetGuid(0));
                        }
                    }

                    Logger.LogInformation(
                        "Found {count} message(s) for stream {streamId} to scavenge.",
                        deletedMessageIds.Count,
                        streamIdInfo.PostgresqlStreamId);

                    if(deletedMessageIds.Count > 0)
                    {
                        Logger.LogInformation(
                            "Scavenging the following messages on stream {streamId}: {messageIds}",
                            streamIdInfo.PostgresqlStreamId,
                            deletedMessageIds);

                        await DeleteEventsInternal(
                            streamIdInfo,
                            deletedMessageIds.ToArray(),
                            transaction,
                            cancellationToken).ConfigureAwait(false);
                    }

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    return deletedMessageIds.Count;
                }
            }
            catch(Exception ex)
            {
                Logger.LogWarning(
                    ex,
                    "Scavenge attempt failed on stream {streamId}. Another attempt will be made when this stream is written to.",
                    streamIdInfo.PostgresqlStreamId.IdOriginal);
            }

            return -1;
        }

        /// <summary>
        /// Returns the script that can be used to create the Sql Stream Store in a Postgres database.
        /// </summary>
        /// <returns>The database creation script.</returns>
        public string GetSchemaCreationScript()
        {
            return _schema.Definition;
        }
    }
}
