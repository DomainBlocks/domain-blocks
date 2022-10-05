using System;
using DomainBlocks.Serialization;

namespace DomainBlocks.Projections.AspNetCore;

public class ProjectionRegistrationOptions<TRawData>
{
    public ProjectionRegistrationOptions(IEventPublisher<TRawData> eventPublisher,
        IEventDeserializer<TRawData> eventSerializer, Action<ProjectionRegistryBuilder> onRegisteringProjections)
    {
        EventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        EventSerializer = eventSerializer ?? throw new ArgumentNullException(nameof(eventSerializer));
        OnRegisteringProjections = onRegisteringProjections ??
                                   throw new ArgumentNullException(nameof(onRegisteringProjections));
    }

    public IEventPublisher<TRawData> EventPublisher { get; }
    public IEventDeserializer<TRawData> EventSerializer { get; }
    public Action<ProjectionRegistryBuilder> OnRegisteringProjections { get; }
}