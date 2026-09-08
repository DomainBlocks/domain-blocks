namespace DomainBlocks.EventStore.PostgreSQL;

public sealed class PostgresEventStoreAdminOptions
{
    /// <summary>
    /// Whether to create the logical replication publication for the event log, which subscriptions require. Creating
    /// a publication needs ownership of the table or superuser rights, so restricted deployments may create it out of
    /// band and disable this.
    /// </summary>
    public bool CreatePublication { get; set; } = true;
}
