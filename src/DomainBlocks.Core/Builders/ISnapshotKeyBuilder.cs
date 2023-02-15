namespace DomainBlocks.Core.Builders;

public interface ISnapshotKeyBuilder
{
    /// <summary>
    /// Specify a snapshot key selector.
    /// </summary>
    void WithSnapshotKey(Func<string, string> idToSnapshotKeySelector);
}