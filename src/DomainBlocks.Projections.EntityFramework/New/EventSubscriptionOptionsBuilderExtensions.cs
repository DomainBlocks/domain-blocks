using System;
using DomainBlocks.Projections.New.Builders;
using Microsoft.EntityFrameworkCore;

namespace DomainBlocks.Projections.EntityFramework.New;

public static class EventSubscriptionOptionsBuilderExtensions
{
    public static void AddDbContextProjection<TDbContext>(
        this EventCatchUpSubscriptionOptionsBuilder optionsBuilder,
        Action<DbContextProjectionOptionsBuilder<TDbContext>> optionsAction)
        where TDbContext : DbContext
    {
        var projectionOptionsBuilder = new DbContextProjectionOptionsBuilder<TDbContext>();
        optionsAction(projectionOptionsBuilder);
        optionsBuilder.WithProjectionRegistry(() => projectionOptionsBuilder.Build());
    }
}