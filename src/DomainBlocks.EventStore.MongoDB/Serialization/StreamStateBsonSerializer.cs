using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using static DomainBlocks.EventStore.MongoDB.Schema.ExpectedStreamStateSchema;

namespace DomainBlocks.EventStore.MongoDB.Serialization;

public sealed class StreamStateBsonSerializer : StructSerializerBase<StreamState>
{
    public override void Serialize(
        BsonSerializationContext context,
        BsonSerializationArgs args,
        StreamState value)
    {
        var writer = context.Writer;

        writer.WriteStartDocument();
        writer.WriteName(FieldNames.Kind);

        switch (value.Kind)
        {
            case StreamStateKind.StreamDoesNotExist:
                writer.WriteString(Kinds.StreamDoesNotExist);
                break;

            case StreamStateKind.StreamExists:
                writer.WriteString(Kinds.StreamExists);
                writer.WriteName(FieldNames.Version);
                writer.WriteInt64(checked((long)value.Version!.Value.Value));
                break;

            default:
                throw new BsonSerializationException($"Unknown {nameof(StreamStateKind)}: {value.Kind}");
        }

        writer.WriteEndDocument();
    }

    public override StreamState Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;

        reader.ReadStartDocument();

        string? kind = null;
        ulong? version = null;

        while (reader.ReadBsonType() != BsonType.EndOfDocument)
        {
            var name = reader.ReadName(Utf8NameDecoder.Instance);

            switch (name)
            {
                case FieldNames.Kind:
                    kind = reader.ReadString();
                    break;
                case FieldNames.Version:
                    version = reader.GetCurrentBsonType() switch
                    {
                        BsonType.Int64 => checked((ulong)reader.ReadInt64()),

                        var t => throw new BsonSerializationException(
                            $"Unexpected BSON type for '{FieldNames.Version}': {t}")
                    };
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndDocument();

        if (kind is null)
            throw new BsonSerializationException($"Missing '{FieldNames.Kind}' field.");

        return kind switch
        {
            Kinds.StreamDoesNotExist => StreamState.StreamDoesNotExist,

            Kinds.StreamExists when version.HasValue => StreamState.StreamExists(new StreamVersion(version.Value)),

            Kinds.StreamExists => throw new BsonSerializationException(
                $"Missing '{FieldNames.Version}' for kind '{Kinds.StreamExists}'."),

            _ => throw new BsonSerializationException($"Unknown kind: {kind}")
        };
    }
}