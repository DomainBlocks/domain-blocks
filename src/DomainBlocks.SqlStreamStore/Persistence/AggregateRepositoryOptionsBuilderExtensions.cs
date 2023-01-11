using DomainBlocks.Core.Persistence;
using DomainBlocks.Core.Serialization;
using DomainBlocks.SqlStreamStore.Serialization;

namespace DomainBlocks.SqlStreamStore.Persistence;

public static class AggregateRepositoryOptionsBuilderExtensions
{
    public static AggregateRepositoryOptionsBuilder UseSqlStreamStore(
        this AggregateRepositoryOptionsBuilder optionsBuilder,
        Action<SqlStreamStoreOptionsBuilder> optionsAction)
    {
        if (optionsBuilder == null) throw new ArgumentNullException(nameof(optionsBuilder));
        if (optionsAction == null) throw new ArgumentNullException(nameof(optionsAction));

        var streamStoreOptionsBuilder = new SqlStreamStoreOptionsBuilder();
        optionsAction(streamStoreOptionsBuilder);
        var streamStoreOptions = streamStoreOptionsBuilder.Options;

        ((IAggregateRepositoryOptionsBuilderInfrastructure)optionsBuilder)
            .WithAggregateRepositoryFactory(model =>
            {
                var streamStore = streamStoreOptions.GetOrCreateStreamStore();
                var eventSerializer = streamStoreOptions.GetEventDataSerializer();
                var eventAdapter = new SqlStreamStoreEventAdapter(eventSerializer);
                var eventConverter = EventConverter.Create(model.EventNameMap, eventAdapter);
                var eventsRepository = new SqlStreamStoreEventsRepository(streamStore, eventConverter);
                var snapshotRepository = optionsBuilder.Options.CreateSnapshotRepository(model);
                return new AggregateRepository(eventsRepository, snapshotRepository, model);
            });

        // This can be made configurable via the builder if we need to.
        ((IAggregateRepositoryOptionsBuilderInfrastructure)optionsBuilder)
            .WithSnapshotRepositoryFactory(model =>
            {
                var streamStore = streamStoreOptions.GetOrCreateStreamStore();
                var eventSerializer = streamStoreOptions.GetEventDataSerializer();
                var eventAdapter = new SqlStreamStoreEventAdapter(eventSerializer);
                var eventConverter = EventConverter.Create(model.EventNameMap, eventAdapter);
                return new SqlStreamStoreSnapshotRepository(streamStore, eventConverter);
            });

        return optionsBuilder;
    }
}