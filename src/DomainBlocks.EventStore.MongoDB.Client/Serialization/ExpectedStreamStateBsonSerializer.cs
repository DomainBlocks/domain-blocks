using System.Globalization;
using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace DomainBlocks.EventStore.MongoDB.Client.Serialization;

public sealed class ExpectedStreamStateBsonSerializer : StructSerializerBase<ExpectedStreamState>
{
    public static readonly ExpectedStreamStateBsonSerializer Shared = new();

    private const string KindFieldName = "kind";
    private const string VersionFieldName = "version";
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
        writer.WriteName(KindFieldName);

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
                writer.WriteName(VersionFieldName);
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
                case KindFieldName:
                    kind = reader.ReadString();
                    break;
                case VersionFieldName:
                    version = reader.GetCurrentBsonType() switch
                    {
                        BsonType.Int64 => checked((ulong)reader.ReadInt64()),
                        BsonType.Int32 => checked((ulong)reader.ReadInt32()),
                        BsonType.String => ulong.Parse(reader.ReadString(), CultureInfo.InvariantCulture),
                        var t => throw new BsonSerializationException(
                            $"Unexpected BSON type for '{VersionFieldName}': {t}")
                    };
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndDocument();

        if (kind is null)
            throw new BsonSerializationException($"Missing '{KindFieldName}' field.");

        return kind switch
        {
            Any => ExpectedStreamState.Any,
            StreamExists => ExpectedStreamState.StreamExists,
            StreamDoesNotExist => ExpectedStreamState.StreamDoesNotExist,
            Version when version.HasValue => ExpectedStreamState.SpecificVersion(new StreamVersion(version.Value)),
            Version => throw new BsonSerializationException($"Missing '{VersionFieldName}' for kind '{Version}'."),
            _ => throw new BsonSerializationException($"Unknown expected kind '{kind}'.")
        };
    }
}