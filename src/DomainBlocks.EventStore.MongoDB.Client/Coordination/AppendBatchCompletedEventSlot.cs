using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class AppendBatchCompletedEventSlot
{
    private readonly BsonDocument _event;
    private readonly BsonArray _appendedCommitIdsBsonArray = [];
    private readonly BsonArray _duplicateCommitIdsBsonArray = [];
    private readonly BsonArray _committedRejectionsBsonArray = [];

    public AppendBatchCompletedEventSlot()
    {
        var eventPayload = new BsonDocument
        {
            { AppendBatchCompleted.FieldNames.AppendedCommitIds, _appendedCommitIdsBsonArray },
            { AppendBatchCompleted.FieldNames.DuplicateCommitIds, _duplicateCommitIdsBsonArray },
            { AppendBatchCompleted.FieldNames.Rejections, _committedRejectionsBsonArray }
        };

        _event = new BsonDocument
        {
            { PendingEvent.FieldNames.EventName, "AppendBatchCompleted" },
            { PendingEvent.FieldNames.EventData, eventPayload },
            { PendingEvent.FieldNames.Metadata, BsonNull.Value }
        };
    }

    public BsonDocument Fill(
        IEnumerable<Guid> appendedCommitIds,
        IEnumerable<Guid> duplicateCommitIds,
        IEnumerable<BsonValue> commitRejections)
    {
        _appendedCommitIdsBsonArray.Clear();
        _duplicateCommitIdsBsonArray.Clear();
        _committedRejectionsBsonArray.Clear();

        foreach (var id in appendedCommitIds)
            _appendedCommitIdsBsonArray.Add(new BsonBinaryData(id, GuidRepresentation.Standard));

        foreach (var id in duplicateCommitIds)
            _duplicateCommitIdsBsonArray.Add(new BsonBinaryData(id, GuidRepresentation.Standard));

        _committedRejectionsBsonArray.AddRange(commitRejections);

        return _event;
    }
}