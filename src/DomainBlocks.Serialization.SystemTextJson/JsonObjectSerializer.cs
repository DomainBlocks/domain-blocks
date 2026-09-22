using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DomainBlocks.Serialization.Abstractions;

namespace DomainBlocks.Serialization.SystemTextJson;

public sealed class JsonObjectSerializer(JsonSerializerOptions? options = null) : IObjectSerializer<string>
{
    public string Serialize(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, options);
        }
        catch (Exception ex)
        {
            throw ObjectSerializationException.SerializationFailed(value.GetType(), ex);
        }
    }

    public object Deserialize(string data, Type type)
    {
        object? result;

        try
        {
            result = JsonSerializer.Deserialize(data, type, options);
        }
        catch (Exception ex)
        {
            throw ObjectSerializationException.DeserializationFailed(type, ex);
        }

        return result ?? throw ObjectSerializationException.DeserializationReturnedNull(type);
    }

    /// <summary>
    /// The name of the JSON property, for a type that is written as an object of its properties, and a value that a
    /// converter of System.Text.Json writes and reads back. What a converter of the caller's writes is for it alone
    /// to read, and a value that is written but not read back says nothing of the object that is read.
    /// </summary>
    public string? GetStoredName(Type type, MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);

        var effectiveOptions = options ?? JsonSerializerOptions.Default;

        // As the first use of the options to serialize would leave them.
        if (!effectiveOptions.IsReadOnly)
            effectiveOptions.MakeReadOnly(populateMissingResolver: true);

        if (!effectiveOptions.TryGetTypeInfo(type, out var typeInfo) || typeInfo.Kind != JsonTypeInfoKind.Object)
            return null;

        var property = typeInfo.Properties.FirstOrDefault(x => (x.AttributeProvider as MemberInfo)?.Name == member.Name);

        if (property is not { Get: not null, CustomConverter: null })
            return null;

        // Read back by a setter, or by the constructor.
        if (property is { Set: null, AssociatedParameter: null })
            return null;

        // The converter of a nullable is the library's own, whichever converter writes the value inside it.
        var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        return effectiveOptions.TryGetTypeInfo(valueType, out var valueInfo) &&
               valueInfo.Converter.GetType().Assembly == typeof(JsonSerializer).Assembly
            ? property.Name
            : null;
    }
}