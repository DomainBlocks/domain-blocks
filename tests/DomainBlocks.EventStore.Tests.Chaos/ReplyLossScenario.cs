using DomainBlocks.EventStore.MongoDB;
using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Serialization.MongoDB.Bson;
using DomainBlocks.Testing;
using DomainBlocks.Testing.Integration.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Runs a writer and a subscriber against a one-member replica set, through a proxy that repeatedly loses the
/// server's replies and then drops every connection.
/// </summary>
internal sealed class ReplyLossScenario : IAsyncDisposable
{
    private const int FaultCount = 8;
    private const int Seed = 1;

    // Longer than the writer's append timeout, so an append in flight when a fault starts is left in doubt.
    private static readonly TimeSpan FaultDuration = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan HealthyDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CatchUpTimeout = TimeSpan.FromSeconds(30);

    private readonly MongoEventStoreOptions _options = new() { DatabaseName = "chaos" };
    private readonly ILoggerFactory _loggerFactory = TestLoggerFactory.Create(LogLevel.Warning);
    private readonly MongoReplicaSet _replicaSet;
    private readonly MongoFaultProxy _proxy;
    private readonly MongoClient _proxiedClient;
    private readonly MongoClient _directClient;

    private ReplyLossScenario(MongoReplicaSet replicaSet, MongoFaultProxy proxy)
    {
        _replicaSet = replicaSet;
        _proxy = proxy;
        _proxiedClient = new MongoClient(proxy.ConnectionString);
        _directClient = new MongoClient(replicaSet.ConnectionString);
    }

    public static async Task<ReplyLossScenario> StartAsync(CancellationToken cancellationToken)
    {
        var replicaSet = await MongoReplicaSet.CreateAsync();
        var proxy = await MongoFaultProxy.StartAsync(replicaSet, cancellationToken);
        var scenario = new ReplyLossScenario(replicaSet, proxy);

        await MongoEventStoreAdmin.EnsureInitializedAsync(
            scenario._directClient,
            scenario._options,
            cancellationToken: cancellationToken);

        return scenario;
    }

    /// <summary>
    /// Runs the writer and the subscriber through every fault, and records what they did and the event log that they
    /// left.
    /// </summary>
    public async Task<RunRecord> RunAsync(RetryCommitId retryCommitId, CancellationToken cancellationToken)
    {
        await using var subscriberStore = CreateEventStore(_proxiedClient, "Subscriber");
        await using var subscriber = new ChaosSubscriber(subscriberStore);

        IReadOnlyList<AppendOperation> operations;

        await using (var writerStore = CreateEventStore(_proxiedClient, "Writer"))
        {
            var writer = new ChaosWriter(writerStore, retryCommitId, Seed);
            using var stopping = new CancellationTokenSource();
            var writing = writer.RunAsync(stopping.Token, cancellationToken);

            try
            {
                await InjectFaultsAsync(cancellationToken);
            }
            finally
            {
                await stopping.CancelAsync();
            }

            operations = await writing;
        }

        // The writer's store is disposed, so nothing else can reach the event log.
        var log = await ReadLogAsync(cancellationToken);

        if (log.Count > 0)
            await subscriber.WaitForAsync(log[^1].LogPosition, CatchUpTimeout, cancellationToken);

        await subscriber.StopAsync();

        return new RunRecord(operations, log, subscriber.Received, subscriber.Error, subscriber.FellBehindCount);
    }

    public async ValueTask DisposeAsync()
    {
        _proxiedClient.Dispose();
        _directClient.Dispose();
        _loggerFactory.Dispose();
        await _proxy.DisposeAsync();
        await _replicaSet.DisposeAsync();
    }

    // The last fault is followed by a healthy period too, so the appends that it left in doubt can complete.
    private async Task InjectFaultsAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < FaultCount; i++)
        {
            await Task.Delay(HealthyDuration, cancellationToken);
            await _proxy.StartLosingRepliesAsync();
            await Task.Delay(FaultDuration, cancellationToken);
            await _proxy.StopLosingRepliesAsync();
        }

        await Task.Delay(HealthyDuration, cancellationToken);
    }

    // The proxy plays no part in the read, so the log is read as the server holds it.
    private async Task<IReadOnlyList<ObservedEvent>> ReadLogAsync(CancellationToken cancellationToken)
    {
        await using var eventStore = CreateEventStore(_directClient, "Reader");
        var log = new List<ObservedEvent>();

        await foreach (var readEvent in eventStore.ReadAll().WithCancellation(cancellationToken))
            log.Add(ObservedEvent.From(readEvent));

        return log;
    }

    private IEventStore<object, string, StreamPosition, LogPosition> CreateEventStore(IMongoClient client, string name)
    {
        return new MongoEventStoreBuilder<object>()
            .UseClient(client)
            .UseOptions(_options)
            .UseLogger(_loggerFactory.CreateLogger(name))
            .ConfigureCodec(x => x
                .UseEventTypeMap(new EventTypeMapBuilder().Add<ChaosEvent>().Build())
                .UseEventSerializer(new BsonDocumentObjectSerializer()))
            .Build();
    }
}