using System;
using System.Text.Json;
using DomainBlocks.Persistence.AspNetCore;

namespace DomainBlocks.Serialization.Json.AspNetCore
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
