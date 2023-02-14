namespace DomainBlocks.Core.Builders;

public interface IKeyBuilder : IKeyPrefixBuilder, ISnapshotKeyBuilder
{
    /// <summary>
    /// Specify a stream key selector.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    ISnapshotKeyBuilder WithStreamKey(Func<string, string> idToStreamKeySelector);
}