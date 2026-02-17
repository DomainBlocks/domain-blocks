namespace DomainBlocks.EventStore.Abstractions;

public sealed class AppendToStreamOptions
{
    public static readonly AppendToStreamOptions Default = new();

    public ExpectedStreamState ExpectedState { get; init; } = ExpectedStreamState.Any;

    public Guid CommitId { get; init; } = Guid.CreateVersion7();
}