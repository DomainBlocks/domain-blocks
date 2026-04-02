using DomainBlocks.EventStore.Abstractions;
using DomainBlocks.EventStore.MongoDB.Schema;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using static DomainBlocks.EventStore.MongoDB.Schema.ExpectedStreamStateSchema;

namespace DomainBlocks.EventStore.MongoDB.Serialization;

public sealed class ExpectedStreamStateBsonSerializer : StructSerializerBase<ExpectedStreamState>
{
    public override void Serialize(
        BsonSerializationContext context,
        BsonSerializationArgs args,
        ExpectedStreamState value)
    {
        var writer = context.Writer;

        writer.WriteStartDocument();
        writer.WriteName(ExpectedStreamStateSchema.FieldNames.Kind);

        switch (value.Kind)
        {
            case ExpectedStreamStateKind.Any:
                writer.WriteString(ExpectedStreamStateSchema.Kinds.Any);
                break;

            case ExpectedStreamStateKind.StreamExists:
                writer.WriteString(ExpectedStreamStateSchema.Kinds.StreamExists);
                break;

            case ExpectedStreamStateKind.StreamDoesNotExist:
                writer.WriteString(ExpectedStreamStateSchema.Kinds.StreamDoesNotExist);
                break;

            case ExpectedStreamStateKind.SpecificVersion:
                writer.WriteString(ExpectedStreamStateSchema.Kinds.SpecificVersion);
                writer.WriteName(ExpectedStreamStateSchema.FieldNames.Version);
                writer.WriteInt64(checked((long)value.Version!.Value.Value));
                break;

            default:
                throw new BsonSerializationException($"Unknown {nameof(ExpectedStreamStateKind)}: {value.Kind}");
        }

        writer.WriteEndDocument();
    }

    public override ExpectedStreamState Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
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
                case ExpectedStreamStateSchema.FieldNames.Kind:
                    kind = reader.ReadString();
                    break;
                case ExpectedStreamStateSchema.FieldNames.Version:
                    version = reader.GetCurrentBsonType() switch
                    {
                        BsonType.Int64 => checked((ulong)reader.ReadInt64()),

                        var t => throw new BsonSerializationException(
                            $"Unexpected BSON type for '{ExpectedStreamStateSchema.FieldNames.Version}': {t}")
                    };
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndDocument();

        if (kind is null)
            throw new BsonSerializationException($"Missing '{ExpectedStreamStateSchema.FieldNames.Kind}' field.");

        return kind switch
        {
            ExpectedStreamStateSchema.Kinds.Any => ExpectedStreamState.Any,

            ExpectedStreamStateSchema.Kinds.StreamExists => ExpectedStreamState.StreamExists,

            ExpectedStreamStateSchema.Kinds.StreamDoesNotExist => ExpectedStreamState.StreamDoesNotExist,

            ExpectedStreamStateSchema.Kinds.SpecificVersion when version.HasValue =>
                ExpectedStreamState.SpecificVersion(new StreamVersion(version.Value)),

            ExpectedStreamStateSchema.Kinds.SpecificVersion => throw new BsonSerializationException(
                $"Missing '{ExpectedStreamStateSchema.FieldNames.Version}' for kind '{ExpectedStreamStateSchema.Kinds.SpecificVersion}'."),

            _ => throw new BsonSerializationException($"Unknown kind: {kind}")
        };
    }
}