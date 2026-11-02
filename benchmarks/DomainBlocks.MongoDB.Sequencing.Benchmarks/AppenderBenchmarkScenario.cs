using DomainBlocks.Testing.Integration.MongoDB;
using MongoDB.Driver;

namespace DomainBlocks.MongoDB.Sequencing.Benchmarks;

/// <summary>
/// One benchmark's appenders, over a database of their own that is dropped on dispose.
/// </summary>
internal sealed class AppenderBenchmarkScenario : IAsyncDisposable
{
    private static readonly TimeSpan DropDatabaseTimeout = TimeSpan.FromSeconds(10);

    private readonly MongoClient _client;
    private readonly string _databaseName;
    private readonly MongoSequencedAppender<BenchmarkDocument, object>[] _appenders;

    private AppenderBenchmarkScenario(
        MongoClient client,
        string databaseName,
        MongoSequencedAppender<BenchmarkDocument, object>[] appenders)
    {
        _client = client;
        _databaseName = databaseName;
        _appenders = appenders;
    }

    public static async Task<AppenderBenchmarkScenario> CreateAsync(
        string connectionString,
        int instanceCount,
        MongoSequencedAppenderOptions options)
    {
        var client = new MongoClient(connectionString);
        var databaseName = $"seq_benchmark_{Guid.NewGuid():N}";
        var targetNamespace = new CollectionNamespace(databaseName, "targets");

        var index = new CreateIndexModel<BenchmarkDocument>(
            Builders<BenchmarkDocument>.IndexKeys.Ascending(x => x.Sequence),
            new CreateIndexOptions { Unique = true });

        await client
            .GetDatabase(databaseName)
            .GetCollection<BenchmarkDocument>(targetNamespace.CollectionName)
            .Indexes
            .CreateOneAsync(index);

        var binding = new MongoSequenceBinding<BenchmarkDocument>(
            sequenceCollectionNamespace: new CollectionNamespace(databaseName, "sequences"),
            sequenceId: "benchmark",
            targetCollectionNamespace: targetNamespace,
            targetField: new ExpressionFieldDefinition<BenchmarkDocument, long>(x => x.Sequence));

        var appenders = Enumerable
            .Range(0, instanceCount)
            .Select(i => new MongoSequencedAppender<BenchmarkDocument, object>(
                client,
                binding,
                options: options,
                logger: MongoTestEnvironment.LoggerFactory.CreateLogger($"appender_{i}")))
            .ToArray();

        return new AppenderBenchmarkScenario(client, databaseName, appenders);
    }

    /// <summary>
    /// Appends one document, spreading workers over the appender instances.
    /// </summary>
    public Task AppendAsync(int workerIndex, CancellationToken cancellationToken)
    {
        var appender = _appenders[workerIndex % _appenders.Length];

        return appender.AppendAsync(
            [new BenchmarkDocument { Value = "Benchmark" }],
            context: new object(),
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var appender in _appenders)
            await appender.DisposeAsync();

        using var timeout = new CancellationTokenSource(DropDatabaseTimeout);

        try
        {
            await _client.DropDatabaseAsync(_databaseName, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // The container is removed with the test run, so a database left behind costs nothing.
        }
        finally
        {
            _client.Dispose();
        }
    }

    private sealed class BenchmarkDocument
    {
        public required string Value { get; init; }

        public long Sequence { get; init; }
    }
}