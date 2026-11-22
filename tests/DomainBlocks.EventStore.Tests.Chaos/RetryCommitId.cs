namespace DomainBlocks.EventStore.Tests.Chaos;

/// <summary>
/// Specifies which commit ID a writer uses when it retries an append.
/// </summary>
internal enum RetryCommitId
{
    /// <summary>
    /// A retry uses its operation's commit ID, which makes it idempotent.
    /// </summary>
    Same,

    /// <summary>
    /// A retry uses a new commit ID, as a writer with a bug would. A run with this sabotage must report duplicates,
    /// or else its faults left no append in doubt.
    /// </summary>
    New
}