using DomainBlocks.Core.Persistence;
using DomainBlocks.Core.Serialization;
using DomainBlocks.Logging;
using EventStore.Client;
using Microsoft.Extensions.Logging;

namespace DomainBlocks.EventStore.Persistence;

public class EventStoreSnapshotRepository : ISnapshotRepository
{
    private const string SnapshotVersionMetadataKey = "SnapshotVersion";
    private const string SnapshotEventName = "Snapshot";

    private static readonly ILogger<EventStoreSnapshotRepository> Logger =
        LogProvider.Get<EventStoreSnapshotRepository>();

    private readonly EventStoreClient _client;
    private readonly IEventConverter<ResolvedEvent, EventData> _eventConverter;

    public EventStoreSnapshotRepository(EventStoreClient client, IEventConverter<ResolvedEvent, EventData> eventAdapter)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _eventConverter = eventAdapter ?? throw new ArgumentNullException(nameof(eventAdapter));
    }

    public async Task SaveSnapshotAsync<TState>(
        string snapshotKey,
        long snapshotVersion,
        TState snapshotState,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (snapshotKey == null) throw new ArgumentNullException(nameof(snapshotKey));
            if (snapshotState == null) throw new ArgumentNullException(nameof(snapshotState));

            var snapshotData = new[]
            {
                _eventConverter.SerializeToWriteEvent(
                    snapshotState,
                    SnapshotEventName,
                    KeyValuePair.Create(SnapshotVersionMetadataKey, snapshotVersion.ToString()))
            };

            await _client.AppendToStreamAsync(
                snapshotKey,
                StreamState.Any,
                snapshotData,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Error when attempting to save snapshot. Stream Name {StreamName}. " +
                "Snapshot Version {SnapshotVersion}, Snapshot Type {SnapshotType}",
                snapshotKey, snapshotVersion, typeof(TState).FullName);

            throw;
        }
    }

    public async Task<(bool isSuccess, Snapshot<TState>? snapshot)> TryLoadSnapshotAsync<TState>(
        string snapshotKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (snapshotKey == null) throw new ArgumentNullException(nameof(snapshotKey));

            var readStreamResult = _client.ReadStreamAsync(
                Direction.Backwards,
                snapshotKey,
                StreamPosition.End,
                1,
                cancellationToken: cancellationToken);

            var readState = await readStreamResult.ReadState;
            await using var asyncEnumerator = readStreamResult.GetAsyncEnumerator(cancellationToken);

            if (readState == ReadState.StreamNotFound || !await asyncEnumerator.MoveNextAsync())
            {
                return (false, null);
            }

            var eventRecord = asyncEnumerator.Current;
            var snapshotState =
                (TState)await _eventConverter.DeserializeEvent(eventRecord, typeof(TState), cancellationToken);
            var metadata = _eventConverter.DeserializeMetadata(eventRecord);
            var snapshotVersion = long.Parse(metadata[SnapshotVersionMetadataKey]);

            return (true, new Snapshot<TState>(snapshotState, snapshotVersion));
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Error when attempting to load snapshot. Stream Name: {StreamName}. Snapshot Type {SnapshotType}",
                snapshotKey,
                typeof(TState).FullName);

            throw;
        }
    }
}