using System;
using DomainLib.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DomainLib.Projections.AspNetCore
{
    public interface IProjectionRegistrationOptionsBuilderInfrastructure<TRawData>
    {
        IConfiguration Configuration { get; }
        IServiceProvider ServiceProvider { get; }
        IServiceCollection ServiceCollection { get; }

        ProjectionRegistrationOptionsBuilder<TRawData> AddEventPublisher(
            Func<IEventPublisher<TRawData>> getEventPublisher);

        ProjectionRegistrationOptionsBuilder<TRawData> AddEventDeserializer(
            Func<IEventDeserializer<TRawData>> getEventDeserializer);

        ProjectionRegistrationOptions<TRawData> Build();

        ProjectionRegistrationOptionsBuilder<TRawData> AddProjectionRegistrations(
            Action<ProjectionRegistryBuilder> onRegisteringProjections);
    }
}