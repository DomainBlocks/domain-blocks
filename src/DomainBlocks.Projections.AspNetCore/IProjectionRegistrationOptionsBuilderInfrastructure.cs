using System;
using DomainBlocks.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainBlocks.Projections.AspNetCore
{
    public interface IProjectionRegistrationOptionsBuilderInfrastructure
    {
        ProjectionRegistrationOptionsBuilder<TRawData> RawEventDataType<TRawData>();

        ProjectionRegistrationOptions<TRawData> Build<TRawData>(IServiceProvider serviceProvider);
    }

    public interface IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        IConfiguration Configuration { get; }
        IServiceCollection ServiceCollection { get; }

        ProjectionRegistrationOptionsBuilder<TRawData> AddEventPublisher(
            Func<IServiceProvider, IEventPublisher<TRawData>> getEventPublisher);

        ProjectionRegistrationOptionsBuilder<TRawData> AddEventDeserializer(
            Func<IServiceProvider, IEventDeserializer<TRawData>> getEventDeserializer);

        ProjectionRegistrationOptions<TRawData> Build(IServiceProvider serviceProvider);

        ProjectionRegistrationOptionsBuilder<TRawData> AddProjectionRegistrations(
            Action<IServiceProvider, ProjectionRegistryBuilder> onRegisteringProjections);
    }
}