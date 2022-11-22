using System;
using DomainBlocks.Persistence.New;

namespace DomainBlocks.Persistence.SqlStreamStore.New;

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
                var eventSerializer = streamStoreOptions.GetOrCreateEventSerializer(model.EventNameMap);
                var eventsRepository = new SqlStreamStoreEventsRepository(streamStore, eventSerializer);
                var snapshotRepository = optionsBuilder.Options.CreateSnapshotRepository(model.EventNameMap);
                return new AggregateRepository<string>(eventsRepository, snapshotRepository, model);
            });

        // This can be made configurable via the builder if we need to.
        ((IAggregateRepositoryOptionsBuilderInfrastructure)optionsBuilder)
            .WithSnapshotRepositoryFactory(eventNameMap =>
            {
                var streamStore = streamStoreOptions.GetOrCreateStreamStore();
                var eventSerializer = streamStoreOptions.GetOrCreateEventSerializer(eventNameMap);
                return new SqlStreamStoreSnapshotRepository(streamStore, eventSerializer);
            });

        return optionsBuilder;
    }
}