namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Specifies how an append operation ended, as the writer saw it.
/// </summary>
internal enum AppendOutcome
{
    /// <summary>
    /// An attempt returned, so the operation's events must be in the event log.
    /// </summary>
    Ok,

    /// <summary>
    /// Every attempt failed, so the writer can't know whether the events were written. They may be in the event log,
    /// but only once.
    /// </summary>
    Indeterminate
}