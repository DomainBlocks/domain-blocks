namespace DomainBlocks.Core.Builders;

public interface IIdentityBuilder<out TEntity> : IKeyPrefixBuilder
{
    /// <summary>
    /// Specify a unique ID selector for the entity.
    /// </summary>
    /// <returns>
    /// An object that can be used for further configuration.
    /// </returns>
    IKeyBuilder HasId(Func<TEntity, string> idSelector);
}