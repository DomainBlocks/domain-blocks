using System;
using DomainBlocks.Serialization;

namespace DomainBlocks.Persistence.AspNetCore;

public class AggregateRepositoryOptions<TRawData>
{
    public AggregateRepositoryOptions(IEventsRepository<TRawData> eventsRepository,
        ISnapshotRepository snapshotRepository, IEventSerializer<TRawData> eventSerializer)
    {
        EventsRepository = eventsRepository ?? throw new ArgumentNullException(nameof(eventsRepository));
        SnapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
        EventSerializer = eventSerializer ?? throw new ArgumentNullException(nameof(eventSerializer));
    }

    // Note: These properties are currently accessed via a dynamic object.
    // See DomainBlocks.Persistence.AspNetCore.AggregateServiceCollectionExtensions.AddAggregateRepository
    // ReSharper disable MemberCanBePrivate.Global, UnusedAutoPropertyAccessor.Global
    public IEventsRepository<TRawData> EventsRepository { get; }
    public ISnapshotRepository SnapshotRepository { get; }
    public IEventSerializer<TRawData> EventSerializer { get; }
    // ReSharper restore MemberCanBePrivate.Global, UnusedAutoPropertyAccessor.Global
}