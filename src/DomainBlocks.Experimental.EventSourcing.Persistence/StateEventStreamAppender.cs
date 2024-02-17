namespace DomainBlocks.Experimental.EventSourcing.Persistence;

internal sealed class StateEventStreamAppender<TState, TWriteEvent, TRawData, TStreamVersion> :
    IStateEventStreamAppender
    where TStreamVersion : struct
{
    private readonly string _streamId;
    private readonly TStreamVersion? _expectedVersion;
    private readonly IWriteOnlyEventRepository<TWriteEvent, TStreamVersion> _eventRepository;
    private readonly IWriteEventAdapter<TWriteEvent, TRawData> _eventAdapter;
    private readonly StateEventStreamBinding<TState, TRawData> _binding;
    private readonly List<TWriteEvent> _appendedEvents = new();

    private TState _state;
    private int _eventCountSinceLastSnapshot;

    public StateEventStreamAppender(
        TState state,
        int loadedEventCount,
        string streamId,
        TStreamVersion? expectedVersion,
        IWriteOnlyEventRepository<TWriteEvent, TStreamVersion> eventRepository,
        IWriteEventAdapter<TWriteEvent, TRawData> eventAdapter,
        StateEventStreamBinding<TState, TRawData> binding)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _eventCountSinceLastSnapshot = loadedEventCount;
        _streamId = streamId;
        _expectedVersion = expectedVersion;
        _eventRepository = eventRepository ?? throw new ArgumentNullException(nameof(eventRepository));
        _eventAdapter = eventAdapter ?? throw new ArgumentNullException(nameof(eventAdapter));
        _binding = binding ?? throw new ArgumentNullException(nameof(binding));
    }

    public void Append(IEnumerable<object> events)
    {
        var eventTypes = _binding.EventTypes;
        var serializer = _binding.EventDataSerializer;

        foreach (var e in events)
        {
            var eventType = e.GetType();
            var eventTypeMapping = eventTypes[eventType];

            // TODO: Should we allow upcasting here? Is it permissible to emit an upcasted event?
            _state = eventTypeMapping.EventApplier!.Invoke(_state, e);

            var eventName = eventTypes.GetEventName(eventType);
            var rawData = serializer.Serialize(e);

            TWriteEvent writeEvent;

            if (eventTypeMapping.MetadataFactory != null)
            {
                var metadata = eventTypeMapping.MetadataFactory();
                var rawMetadata = serializer.Serialize(metadata);
                writeEvent = _eventAdapter.CreateWriteEvent(eventName, rawData, rawMetadata, serializer.ContentType);
            }
            else
            {
                writeEvent = _eventAdapter.CreateWriteEvent(eventName, rawData, contentType: serializer.ContentType);
            }

            _appendedEvents.Add(writeEvent);
            _eventCountSinceLastSnapshot++;

            var isSnapshotRequired =
                _binding.SnapshotEventCount.HasValue && _eventCountSinceLastSnapshot >= _binding.SnapshotEventCount;

            if (!isSnapshotRequired) continue;

            var rawSnapshotData = serializer.Serialize(_state!);

            var snapshotEvent = _eventAdapter.CreateWriteEvent(
                SystemEventNames.StateSnapshot, rawSnapshotData, contentType: serializer.ContentType);

            _appendedEvents.Add(snapshotEvent);
            _eventCountSinceLastSnapshot = 0;
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _eventRepository.AppendToStreamAsync(_streamId, _appendedEvents, _expectedVersion, cancellationToken);
    }
}