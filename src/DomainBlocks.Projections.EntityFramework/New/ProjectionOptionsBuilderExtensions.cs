using System;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public static class ProjectionOptionsBuilderExtensions
{
    public static DbContextProjectionOptionsBuilder<TResource, TDbContext> WithDbContext<TResource, TDbContext>(
        this ResourceProjectionOptionsBuilder<TResource> optionsBuilder,
        Func<TResource, TDbContext> dbContextFactory) where TResource : IDisposable where TDbContext : DbContext
    {
        // TODO (DS): Make a proper copy ctor.
        var options = new DbContextProjectionOptions<TResource, TDbContext>();
        options.WithResourceFactory(optionsBuilder.Options.ResourceFactory);
        options.WithDbContextFactory(dbContextFactory);

        return new DbContextProjectionOptionsBuilder<TResource, TDbContext>(options);
    }
}