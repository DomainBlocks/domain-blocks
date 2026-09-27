using System.Reflection;

namespace DomainBlocks.Serialization.Abstractions;

/// <summary>
/// Serializes objects to, and deserializes them from, a data representation of type <typeparamref name="TData"/>.
/// </summary>
public interface IObjectSerializer<TData>
{
    TData Serialize(object value);

    object Deserialize(TData data, Type type);

    /// <summary>
    /// The name that a member of <paramref name="type"/> is stored under, where whoever holds the data can look it up,
    /// if the serializer writes the member's value as it would any value of that type. Otherwise
    /// <see langword="null"/>, which is the answer of a serializer whose data cannot be looked into.
    /// </summary>
    string? GetStoredName(Type type, MemberInfo member) => null;
}