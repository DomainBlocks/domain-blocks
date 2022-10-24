using System;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public static class UsingResourceOptionsBuilderExtensions
{
    public static WithDbContextOptionsBuilder<TResource, TDbContext> WithDbContext<TResource, TDbContext>(
        this UsingResourceOptionsBuilder<TResource> optionsBuilder,
        Func<TResource, TDbContext> dbContextFactory) where TResource : IDisposable where TDbContext : DbContext
    {
        var initialOptions = new DbContextProjectionOptions<TResource, TDbContext>()
            .WithResourceFactory(optionsBuilder.ResourceFactory)
            .WithDbContextFactory(dbContextFactory);

        return new WithDbContextOptionsBuilder<TResource, TDbContext>(
            optionsBuilder.CoreBuilder, initialOptions);
    }
}