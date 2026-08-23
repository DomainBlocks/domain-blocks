namespace DomainBlocks.EventStore.Abstractions;

public sealed class AppendToStreamOptions
{
    public static readonly AppendToStreamOptions Default = new();

    /// <summary>
    /// Gets or sets how long the client waits for the operation to be acknowledged before timing out.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}