using System;
using DomainBlocks.Projections.New;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public static class ProjectionOptionsBuilderExtensions
{
    public static DbContextProjectionOptionsBuilder<TResource, TDbContext> WithDbContext<TResource, TDbContext>(
        this ProjectionOptionsBuilder<TResource> optionsBuilder,
        Func<TResource, TDbContext> dbContextFactory) where TResource : IDisposable where TDbContext : DbContext
    {
        return new DbContextProjectionOptionsBuilder<TResource, TDbContext>(optionsBuilder.Options, dbContextFactory);
    }
}