using System;

namespace DomainBlocks.Projections;

public record EventDispatcherConfiguration
{
    public EventDispatcherConfiguration(TimeSpan projectionHandlerTimeout,
        bool continueAfterTimeout,
        bool continueAfterProjectionException,
        bool continueAfterSerializationException)
    {
        ProjectionHandlerTimeout = projectionHandlerTimeout;
        ContinueAfterTimeout = continueAfterTimeout;
        ContinueAfterProjectionException = continueAfterProjectionException;
        ContinueAfterSerializationException = continueAfterSerializationException;
    }

    public static EventDispatcherConfiguration ReadModelDefaults =
        new(TimeSpan.FromSeconds(5), false, false, true);

    public static EventDispatcherConfiguration ProcessDefaults =
        new(TimeSpan.FromSeconds(5), true, true, true);

    public TimeSpan ProjectionHandlerTimeout { get; init; }
    public bool ContinueAfterTimeout { get; init; }
    public bool ContinueAfterProjectionException { get; init; }
    public bool ContinueAfterSerializationException { get; init; }
}