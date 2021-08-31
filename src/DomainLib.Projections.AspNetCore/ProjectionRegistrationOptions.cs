using System;
using DomainLib.Serialization;

namespace DomainLib.Projections.AspNetCore
{
    public class ProjectionRegistrationOptions<TRawData>
    {
        public ProjectionRegistrationOptions(IEventPublisher<TRawData> eventPublisher, IEventDeserializer<TRawData> eventSerializer, Action<ProjectionRegistryBuilder> onRegisteringProjections)
        {
            EventPublisher = eventPublisher;
            EventSerializer = eventSerializer;
            OnRegisteringProjections = onRegisteringProjections;
        }

        public IEventPublisher<TRawData> EventPublisher { get; }
        public IEventDeserializer<TRawData> EventSerializer { get; }
        public Action<ProjectionRegistryBuilder> OnRegisteringProjections { get; }
    }
}