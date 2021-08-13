using System;
using DomainLib.Projections.AspNetCore;
using DomainLib.Serialization.Json;

namespace DomainLib.Projections.Serialization.Json.AspNetCore
{
    public static class ProjectionRegistrationOptionsExtensions
    {
        public static IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> UseJsonDeserialization(
            this IProjectionRegistrationOptionsBuilderInfrastructure<ReadOnlyMemory<byte>> builder)
        {
            builder.AddEventDeserializer(() => new JsonEventDeserializer());
            return builder;
        }
    }
}