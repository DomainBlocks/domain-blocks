namespace DomainBlocks.EventStore.PostgreSQL;

public sealed class PostgresEventStoreAdminOptions
{
    /// <summary>
    /// Gets or sets a value that indicates whether to create the publication that subscriptions require. The default is
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Creating a publication requires table ownership or superuser rights. Without them, create it separately and
    /// disable this.
    /// </remarks>
    public bool CreatePublication { get; set; } = true;
}