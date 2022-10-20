using System;
using System.Collections.Generic;
using System.Linq;

namespace DomainBlocks.Projections.New;

public class ProjectionResourceBuilder<TResource> : IProjectionOptionsProvider
{
    private readonly List<IProjectionOptionsProvider> _projectionOptionsProviders = new();

    public ProjectionResourceBuilder(Func<TResource> resourceFactory)
    {
        ResourceFactory = resourceFactory;
    }

    public Func<TResource> ResourceFactory { get; }

    public void AddProjectionOptionsProvider(IProjectionOptionsProvider builder)
    {
        _projectionOptionsProviders.Add(builder);
    }

    public IEnumerable<IProjectionOptions> GetProjectionOptions()
    {
        return _projectionOptionsProviders.SelectMany(x => x.GetProjectionOptions());
    }
}