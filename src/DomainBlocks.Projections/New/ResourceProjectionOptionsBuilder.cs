using System;

namespace DomainBlocks.Projections.New;

public class ResourceProjectionOptionsBuilder<TResource> where TResource : IDisposable
{
    public ResourceProjectionOptionsBuilder(ResourceProjectionOptions<TResource> options)
    {
        Options = options;
    }

    public ResourceProjectionOptions<TResource> Options { get; }
}