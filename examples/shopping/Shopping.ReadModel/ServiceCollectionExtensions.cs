using DomainBlocks.Core.Projections.Builders;
using DomainBlocks.EventStore.Subscriptions;
using DomainBlocks.Hosting;
using EventStore.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shopping.Domain.Events;

namespace Shopping.ReadModel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShoppingReadModel(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ShoppingCartDbContext>(
            options => options.UseNpgsql(configuration.GetConnectionString("Default")),
            optionsLifetime: ServiceLifetime.Singleton);

        services.AddDbContextFactory<ShoppingCartDbContext>();

        services.AddHostedEventStreamSubscription((sp, options) =>
        {
            var connectionString = configuration.GetValue<string>("EventStore:ConnectionString")!;

            options
                .UseEventStore(o => o.WithSettings(EventStoreClientSettings.Create(connectionString)))
                .FromAllEventsStream()
                .ProjectTo()
                .State(_ => sp.GetRequiredService<IDbContextFactory<ShoppingCartDbContext>>().CreateDbContext())
                .WithCatchUpCheckpoints(x => x.PerEventCount(100))
                .WithLiveCheckpoints(x => x.PerEventCount(10).Or().PerTimeInterval(TimeSpan.FromSeconds(1)))
                .OnStarting(async (s, ct) =>
                {
                    await s.Database.EnsureCreatedAsync(ct);
                    var bookmark = await s.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, ct);
                    return bookmark == null ? null : new Position(bookmark.CommitPosition, bookmark.PreparePosition);
                })
                .OnCheckpoint(async (s, pos, ct) =>
                {
                    var bookmark = await s.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, ct);
                    if (bookmark == null)
                    {
                        bookmark = new Bookmark();
                        s.Bookmarks.Add(bookmark);
                    }

                    bookmark.CommitPosition = pos.CommitPosition;
                    bookmark.PreparePosition = pos.PreparePosition;
                    await s.SaveChangesAsync(ct);
                })
                .When<ItemAddedToShoppingCart>(async (e, s, ct) =>
                {
                    var item = await s.ShoppingCartSummaryItems.FindAsync(new object[] { e.SessionId, e.Item }, ct);
                    if (item != null)
                    {
                        return;
                    }

                    s.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
                    {
                        SessionId = e.SessionId,
                        Item = e.Item
                    });
                })
                .When<ItemRemovedFromShoppingCart>(async (e, s, ct) =>
                {
                    var item = await s.ShoppingCartSummaryItems.FindAsync(new object[] { e.SessionId, e.Item }, ct);
                    if (item != null)
                    {
                        s.ShoppingCartSummaryItems.Remove(item);
                    }
                });
        });

        return services;
    }
}