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

    public interface IProjectionRegistrationOptionsBuilderInfrastructure<TEventBase> : IProjectionRegistrationOptionsBuilderInfrastructure
    {
        ProjectionRegistrationOptionsBuilder<TEventBase> UseEventPublisher(
            Func<IServiceProvider, IEventPublisher<TEventBase>> getEventPublisher);

        ProjectionRegistrationOptionsBuilder<TEventBase> UseEventDeserializer(
            Func<IServiceProvider, IEventDeserializer<TEventBase>> getEventDeserializer);

        ProjectionRegistrationOptions<TEventBase> Build(IServiceProvider serviceProvider);

        ProjectionRegistrationOptionsBuilder<TEventBase> UseProjectionRegistrations(
            Action<IServiceProvider, ProjectionRegistryBuilder> onRegisteringProjections);
    }
}