using System;
using System.Text.Json;
using DomainLib.Persistence.AspNetCore;

namespace DomainLib.Serialization.Json.AspNetCore
{
    public static class AggregateRegistrationOptionsExtensions
    {
        public static IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseJsonSerialization(
            this IAggregateRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder, JsonSerializerOptions serializerOptions = null)
        {
            builder.AddEventSerializer(eventNameMap => new JsonBytesEventSerializer(eventNameMap, serializerOptions));
            return builder;
        }
    }
}
