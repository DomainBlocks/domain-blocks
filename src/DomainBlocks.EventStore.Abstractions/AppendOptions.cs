namespace DomainBlocks.EventStore.Abstractions;

public sealed class AppendOptions
{
    public static readonly AppendOptions Default = new();

    /// <summary>
    /// Gets or sets how long the client waits for the operation to be acknowledged before timing out.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}