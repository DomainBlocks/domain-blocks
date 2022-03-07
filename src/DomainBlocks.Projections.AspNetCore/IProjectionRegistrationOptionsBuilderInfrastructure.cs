using System;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.AspNetCore
{
    public interface IProjectionRegistrationOptionsBuilderInfrastructure
    {
        IConfiguration Configuration { get; }
        IServiceCollection ServiceCollection { get; }

        ProjectionRegistrationOptionsBuilder<TRawData> TypedAs<TRawData>();

        ProjectionRegistrationOptions<TRawData> Build<TRawData>(IServiceProvider serviceProvider);
    }

    public interface IProjectionRegistrationOptionsBuilderInfrastructure<TRawData> : IProjectionRegistrationOptionsBuilderInfrastructure
    {
        ProjectionRegistrationOptionsBuilder<TRawData> UseEventPublisher(
            Func<IServiceProvider, IEventPublisher<TRawData>> getEventPublisher);

        ProjectionRegistrationOptionsBuilder<TRawData> UseEventDeserializer(
            Func<IServiceProvider, IEventDeserializer<TRawData>> getEventDeserializer);

        ProjectionRegistrationOptions<TRawData> Build(IServiceProvider serviceProvider);

        ProjectionRegistrationOptionsBuilder<TRawData> UseProjectionRegistrations(
            Action<IServiceProvider, ProjectionRegistryBuilder> onRegisteringProjections);
    }
}