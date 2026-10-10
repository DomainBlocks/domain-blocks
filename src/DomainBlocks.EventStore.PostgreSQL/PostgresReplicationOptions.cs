namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Provides options for the logical replication connection that feeds live subscriptions.
/// </summary>
public sealed class PostgresReplicationOptions
{
    /// <summary>
    /// Gets or sets the connection string for the replication connection, or <see langword="null"/> (the default) to
    /// use the store's.
    /// </summary>
    /// <remarks>
    /// A data source's connection string includes the password only with <c>Persist Security Info=true</c>. The role
    /// needs the REPLICATION attribute or superuser rights, and the server must run with <c>wal_level = logical</c>.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the prefix of the temporary replication slot names. The prefix must be 1 to 32 characters long and
    /// contain only lowercase letters, digits, or underscores. The default is <c>dbx</c>.
    /// </summary>
    public string SlotNamePrefix { get; set; } = "dbx";

    /// <summary>
    /// Gets or sets a value that indicates whether to receive row values in binary rather than text. Binary values
    /// require PostgreSQL 14 or later. The default is <see langword="true"/>.
    /// </summary>
    public bool UseBinaryProtocol { get; set; } = true;

    /// <summary>
    /// Gets or sets how long to wait for a message from the server before treating the connection as lost. The default
    /// is 60 seconds.
    /// </summary>
    public TimeSpan WalReceiverTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets how often to report the consumed WAL position, so the server can release WAL. The default is 10
    /// seconds.
    /// </summary>
    public TimeSpan WalReceiverStatusInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the initial delay before reconnecting a lost replication connection. It grows exponentially, with
    /// jitter, up to <see cref="MaxRetryDelay"/>. The default is 1 second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum delay between reconnection attempts. The default is 1 minute.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the maximum number of consecutive failed connection attempts before subscriptions fail. The default
    /// is <see cref="int.MaxValue"/>.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = int.MaxValue;
}