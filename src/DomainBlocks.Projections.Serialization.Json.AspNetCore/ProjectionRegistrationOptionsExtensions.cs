using System;
using DomainBlocks.Projections.AspNetCore;
using DomainBlocks.Serialization.Json;

namespace DomainBlocks.Projections.Serialization.Json.AspNetCore
{
    public static class ProjectionRegistrationOptionsExtensions
    {
        public static IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseJsonDeserialization(
            this IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder)
        {
            builder.AddEventDeserializer(_ => new JsonEventDeserializer());
            return builder;
        }
    }
}