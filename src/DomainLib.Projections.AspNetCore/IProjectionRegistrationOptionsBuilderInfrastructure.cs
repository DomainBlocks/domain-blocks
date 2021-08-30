using System;
using DomainLib.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Projections.AspNetCore
{
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