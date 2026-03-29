namespace DomainBlocks.EventStore.Abstractions;

public sealed class AppendToStreamOptions
{
    public static readonly AppendToStreamOptions Default = new();

    public ExpectedStreamState ExpectedState { get; init; } = ExpectedStreamState.Any;

    public Guid CommitId { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// Gets or sets how long the client waits for the operation to be acknowledged before timing out.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);
}