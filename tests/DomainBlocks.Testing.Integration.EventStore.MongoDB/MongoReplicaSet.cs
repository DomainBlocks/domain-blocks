using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace DomainBlocks.Testing.Integration.EventStore.MongoDB;

public sealed class MongoReplicaSet : IAsyncDisposable
{
    private const string ReplicaSetName = "rs0";

    // The number of lines of each of the container's output streams that a failed start reports.
    private const int OutputLineCount = 40;

    private static readonly TimeSpan PrimaryTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PrimaryPollInterval = TimeSpan.FromMilliseconds(100);

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

        // The builder's default root user and the key file script that WithReplicaSet() adds both make the image run
        // its setup on a temporary mongod first. The mongod that serves the tests then sometimes fails to bind its port
        // while the temporary one is still exiting: https://github.com/docker-library/mongo/issues/730. Without the
        // user and the script, the image starts only the mongod that serves the tests, so the replica set is initiated
        // here instead. Go back to WithReplicaSet() once the issue is fixed.
        var container = new MongoDbBuilder("mongo:7.0")
            .WithUsername(string.Empty)
            .WithPassword(string.Empty)
            .WithCommand("--replSet", ReplicaSetName)
            .WithNetwork(network)
            .WithNetworkAliases(networkAlias)
            .Build();

        try
        {
            await container.StartAsync();
            await InitiateAsync(container.GetConnectionString());
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

    private static async Task InitiateAsync(string connectionString)
    {
        using var client = new MongoClient(connectionString);
        var admin = client.GetDatabase("admin");

        var config = new BsonDocument
        {
            { "_id", ReplicaSetName },
            { "members", new BsonArray { new BsonDocument { { "_id", 0 }, { "host", "127.0.0.1:27017" } } } }
        };

        await admin.RunCommandAsync<BsonDocument>(new BsonDocument("replSetInitiate", config));

        // The member elects itself primary shortly after the replica set is initiated, and writes fail until it has.
        using var timeout = new CancellationTokenSource(PrimaryTimeout);

        while (!await IsWritablePrimaryAsync(admin, timeout.Token))
            await Task.Delay(PrimaryPollInterval, timeout.Token);
    }

    private static async Task<bool> IsWritablePrimaryAsync(IMongoDatabase admin, CancellationToken ct)
    {
        var hello = await admin.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: ct);
        return hello["isWritablePrimary"].AsBoolean;
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