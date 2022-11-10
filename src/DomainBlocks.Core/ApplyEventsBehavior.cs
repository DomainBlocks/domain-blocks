namespace DomainBlocks.Core;

public enum ApplyEventsBehavior
{
    /// <summary>
    /// Do not apply the events to the aggregate. Specify this behavior when the aggregate's state is updated by
    /// the command method.
    /// </summary>
    DoNotApply,

    /// <summary>
    /// Materialize the event enumerable prior to applying each event to the aggregate. Specify this behavior to
    /// avoid state changes if events are yield returned from the command method.
    /// </summary>
    MaterializeFirst,

    /// <summary>
    /// Apply the returned events to the aggregate while enumerating them. Specify this behavior to update the state of
    /// the aggregate as events are yield returned from the command method.
    /// </summary>
    ApplyWhileEnumerating
}