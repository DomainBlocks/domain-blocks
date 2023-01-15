using System;
using DomainBlocks.Core.Projections.Experimental.Builders;
using DomainBlocks.Hosting;
using DomainBlocks.Logging;
using DomainBlocks.SqlStreamStore.Postgres;
using DomainBlocks.SqlStreamStore.Subscriptions;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
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
    static Startup()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddConsole());
        Log.SetLoggerFactory(loggerFactory);
    }

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
                .UseSqlStreamStore(o =>
                {
                    var connectionString = Configuration.GetValue<string>("SqlStreamStore:ConnectionString");
                    var settings = new PostgresStreamStoreSettings(connectionString);
                    o.UsePostgresStreamStore(settings);
                })
                .FromAllEventsStream()
                .ProjectTo()
                .State(_ => sp.GetRequiredService<IDbContextFactory<ShoppingCartDbContext>>().CreateDbContext())
                .WithCatchUpCheckpoints(x => x.PerEventCount(100))
                .WithLiveCheckpoints(x => x.PerEventCount(10).Or().PerTimeInterval(TimeSpan.FromSeconds(1)))
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
                })
                .When<ItemAddedToShoppingCart>(async (e, s, ct) =>
                {
                    var item = await s.ShoppingCartSummaryItems.FindAsync(new object[] { e.Id }, ct);
                    if (item != null)
                    {
                        return;
                    }

                    s.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
                    {
                        CartId = e.CartId,
                        Id = e.Id,
                        ItemDescription = e.Item
                    });
                })
                .When<ItemRemovedFromShoppingCart>(async (e, s, ct) =>
                {
                    var item = await s.ShoppingCartSummaryItems.FindAsync(new object[] { e.Id }, ct);
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
    }
}