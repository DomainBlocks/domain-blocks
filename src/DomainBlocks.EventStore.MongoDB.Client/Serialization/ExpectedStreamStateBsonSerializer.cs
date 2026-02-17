using System.Globalization;
using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace DomainBlocks.EventStore.MongoDB.Client.Serialization;

public sealed class ExpectedStreamStateBsonSerializer : StructSerializerBase<ExpectedStreamState>
{
    private const string KindField = "kind";
    private const string VersionField = "version";

    // Wire tokens – keep stable forever once shipped
    private const string Any = "any";
    private const string StreamExists = "streamExists";
    private const string StreamDoesNotExist = "streamDoesNotExist";
    private const string Version = "version";

    public override void Serialize(
        BsonSerializationContext context,
        BsonSerializationArgs args,
        ExpectedStreamState value)
    {
        var writer = context.Writer;

        writer.WriteStartDocument();
        writer.WriteName(KindField);

        switch (value.Kind)
        {
            case ExpectedStreamStateKind.Any:
                writer.WriteString(Any);
                break;

            case ExpectedStreamStateKind.StreamExists:
                writer.WriteString(StreamExists);
                break;

            case ExpectedStreamStateKind.StreamDoesNotExist:
                writer.WriteString(StreamDoesNotExist);
                break;

            case ExpectedStreamStateKind.SpecificVersion:
                writer.WriteString(Version);

                writer.WriteName(VersionField);

                // Option A: Int64 (compact). Requires version <= long.MaxValue.
                writer.WriteInt64(checked((long)value.Version!.Value.Value));

                // Option B: String (full ulong range, very stable):
                // w.WriteString(value.Version!.Value.Value.ToString(CultureInfo.InvariantCulture));
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
                case KindField:
                    kind = reader.ReadString();
                    break;
                case VersionField:
                    version = reader.GetCurrentBsonType() switch
                    {
                        BsonType.Int64 => checked((ulong)reader.ReadInt64()),
                        BsonType.Int32 => checked((ulong)reader.ReadInt32()),
                        BsonType.String => ulong.Parse(reader.ReadString(), CultureInfo.InvariantCulture),
                        var t => throw new BsonSerializationException($"Unexpected BSON type for '{VersionField}': {t}")
                    };
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndDocument();

        if (kind is null)
            throw new BsonSerializationException($"Missing '{KindField}' field.");

        return kind switch
        {
            Any => ExpectedStreamState.Any,
            StreamExists => ExpectedStreamState.StreamExists,
            StreamDoesNotExist => ExpectedStreamState.StreamDoesNotExist,
            Version when version.HasValue => ExpectedStreamState.SpecificVersion(new StreamVersion(version.Value)),
            Version => throw new BsonSerializationException($"Missing '{VersionField}' for kind '{Version}'."),
            _ => throw new BsonSerializationException($"Unknown expected stream state kind '{kind}'.")
        };
    }
}