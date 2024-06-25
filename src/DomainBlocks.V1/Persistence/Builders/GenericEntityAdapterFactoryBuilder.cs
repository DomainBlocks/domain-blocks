using DomainBlocks.V1.Persistence.Entities;

namespace DomainBlocks.V1.Persistence.Builders;

public class GenericEntityAdapterFactoryBuilder
{
    private readonly GenericEntityAdapterTypeResolver _typeResolver;
    private object?[]? _constructorArgs;

    public GenericEntityAdapterFactoryBuilder(Type genericTypeDefinition)
    {
        _typeResolver = new GenericEntityAdapterTypeResolver(genericTypeDefinition);
    }

    public GenericEntityAdapterFactoryBuilder WithConstructorArgs(params object?[]? args)
    {
        _constructorArgs = args;
        return this;
    }

    public GenericEntityAdapterFactory Build()
    {
        var factory = new GenericEntityAdapterFactory(_typeResolver, _constructorArgs);
        return factory;
    }
}