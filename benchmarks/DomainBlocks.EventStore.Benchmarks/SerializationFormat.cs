namespace DomainBlocks.EventStore.Benchmarks;

/// <summary>
/// The serializer pairing used for event data and metadata.
/// </summary>
public enum SerializationFormat
{
    /// <summary>System.Text.Json to strings, for both event data and metadata.</summary>
    Json,

    /// <summary>System.Text.Json to UTF-8 bytes, for both event data and metadata.</summary>
    JsonUtf8,

    /// <summary>BSON documents, for both event data and metadata.</summary>
    Bson,

    /// <summary>BSON bytes, for both event data and metadata.</summary>
    RawBson,

    /// <summary>Protobuf wire format for event data, with metadata as UTF-8 JSON bytes.</summary>
    Protobuf,

    /// <summary>Protobuf JSON strings for event data, with metadata as a JSON string.</summary>
    ProtobufJson
}