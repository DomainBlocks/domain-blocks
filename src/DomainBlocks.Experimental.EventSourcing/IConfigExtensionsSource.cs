namespace DomainBlocks.Experimental.EventSourcing;

public interface IConfigExtensionsSource
{
    ICollection<IConfigExtension> ConfigExtensions { get; }
}