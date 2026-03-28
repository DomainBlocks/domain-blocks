using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class EventWriteSlot
{
    private readonly BsonDocument _filter;
    private readonly BsonDocument _set;
    private readonly UpdateOneModel<BsonDocument> _model;

    public EventWriteSlot(long epoch)
    {
        _filter = new BsonDocument
        {
            { "_id", BsonNull.Value },
            { EventLogEntry.FieldNames.Epoch, new BsonDocument("$lt", epoch) }
        };

        _set = new BsonDocument
        {
            { "_id", BsonNull.Value },
            { EventLogEntry.FieldNames.Epoch, epoch },
            { EventLogEntry.FieldNames.StreamId, BsonNull.Value },
            { EventLogEntry.FieldNames.StreamVersion, BsonNull.Value },
            { EventLogEntry.FieldNames.CommitId, BsonNull.Value },
            { EventLogEntry.FieldNames.EventName, BsonNull.Value },
            { EventLogEntry.FieldNames.EventData, BsonNull.Value },
            { EventLogEntry.FieldNames.Metadata, BsonNull.Value }
        };

        var update = new BsonDocument
        {
            { "$set", _set },
            { "$currentDate", new BsonDocument(EventLogEntry.FieldNames.WrittenAtUtc, true) }
        };

        _model = new UpdateOneModel<BsonDocument>(
            new BsonDocumentFilterDefinition<BsonDocument>(_filter),
            new BsonDocumentUpdateDefinition<BsonDocument>(update))
        {
            IsUpsert = true
        };
    }

    public UpdateOneModel<BsonDocument> Fill(
        long position,
        BsonValue streamId,
        long streamVersion,
        BsonValue commitId,
        BsonDocument @event)
    {
        _filter["_id"] = position;

        _set["_id"] = position;
        _set[EventLogEntry.FieldNames.StreamId] = streamId;
        _set[EventLogEntry.FieldNames.StreamVersion] = streamVersion;
        _set[EventLogEntry.FieldNames.CommitId] = commitId;
        _set[EventLogEntry.FieldNames.EventName] = @event[PendingEvent.FieldNames.EventName];
        _set[EventLogEntry.FieldNames.EventData] = @event[PendingEvent.FieldNames.EventData];
        _set[EventLogEntry.FieldNames.Metadata] = @event[PendingEvent.FieldNames.Metadata];

        return _model;
    }
}