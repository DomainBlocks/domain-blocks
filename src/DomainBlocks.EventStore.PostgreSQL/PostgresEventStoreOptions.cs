namespace DomainBlocks.EventStore.PostgreSQL;

public sealed class PostgresEventStoreOptions
{
    /// <summary>
    /// Gets or sets the schema of the store's tables and functions. The default is <c>dbx</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value does not match <c>^[a-z_][a-z0-9_]{0,62}$</c>.</exception>
    public string Schema
    {
        get;
        set
        {
            SchemaObjectNames.ThrowIfInvalidSchema(value);
            field = value;
        }
    } = "dbx";

    /// <summary>
    /// Gets or sets the maximum number of queued append requests before callers wait. The default is 1,000.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int AppendQueueCapacity
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1_000;

    /// <summary>
    /// Gets or sets the maximum number of append requests committed in one round trip. The default is 500.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int AppendMaxBatchSize
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 500;

    /// <summary>
    /// Gets or sets the number of events fetched per round trip when reading. The default is 1,000.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int ReadPageSize
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 1_000;

    /// <summary>
    /// Gets or sets the options for the replication connection that feeds live subscriptions.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public PostgresReplicationOptions Replication
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = new();
}