namespace DomainBlocks.EventStore;

/// <summary>
/// Provides options for an append.
/// </summary>
public sealed class AppendOptions
{
    /// <summary>
    /// The default options.
    /// </summary>
    public static readonly AppendOptions Default = new();

    /// <summary>
    /// Gets how long to wait for the store to acknowledge the append before timing out. The default is 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}