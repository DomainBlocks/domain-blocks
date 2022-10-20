using System;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public static class ProjectionResourceBuilderExtensions
{
    public static DbContextProjectionOptionsBuilder<TResource, TDbContext> WithDbContext<TResource, TDbContext>(
        this ProjectionResourceBuilder<TResource> optionsBuilder,
        Func<TResource, TDbContext> dbContextFactory) where TResource : IDisposable where TDbContext : DbContext
    {
        var projectionOptions = new DbContextProjectionOptions<TResource, TDbContext>()
            .WithResourceFactory(optionsBuilder.ResourceFactory)
            .WithDbContextFactory(dbContextFactory);

        var builder = new DbContextProjectionOptionsBuilder<TResource, TDbContext>(projectionOptions);
        optionsBuilder.AddProjectionOptionsProvider(builder);

        return builder;
    }
}