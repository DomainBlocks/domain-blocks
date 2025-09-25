namespace DomainBlocks.EventStore.KurrentDB;

public static class SerializationExtensions
{
    public static ReadOnlyMemory<byte> SerializeToUtf8Json(this IReadOnlyDictionary<string, string> dictionary) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(dictionary);
}