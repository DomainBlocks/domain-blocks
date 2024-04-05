using DomainBlocks.Core.Projections.Builders;
using DomainBlocks.Core.Subscriptions.Builders;
using DomainBlocks.EventStore.Subscriptions;
using DomainBlocks.Hosting;
using DomainBlocks.SqlStreamStore.Postgres;
using DomainBlocks.SqlStreamStore.Subscriptions;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
using EventStore.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shopping.Domain.Events;
using Position = EventStore.Client.Position;
using StreamMessage = DomainBlocks.ThirdParty.SqlStreamStore.Streams.StreamMessage;

namespace Shopping.ReadModel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddShoppingReadModel(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ShoppingCartDbContext>(
            options => options.UseNpgsql(configuration.GetConnectionString("ReadModel")),
            optionsLifetime: ServiceLifetime.Singleton);

        services.AddDbContextFactory<ShoppingCartDbContext>();

        services.AddHostedEventStreamSubscription((sp, builder) =>
        {
            var eventStore = configuration.GetValue<string>("EventStore")!;
            var connectionString = configuration.GetConnectionString(eventStore)!;

            switch (eventStore)
            {
                case "EventStoreDb":
                    builder
                        .ConfigureEventStoreDbProjection(sp, connectionString)
                        .ConfigureEventHandlers();
                    break;
                case "SqlStreamStore":
                    builder
                        .ConfigureSqlStreamStoreProjection(sp, connectionString)
                        .ConfigureEventHandlers();
                    break;
            }
        });

        return services;
    }

    private static StateProjectionOptionsBuilder<ResolvedEvent, Position, ShoppingCartDbContext>
        ConfigureEventStoreDbProjection(
            this EventStreamSubscriptionBuilder builder,
            IServiceProvider serviceProvider,
            string connectionString)
    {
        var settings = EventStoreClientSettings.Create(connectionString);

        return builder
            .UseEventStore(o => o.WithSettings(settings))
            .FromAllEventsStream()
            .ConfigureProjection(serviceProvider)
            .OnStarting(async (s, ct) =>
            {
                await s.Database.EnsureCreatedAsync(ct);
                var bookmark = await s.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, ct);
                return bookmark == null ? null : new Position(bookmark.Position, bookmark.Position);
            })
            .OnCheckpoint(async (s, pos, ct) =>
            {
                var bookmark = await s.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, ct);
                if (bookmark == null)
                {
                    bookmark = new Bookmark();
                    s.Bookmarks.Add(bookmark);
                }

                bookmark.Position = pos.CommitPosition;
                await s.SaveChangesAsync(ct);
            });
    }

    private static StateProjectionOptionsBuilder<StreamMessage, long, ShoppingCartDbContext>
        ConfigureSqlStreamStoreProjection(
            this EventStreamSubscriptionBuilder builder,
            IServiceProvider serviceProvider,
            string connectionString)
    {
        var settings = new PostgresStreamStoreSettings(connectionString);

        return builder
            .UseSqlStreamStore(o => o.UsePostgresStreamStore(settings))
            .FromAllEventsStream()
            .ConfigureProjection(serviceProvider)
            .OnStarting(async (s, ct) =>
            {
                await s.Database.EnsureCreatedAsync(ct);
                var bookmark = await s.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, ct);
                return bookmark == null ? null : Convert.ToInt64(bookmark.Position);
            })
            .OnCheckpoint(async (s, pos, ct) =>
            {
                var bookmark = await s.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, ct);
                if (bookmark == null)
                {
                    bookmark = new Bookmark();
                    s.Bookmarks.Add(bookmark);
                }

                bookmark.Position = Convert.ToUInt64(pos);
                await s.SaveChangesAsync(ct);
            });
    }

    private static StateProjectionOptionsBuilder<TEvent, TPosition, ShoppingCartDbContext>
        ConfigureProjection<TEvent, TPosition>(
            this EventStreamConsumersBuilder<TEvent, TPosition> builder,
            IServiceProvider serviceProvider)
        where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    {
        return builder
            .ProjectTo()
            .State(_ => serviceProvider
                .GetRequiredService<IDbContextFactory<ShoppingCartDbContext>>()
                .CreateDbContext())
            .WithCatchUpCheckpoints(x => x.PerEventCount(100))
            .WithLiveCheckpoints(x => x.PerEventCount(10).Or().PerTimeInterval(TimeSpan.FromSeconds(1)));
    }

    private static void ConfigureEventHandlers<TRawEvent, TPosition>(
        this StateProjectionOptionsBuilder<TRawEvent, TPosition, ShoppingCartDbContext> builder)
        where TPosition : struct, IEquatable<TPosition>, IComparable<TPosition>
    {
        builder
            .When<ShoppingSessionStarted>(async (e, s, ct) =>
            {
                var cart = await s.ShoppingCarts.FindAsync(new object?[] { e.SessionId }, cancellationToken: ct);
                if (cart == null)
                {
                    cart = new ShoppingCart { SessionId = e.SessionId };
                    s.ShoppingCarts.Add(cart);
                }
            })
            .When<ItemAddedToShoppingCart>(async (e, s, ct) =>
            {
                var item = await s.ShoppingCartItems.FindAsync(new object[] { e.SessionId, e.Item }, ct);
                if (item == null)
                {
                    s.ShoppingCartItems.Add(new ShoppingCartItem { SessionId = e.SessionId, Name = e.Item });
                }
            })
            .When<ItemRemovedFromShoppingCart>(async (e, s, ct) =>
            {
                var itemToRemove = await s.ShoppingCartItems.FindAsync(new object[] { e.SessionId, e.Item }, ct);
                if (itemToRemove != null)
                {
                    s.ShoppingCartItems.Remove(itemToRemove);
                }
            });
    }
}