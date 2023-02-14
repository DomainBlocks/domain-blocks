namespace DomainBlocks.Core.Builders;

public interface IKeyPrefixBuilder
{
    /// <summary>
    /// Specify a prefix to use for both stream and snapshot keys.
    /// </summary>
    void WithKeyPrefix(string prefix);
}