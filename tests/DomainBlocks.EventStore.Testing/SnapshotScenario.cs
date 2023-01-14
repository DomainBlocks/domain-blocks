using DomainBlocks.Core.Builders;
using DomainBlocks.Core.Persistence;
using DomainBlocks.Core.Serialization;
using DomainBlocks.EventStore.Persistence;
using DomainBlocks.EventStore.Serialization;

namespace DomainBlocks.EventStore.Testing;

public class SnapshotScenario
{
    private EventStoreIntegrationTest _test = null!;
    private AggregateRepository _aggregateRepository = null!;
    private LoadedAggregate<TestAggregateState> _loadedAggregate = null!;
    private readonly Guid _id;
    private long _aggregateVersion = StreamVersion.NewStream;

    public SnapshotScenario()
    {
        _id = Guid.NewGuid();
    }

    public TestAggregateState AggregateState { get; private set; } = null!;

    public async Task Initialise(EventStoreIntegrationTest test)
    {
        _test = test;
        SetupRepositories();
        _loadedAggregate = await LoadLatestStateFromEvents();
    }

    public async Task DispatchCommandsAndSaveEvents(int commandCount)
    {
        DispatchCommandsToState(commandCount);
        _aggregateVersion = await _aggregateRepository.SaveAsync(_loadedAggregate);
        var loadedAggregate = await LoadLatestStateFromEvents();
        _loadedAggregate = loadedAggregate;
        AggregateState = _loadedAggregate.State;
    }

    public async Task SaveSnapshot()
    {
        await _aggregateRepository.SaveSnapshotAsync(VersionedAggregateState.Create(AggregateState, _aggregateVersion));
    }

    public async Task<LoadedAggregate<TestAggregateState>> LoadLatestStateFromEvents()
    {
        return await LoadLatestState(AggregateLoadStrategy.UseEventStream);
    }

    public async Task<LoadedAggregate<TestAggregateState>> LoadLatestStateFromSnapshot()
    {
        return await LoadLatestState(AggregateLoadStrategy.UseSnapshot);
    }

    public async Task<LoadedAggregate<TestAggregateState>> LoadLatestStateFromSnapshotIfAvailable()
    {
        return await LoadLatestState(AggregateLoadStrategy.PreferSnapshot);
    }

    private async Task<LoadedAggregate<TestAggregateState>> LoadLatestState(AggregateLoadStrategy loadStrategy)
    {
        var loadedAggregate =
            await _aggregateRepository.LoadAsync<TestAggregateState>(_id.ToString(), loadStrategy);

        _aggregateVersion = loadedAggregate.Version;
        AggregateState = loadedAggregate.State;

        return loadedAggregate;
    }

    private void DispatchCommandsToState(int commandCount)
    {
        var commands = Enumerable.Range(1, commandCount).Select(_ => new TestCommand(1));

        foreach (var command in commands)
        {
            _loadedAggregate.ExecuteCommand(_ => TestAggregateFunctions.Execute(command));
        }
    }

    private void SetupRepositories()
    {
        var modelBuilder = new ModelBuilder();
        TestAggregateFunctions.BuildModel(modelBuilder, _id);
        var model = modelBuilder.Build();

        var serializer = new JsonBytesEventDataSerializer();
        var adapter = new EventStoreEventAdapter(serializer);
        var eventConverter = EventConverter.Create(model.EventNameMap, adapter);
        var snapshotRepository = new EventStoreSnapshotRepository(_test.EventStoreClient, eventConverter);
        var eventsRepository = new EventStoreEventsRepository(_test.EventStoreClient, eventConverter);

        _aggregateRepository = new AggregateRepository(eventsRepository, snapshotRepository, model);
    }
}