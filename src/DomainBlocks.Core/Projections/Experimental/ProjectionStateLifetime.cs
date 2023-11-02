namespace DomainBlocks.Core.Projections.Experimental;

/// <summary>
/// Specifies the lifetime of state for a <see cref="StateProjection{TRawEvent,TPosition,TState}"/>.
/// </summary>
public enum ProjectionStateLifetime
{
    /// <summary>
    /// Specifies that the lifetime of the state is tied to each checkpoint interval.
    /// </summary>
    PerCheckpoint,

    /// <summary>
    /// Specifies that a single instance of the state will be created for the lifetime of the projection.
    /// </summary>
    Singleton
}