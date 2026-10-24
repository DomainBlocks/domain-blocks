using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Testcontainers.MongoDb;

namespace DomainBlocks.Testing.Integration.EventStore.MongoDB;

public sealed class MongoReplicaSet : IAsyncDisposable
{
    // The number of lines of each of the container's output streams that a failed start reports.
    private const int OutputLineCount = 40;

    private readonly INetwork _network;
    private readonly MongoDbContainer _container;

    private MongoReplicaSet(INetwork network, MongoDbContainer container)
    {
        _network = network;
        _container = container;
    }

    public string ConnectionString => _container.GetConnectionString();

    public static async Task<MongoReplicaSet> CreateAsync(string networkAlias = "mongo")
    {
        var network = new NetworkBuilder()
            .WithName($"mongo-test-{Guid.NewGuid():N}")
            .Build();

        await network.CreateAsync();

        var container = new MongoDbBuilder("mongo:7.0")
            .WithReplicaSet()
            .WithNetwork(network)
            .WithNetworkAliases(networkAlias)
            .Build();

        try
        {
            await container.StartAsync();
        }
        catch (Exception ex)
        {
            // Testcontainers reports only the call that failed. The container's output says why mongod stopped.
            throw new InvalidOperationException(
                $"The MongoDB container failed to start. {await DescribeOutputAsync(container)}",
                ex);
        }

        return new MongoReplicaSet(network, container);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        await _network.DisposeAsync();
    }

    private static async Task<string> DescribeOutputAsync(MongoDbContainer container)
    {
        try
        {
            var (stdout, stderr) = await container.GetLogsAsync(timestampsEnabled: false);

            return $"Its output ended with:{Environment.NewLine}{Tail(stdout)}{Environment.NewLine}" +
                   $"Its error output ended with:{Environment.NewLine}{Tail(stderr)}";
        }
        catch (Exception ex)
        {
            return $"Its output could not be read: {ex.Message}";
        }
    }

    private static string Tail(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines.TakeLast(OutputLineCount));
    }
}