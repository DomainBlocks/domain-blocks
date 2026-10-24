using Testcontainers.PostgreSql;

namespace DomainBlocks.Testing.Integration.EventStore.PostgreSQL;

/// <summary>
/// A throw-away PostgreSQL server configured for logical replication. The image can be overridden with the
/// DBX_POSTGRES_IMAGE environment variable, e.g., to run the suite against the minimum supported version.
/// </summary>
public sealed class PostgresServer : IAsyncDisposable
{
    public const string DefaultImage = "postgres:17";
    public const string ImageEnvironmentVariable = "DBX_POSTGRES_IMAGE";

    private readonly PostgreSqlContainer _container;

    private PostgresServer(PostgreSqlContainer container)
    {
        _container = container;
    }

    public string ConnectionString => _container.GetConnectionString();

    public static async Task<PostgresServer> StartAsync()
    {
        var image = Environment.GetEnvironmentVariable(ImageEnvironmentVariable) is { Length: > 0 } configured
            ? configured
            : DefaultImage;

        var container = new PostgreSqlBuilder(image)
            .WithCommand("-c", "wal_level=logical", "-c", "max_replication_slots=16", "-c", "max_wal_senders=16")

            // Testcontainers turns synchronous_commit off. A commit is then visible, and acknowledged, before it is
            // flushed, and the live feed only receives it once the WAL writer flushes, up to 200 ms later. This turns
            // it back on, as a server has it by default. It costs little here, because the container does not sync the
            // flush to disk.
            .WithCommand("-c", "synchronous_commit=on")
            .Build();

        await container.StartAsync();

        return new PostgresServer(container);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}