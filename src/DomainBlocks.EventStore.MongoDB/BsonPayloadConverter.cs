using MongoDB.Bson;

namespace DomainBlocks.EventStore.MongoDB;

/// <summary>
/// Provides conversion between supported payload types and <see cref="BsonValue"/>.
/// </summary>
/// <remarks>
/// Supported payload types are:
/// <list type="bullet">
///   <item><see cref="BsonDocument"/></item>
///   <item><see cref="byte[]"/></item>
///   <item><see cref="string"/></item>
/// </list>
/// Any other type will result in a <see cref="NotSupportedException"/>.
/// </remarks>
public static class BsonPayloadConverter
{
    /// <summary>
    /// Converts a payload to a <see cref="BsonValue"/>.
    /// </summary>
    /// <param name="payload">
    /// The payload to convert. Must be a <see cref="BsonDocument"/>, <see cref="byte[]"/>, or <see cref="string"/>.
    /// </param>
    /// <returns>A <see cref="BsonValue"/> representation of the payload.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown if <paramref name="payload"/> is not of a supported type.
    /// </exception>
    public static BsonValue ToBsonValue(object payload)
    {
        return payload switch
        {
            BsonDocument p => p,
            byte[] p => p,
            string p => p,
            _ => throw GetNotSupportedException(payload.GetType())
        };
    }

    /// <summary>
    /// Converts a <see cref="BsonValue"/> to the specified payload type.
    /// </summary>
    /// <typeparam name="TPayload">
    /// The expected payload type. Must be <see cref="BsonDocument"/>, <see cref="byte[]"/>, or <see cref="string"/>.
    /// </typeparam>
    /// <param name="bsonValue">The <see cref="BsonValue"/> to convert.</param>
    /// <returns>A <typeparamref name="TPayload"/> representation of the payload.</returns>
    /// <exception cref="InvalidCastException">
    /// Thrown if the BSON value cannot be converted to <typeparamref name="TPayload"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown if <typeparamref name="TPayload"/> is not a supported type.
    /// </exception>
    public static TPayload FromBsonValue<TPayload>(BsonValue bsonValue)
    {
        var payloadType = typeof(TPayload);

        object payload = payloadType switch
        {
            _ when payloadType == typeof(BsonDocument) => bsonValue.AsBsonDocument,
            _ when payloadType == typeof(byte[]) => bsonValue.AsByteArray,
            _ when payloadType == typeof(string) => bsonValue.AsString,
            _ => throw GetNotSupportedException(payloadType)
        };

        return (TPayload)payload;
    }

    private static NotSupportedException GetNotSupportedException(Type payloadType)
    {
        return new NotSupportedException($"Unsupported payload type '{payloadType}'.");
    }
}