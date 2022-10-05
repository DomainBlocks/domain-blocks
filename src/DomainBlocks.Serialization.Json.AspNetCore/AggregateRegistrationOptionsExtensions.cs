using System;
using System.Text.Json;
using DomainBlocks.Persistence.AspNetCore;

namespace DomainBlocks.Serialization.Json.AspNetCore;

public static class AggregateRegistrationOptionsExtensions
{
    public static IAggregateRepositoryOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseJsonSerialization(
        this IAggregateRepositoryOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder, JsonSerializerOptions serializerOptions = null)
    {
        builder.AddEventSerializer(eventNameMap => new JsonBytesEventSerializer(eventNameMap, serializerOptions));
        return builder;
    }

    public static IAggregateRepositoryOptionsBuilderInfrastructure<string> UseJsonSerialization(
        this IAggregateRepositoryOptionsBuilderInfrastructure<string> builder, JsonSerializerOptions serializerOptions = null)
    {
        builder.AddEventSerializer(eventNameMap => new JsonStringEventSerializer(eventNameMap, serializerOptions));
        return builder;
    }
}