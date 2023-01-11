namespace DomainBlocks.Core.Subscriptions;

public sealed class CheckpointFrequency
{
    public static readonly CheckpointFrequency Default = new();
    public int? PerEventCount { get; init; }
    public TimeSpan? PerTimeInterval { get; init; }
    public bool CanCheckpoint => PerEventCount != null || PerTimeInterval != null;
}