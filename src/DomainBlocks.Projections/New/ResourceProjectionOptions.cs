using System;

namespace DomainBlocks.Projections.New;

public class ResourceProjectionOptions<TResource> : ProjectionOptionsBase where TResource : IDisposable
{
    public Func<TResource> ResourceFactory { get; private set; }

    public void WithResourceFactory(Func<TResource> resourceFactory)
    {
        ResourceFactory = resourceFactory;
    }

    public override ProjectionRegistry ToProjectionRegistry()
    {
        throw new NotImplementedException();
    }
}