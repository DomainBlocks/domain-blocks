namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Configures the logical replication connection that feeds live subscriptions.
/// </summary>
public sealed class PostgresReplicationOptions
{
    /// <summary>
    /// The connection string for the replication connection. Defaults to the data source's connection string. The
    /// role must have the REPLICATION attribute (or be a superuser) and the server must run with
    /// <c>wal_level = logical</c>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The prefix of the temporary replication slot names, which must be lower-case letters, digits or underscores.
    /// </summary>
    public string SlotNamePrefix { get; set; } = "dbx";

    /// <summary>
    /// Whether to receive row values in binary rather than text representation. Requires PostgreSQL 14 or later.
    /// </summary>
    public bool UseBinaryProtocol { get; set; } = true;

    /// <summary>
    /// How long to wait for any message from the server before treating the connection as lost.
    /// </summary>
    public TimeSpan WalReceiverTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How often to report the consumed WAL position to the server, which lets it release WAL.
    /// </summary>
    public TimeSpan WalReceiverStatusInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The base delay between attempts to re-establish a lost replication connection. Grows exponentially, with
    /// jitter, up to <see cref="MaxRetryDelay"/>.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The maximum number of consecutive failed attempts to establish a replication connection before subscriptions
    /// fail.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = int.MaxValue;
}
