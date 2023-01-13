using DomainBlocks.Core.Persistence;
using DomainBlocks.Core.Serialization;
using DomainBlocks.ThirdParty.SqlStreamStore;
using DomainBlocks.ThirdParty.SqlStreamStore.Streams;
using DomainBlocks.Logging;
using Microsoft.Extensions.Logging;
using StreamVersion = DomainBlocks.ThirdParty.SqlStreamStore.Streams.StreamVersion;

namespace DomainBlocks.SqlStreamStore.Persistence;

public class SqlStreamStoreSnapshotRepository : ISnapshotRepository
{
    private const string SnapshotVersionMetadataKey = "SnapshotVersion";
    private const string SnapshotEventName = "Snapshot";

    private static readonly ILogger<SqlStreamStoreSnapshotRepository> Log =
        Logger.CreateFor<SqlStreamStoreSnapshotRepository>();

    private readonly IStreamStore _streamStore;
    private readonly IEventConverter<StreamMessage, NewStreamMessage> _eventAdapter;

    public SqlStreamStoreSnapshotRepository(
        IStreamStore streamStore,
        IEventConverter<StreamMessage, NewStreamMessage> eventAdapter)
    {
        _streamStore = streamStore;
        _eventAdapter = eventAdapter;
    }

    public async Task SaveSnapshotAsync<TState>(
        string snapshotKey,
        long snapshotVersion,
        TState snapshotState,
        CancellationToken cancellationToken = default)
    {
        if (snapshotKey == null) throw new ArgumentNullException(nameof(snapshotKey));
        if (snapshotState == null) throw new ArgumentNullException(nameof(snapshotState));

        try
        {
            var messages = new[]
            {
                _eventAdapter.SerializeToWriteEvent(
                    snapshotState,
                    SnapshotEventName,
                    KeyValuePair.Create(SnapshotVersionMetadataKey, snapshotVersion.ToString()))
            };

            await _streamStore.AppendToStream(snapshotKey, ExpectedVersion.Any, messages, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.LogError(
                ex,
                "Error when attempting to save snapshot. Stream Name {StreamName}. " +
                "Snapshot Version {SnapshotVersion}, Snapshot Type {SnapshotType}",
                snapshotKey,
                snapshotVersion,
                typeof(TState).FullName);

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

            var readStreamPage = await _streamStore.ReadStreamBackwards(
                snapshotKey,
                StreamVersion.End,
                1,
                cancellationToken: cancellationToken);

            var messages = readStreamPage.Messages;

            if (messages is not { Length: 1 })
            {
                return (false, null);
            }

            var snapshotMessage = messages[0];

            var snapshotState =
                (TState)await _eventAdapter.DeserializeEvent(snapshotMessage, typeof(TState), cancellationToken);

            var metadata = _eventAdapter.DeserializeMetadata(snapshotMessage);
            var snapshotVersion = long.Parse(metadata[SnapshotVersionMetadataKey]);

            return (true, new Snapshot<TState>(snapshotState, snapshotVersion));
        }
        catch (Exception ex)
        {
            Log.LogError(
                ex,
                "Error when attempting to load snapshot. Stream Name: {StreamName}. Snapshot Type {SnapshotType}",
                snapshotKey,
                typeof(TState).FullName);

            throw;
        }
    }
}