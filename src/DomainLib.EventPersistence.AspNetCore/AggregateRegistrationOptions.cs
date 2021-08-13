using System;
using DomainLib.Serialization;

namespace DomainLib.Persistence.AspNetCore
{
    public class AggregateRegistrationOptions<TRawData>
    {
        public AggregateRegistrationOptions(IEventsRepository<TRawData> eventsRepository, ISnapshotRepository snapshotRepository, IEventSerializer<TRawData> eventSerializer)
        {
            EventsRepository = eventsRepository ?? throw new ArgumentNullException(nameof(eventsRepository));
            SnapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
            EventSerializer = eventSerializer ?? throw new ArgumentNullException(nameof(eventSerializer));
        }

        public IEventsRepository<TRawData> EventsRepository { get; }
        public ISnapshotRepository SnapshotRepository { get; }
        public IEventSerializer<TRawData> EventSerializer { get; }
    }
}