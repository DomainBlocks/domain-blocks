using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Client.Schema2;
using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB.Client.Coordination;

public sealed class CommitRejectionSlot
{
    private readonly BsonDocument _actualStreamState;
    private readonly BsonDocument _rejection;

    public CommitRejectionSlot()
    {
        _actualStreamState = new BsonDocument { { "kind", BsonNull.Value } };

        _rejection = new BsonDocument
        {
            { CommitRejection.FieldNames.CommitId, BsonNull.Value },
            { CommitRejection.FieldNames.StreamId, BsonNull.Value },
            { CommitRejection.FieldNames.ExpectedStreamState, BsonNull.Value },
            { CommitRejection.FieldNames.ActualStreamState, _actualStreamState }
        };
    }

    public BsonDocument Fill(
        Guid commitId,
        BsonValue streamId,
        BsonValue expectedStreamState,
        StreamState actualStreamState)
    {
        _rejection[CommitRejection.FieldNames.CommitId] = new BsonBinaryData(commitId, GuidRepresentation.Standard);
        _rejection[CommitRejection.FieldNames.StreamId] = streamId;
        _rejection[CommitRejection.FieldNames.ExpectedStreamState] = expectedStreamState;

        FillActualStreamState(actualStreamState);

        return _rejection;
    }

    private void FillActualStreamState(StreamState value)
    {
        if (value.IsStreamDoesNotExist)
        {
            _actualStreamState["kind"] = "streamDoesNotExist";
            _actualStreamState.Remove("version");
        }
        else if (value.IsStreamExists)
        {
            _actualStreamState["kind"] = "streamExists";
            _actualStreamState["version"] = checked((long)value.Version.Value.Value);
        }
        else
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), $"Unknown {nameof(StreamStateKind)}: {value.Kind}");
        }
    }
}