namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// Provides options for the logical replication connection that feeds live subscriptions.
/// </summary>
/// <remarks>
/// <para>
/// The feed reads a failover slot. When the server lists its standbys' physical replication slots in
/// <c>synchronized_standby_slots</c>, the slot sends a commit only once they have it, so live subscriptions never
/// receive an event that a failover loses. List every standby that a failover can promote. With synchronous standbys
/// and no list, live subscriptions still work but lack that guarantee, and the store logs a warning. The slot waits for
/// every listed slot, so an inactive one holds live subscriptions back.
/// </para>
/// <para>
/// Standbys with <c>sync_replication_slots</c> and <c>hot_standby_feedback</c> keep a copy of the slot, so that after a
/// failover the feed resumes on the promoted standby instead of resetting subscriptions.
/// </para>
/// <para>
/// Live subscriptions follow a failover only through a host that always reaches the primary, such as a writer
/// endpoint, a virtual IP, or a proxy. They fail if the store's data source or the replication connection string lists
/// multiple hosts.
/// </para>
/// </remarks>
public sealed class PostgresReplicationOptions
{
    /// <summary>
    /// Gets or sets the connection string for the replication connection, or <see langword="null"/> (the default) to
    /// use the store's.
    /// </summary>
    /// <remarks>
    /// A data source's connection string includes the password only with <c>Persist Security Info=true</c>. The role
    /// needs the REPLICATION attribute or superuser rights, and the server must be PostgreSQL 17 or later and run with
    /// <c>wal_level = logical</c>.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets how long a replication slot of the store's schema can stay inactive before a store on that schema
    /// drops it. The value must be positive. The default is 10 minutes.
    /// </summary>
    /// <remarks>
    /// A store names its slots after its schema. A process that stops without dropping its slot leaves it behind,
    /// holding WAL. Stores drop the leftover slots of their schema before they create one, and at this interval while
    /// subscriptions are live. A live store's slot is inactive only while the store reconnects. Each store on a schema
    /// applies its value to all of the schema's slots, so stores on the same schema should use the same value. To cap
    /// the WAL that a leftover slot holds, set the server's <c>max_slot_wal_keep_size</c>.
    /// </remarks>
    public TimeSpan InactiveSlotTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets a value that indicates whether to receive row values in binary rather than text. The default is
    /// <see langword="true"/>.
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