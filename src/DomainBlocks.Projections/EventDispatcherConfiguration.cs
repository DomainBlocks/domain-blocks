using System;

namespace DomainBlocks.Projections;

public record EventDispatcherConfiguration(
    TimeSpan ProjectionHandlerTimeout,
    bool ContinueAfterTimeout,
    bool ContinueAfterProjectionException,
    bool ContinueAfterSerializationException)
{
    public static readonly EventDispatcherConfiguration ReadModelDefaults =
        new(TimeSpan.FromSeconds(5), false, false, true);

    public static readonly EventDispatcherConfiguration ProcessDefaults =
        new(TimeSpan.FromSeconds(5), true, true, true);
}