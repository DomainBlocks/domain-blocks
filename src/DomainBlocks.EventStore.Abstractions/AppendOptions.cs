namespace DomainBlocks.EventStore.Abstractions;

/// <summary>
/// Configures an event append operation.
/// </summary>
public sealed class AppendOptions
{
    /// <summary>
    /// The default append options.
    /// </summary>
    public static readonly AppendOptions Default = new();

    /// <summary>
    /// The maximum time to wait for the append operation to be acknowledged before timing out. The default is 30
    /// seconds.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}