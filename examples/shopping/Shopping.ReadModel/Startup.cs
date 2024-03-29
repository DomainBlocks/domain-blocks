using System;
using DomainBlocks.Core.Projections.Builders;
using DomainBlocks.EventStore.Subscriptions;
using DomainBlocks.Hosting;
using DomainBlocks.Logging;
using EventStore.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Shopping.Domain.Events;
using Shopping.ReadModel.Db;
using Shopping.ReadModel.Db.Model;

namespace Shopping.ReadModel;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    private IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<ShoppingCartDbContext>(
            options => options.UseNpgsql(Configuration.GetConnectionString("Default")),
            optionsLifetime: ServiceLifetime.Singleton);

        services.AddDbContextFactory<ShoppingCartDbContext>();

        services.AddHostedEventStreamSubscription((sp, options) =>
        {
            options
                .UseEventStore(o => o.WithSettings(
                    EventStoreClientSettings.Create("esdb://shopping.eventstore:2113?tls=false")))
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

        services.AddControllers();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Shopping.ReadModel", Version = "v1" });
        });
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Shopping.ReadModel v1"));
        }

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseAuthorization();
        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        
        // Configure DomainBlocks logging.
        var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
        LogProvider.SetLoggerFactory(loggerFactory);
    }
}