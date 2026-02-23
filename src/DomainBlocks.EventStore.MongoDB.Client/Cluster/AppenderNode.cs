using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using DomainBlocks.EventStore.MongoDB.Client.Cluster.Events;
using DomainBlocks.EventStore.MongoDB.Client.Cluster.Events.Local;
using DomainBlocks.Infrastructure.MongoDB.ChangeStreams;
using DomainBlocks.Infrastructure.MongoDB.Leases;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Cluster;

public sealed class AppenderNode
{
    public const string LeaseResourceId = "dbx_LogLease";

    private readonly IMongoDatabase _database;
    private readonly EventStoreNamespaceSettings _namespaceSettings;
    private readonly ILeaseClient _leaseClient;
    private readonly ILogger<AppenderNode> _logger;
    private readonly Channel<IAppenderEvent> _channel;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly AppenderProcess _appenderProcess = new();
    private Task? _changeStreamIngressTask;
    private Task? _leaseContenderTask;
    private Task? _eventConsumerTask;

    public AppenderNode(
        IMongoClient mongoClient,
        EventStoreNamespaceSettings namespaceSettings,
        ILeaseClient leaseClient,
        ILogger<AppenderNode> logger)
    {
        _database = mongoClient.GetDatabase(namespaceSettings.DatabaseName)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true));

        _namespaceSettings = namespaceSettings;
        _leaseClient = leaseClient;
        _logger = logger;

        // TODO: use bounded
        var channelOptions = new UnboundedChannelOptions()
        {
            SingleWriter = false,
            SingleReader = true
        };

        _channel = Channel.CreateUnbounded<IAppenderEvent>(channelOptions);
    }

    [MemberNotNull(nameof(_changeStreamIngressTask), nameof(_leaseContenderTask), nameof(_eventConsumerTask))]
    public void Start()
    {
        _changeStreamIngressTask = RunChangeStreamIngressAsync();
        _leaseContenderTask = RunLeaseContenderAsync();
        _eventConsumerTask = RunEventConsumerAsync();
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopCts.Token);

        Start();

        await Task
            .WhenAny(_changeStreamIngressTask, _leaseContenderTask, _eventConsumerTask)
            .ConfigureAwait(false);

        if (!_stopCts.IsCancellationRequested)
            await _stopCts.CancelAsync().ConfigureAwait(false);

        await Task
            .WhenAll(_changeStreamIngressTask, _leaseContenderTask, _eventConsumerTask)
            .ConfigureAwait(false);
    }

    private async Task RunChangeStreamIngressAsync()
    {
        var subscriptionOptions = new ChangeStreamSubscriptionOptions
        {
            MongoOptions = new ChangeStreamOptions
            {
                FullDocument = ChangeStreamFullDocumentOption.WhenAvailable,
                FullDocumentBeforeChange = ChangeStreamFullDocumentBeforeChangeOption.WhenAvailable
            }
        };

        var subscription = _database.SubscribeToChangeStream(subscriptionOptions, _logger);
        var eventResolver = new ChangeStreamEventResolver(_namespaceSettings);

        await using (subscription.ConfigureAwait(false))
        {
            await subscription
                .ForEachAsync(
                    async (change, ct) =>
                    {
                        if (!eventResolver.TryResolve(change, out var @event))
                            return;

                        await _channel.Writer.WriteAsync(@event, ct).ConfigureAwait(false);
                    },
                    _stopCts.Token)
                .ConfigureAwait(false);
        }
    }

    private async Task RunLeaseContenderAsync()
    {
        var options = new AcquireLeaseOptions
        {
            AcquireTimeout = Timeout.InfiniteTimeSpan
        };

        while (!_stopCts.IsCancellationRequested)
        {
            var result = await _leaseClient
                .AcquireLeaseAsync<LogLeaseState>(LeaseResourceId, options, _stopCts.Token)
                .ConfigureAwait(false);

            if (!result.IsAcquired)
                continue; // Shouldn't happen with infinite timeout

            var handle = result.Handle;

            await _channel.Writer.WriteAsync(new LeaseLocallyAcquired(handle.Claim)).ConfigureAwait(false);

            await using (handle.ConfigureAwait(false))
            {
                await handle.LeaseLostTask.WaitAsync(_stopCts.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task RunEventConsumerAsync()
    {
        await foreach (var @event in _channel.Reader.ReadAllAsync(_stopCts.Token).ConfigureAwait(false))
        {
            _appenderProcess.Handle(@event);
        }
    }
}