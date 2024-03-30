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
                return bookmark?.Position;
            })
            .OnCheckpoint(async (s, pos, ct) =>
            {
                var bookmark = await s.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, ct);
                if (bookmark == null)
                {
                    bookmark = new Bookmark();
                    s.Bookmarks.Add(bookmark);
                }

                bookmark.Position = pos;
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
    }
}

public interface IStateTransition<TState, in TEvent>
{
    Task<TState> ApplyAsync(TState state, TEvent @event, CancellationToken cancellationToken);
}

public interface ICheckpointPolicy
{
    bool ShouldCheckpoint(int eventsProcessedSinceLastCheckpoint);
    bool ShouldCheckpoint(TimeSpan timeSinceLastCheckpoint);
    bool ShouldCheckpoint(object @event);
}

public class Checkpoint<TState>
{
    public Checkpoint(TState state, long? position = null)
    {
        State = state;
        Position = position;
    }

    public TState State { get; }
    public long? Position { get; }
}

public interface ICheckpointStore<TState>
{
    Task<Checkpoint<TState>> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(Checkpoint<TState> checkpoint, CancellationToken cancellationToken);
}

public class ShoppingCartProjection :
    IStateTransition<ShoppingCartDbContext, ShoppingSessionStarted>,
    IStateTransition<ShoppingCartDbContext, ItemAddedToShoppingCart>,
    IStateTransition<ShoppingCartDbContext, ItemRemovedFromShoppingCart>
{
    public Task<ShoppingCartDbContext> ApplyAsync(
        ShoppingCartDbContext state, ShoppingSessionStarted @event, CancellationToken cancellationToken)
    {
        return Task.FromResult(state);
    }

    public async Task<ShoppingCartDbContext> ApplyAsync(
        ShoppingCartDbContext state, ItemAddedToShoppingCart @event, CancellationToken cancellationToken)
    {
        var item = await state.ShoppingCartSummaryItems.FindAsync(
            new object[] { @event.SessionId, @event.Item }, cancellationToken);

        if (item != null)
        {
            return state;
        }

        state.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
        {
            SessionId = @event.SessionId,
            Item = @event.Item
        });

        return state;
    }

    public async Task<ShoppingCartDbContext> ApplyAsync(
        ShoppingCartDbContext state, ItemRemovedFromShoppingCart @event, CancellationToken cancellationToken)
    {
        var item = await state.ShoppingCartSummaryItems.FindAsync(
            new object[] { @event.SessionId, @event.Item }, cancellationToken);

        if (item != null)
        {
            state.ShoppingCartSummaryItems.Remove(item);
        }

        return state;
    }
}

public class ShoppingCartCheckpointStore : ICheckpointStore<ShoppingCartDbContext>
{
    private readonly IDbContextFactory<ShoppingCartDbContext> _dbContextFactory;

    public ShoppingCartCheckpointStore(IDbContextFactory<ShoppingCartDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<Checkpoint<ShoppingCartDbContext>> LoadAsync(CancellationToken cancellationToken)
    {
        var state = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var bookmark = await state.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, cancellationToken);
        var position = bookmark?.Position;
        return new Checkpoint<ShoppingCartDbContext>(state, position);
    }

    public async Task SaveAsync(
        Checkpoint<ShoppingCartDbContext> checkpoint, CancellationToken cancellationToken)
    {
        var bookmark = await checkpoint.State.Bookmarks.FindAsync(
            new object[] { Bookmark.DefaultId }, cancellationToken);

        if (bookmark == null)
        {
            bookmark = new Bookmark();
            checkpoint.State.Bookmarks.Add(bookmark);
        }

        bookmark.Position = checkpoint.Position!.Value;
        await checkpoint.State.SaveChangesAsync(cancellationToken);
    }
}