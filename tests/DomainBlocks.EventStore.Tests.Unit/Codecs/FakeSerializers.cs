using DomainBlocks.Serialization.Abstractions;
using Shouldly;

namespace DomainBlocks.EventStore.Tests.Unit.Codecs;

/// <summary>
/// Writes "TypeName:OrderId" for record types with a single string OrderId constructor parameter.
/// </summary>
internal sealed class FakeObjectSerializer : IObjectSerializer<string>
{
    public string Serialize(object value) =>
        $"{value.GetType().Name}:{value.GetType().GetProperty("OrderId")!.GetValue(value)}";

    public object Deserialize(string data, Type type)
    {
        var parts = data.Split(':');
        parts[0].ShouldBe(type.Name);
        return Activator.CreateInstance(type, parts[1])!;
    }
}

/// <summary>
/// Writes "k=v;k=v".
/// </summary>
internal sealed class FakeMetadataSerializer : IMetadataSerializer<string>
{
    public string Serialize(ReadOnlySpan<KeyValuePair<string, string>> metadata)
    {
        var parts = new string[metadata.Length];

        for (var i = 0; i < metadata.Length; i++)
            parts[i] = $"{metadata[i].Key}={metadata[i].Value}";

        return string.Join(';', parts);
    }

    public IReadOnlyDictionary<string, string> Deserialize(string data) =>
        data.Split(';').Select(x => x.Split('=')).ToDictionary(x => x[0], x => x[1]);
}