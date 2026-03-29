using System.Globalization;
using DomainBlocks.EventStore.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace DomainBlocks.EventStore.MongoDB.Client.Serialization;

public sealed class StreamStateBsonSerializer : StructSerializerBase<StreamState>
{
    public static readonly StreamStateBsonSerializer Shared = new();

    private const string KindFieldName = "kind";
    private const string VersionFieldName = "version";
    private const string StreamDoesNotExist = "streamDoesNotExist";
    private const string StreamExists = "streamExists";

    public override void Serialize(
        BsonSerializationContext context,
        BsonSerializationArgs args,
        StreamState value)
    {
        var writer = context.Writer;

        writer.WriteStartDocument();
        writer.WriteName(KindFieldName);

        switch (value.Kind)
        {
            case StreamStateKind.StreamDoesNotExist:
                writer.WriteString(StreamDoesNotExist);
                break;

            case StreamStateKind.StreamExists:
                writer.WriteString(StreamExists);
                writer.WriteName(VersionFieldName);
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
            StreamDoesNotExist => StreamState.StreamDoesNotExist,
            StreamExists when version.HasValue =>
                StreamState.StreamExists(new StreamVersion(version.Value)),
            StreamExists =>
                throw new BsonSerializationException($"Missing '{VersionFieldName}' for kind '{StreamExists}'."),
            _ => throw new BsonSerializationException($"Unknown stream kind '{kind}'.")
        };
    }
}