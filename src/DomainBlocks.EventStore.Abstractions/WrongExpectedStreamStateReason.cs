namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Specifies the reason an <see cref="ExpectedStreamState"/> check failed.
/// </summary>
public enum WrongExpectedStreamStateReason
{
    /// <summary>
    /// The operation expected the stream to exist, but it does not.
    /// </summary>
    ExpectedStreamToExist,

    /// <summary>
    /// The operation expected the stream to not exist, but it does.
    /// </summary>
    ExpectedStreamToNotExist,

    /// <summary>
    /// The operation expected the stream to be at a specific version, but the actual version does not match.
    /// </summary>
    VersionConflict
}