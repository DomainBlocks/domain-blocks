using DomainBlocks.Projections;
using DomainBlocks.Projections.AspNetCore;
using DomainBlocks.Projections.SqlStreamStore;
using DomainBlocks.SqlStreamStore;
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
using SqlStreamStore;
using SqlStreamStore.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Shopping.ReadModel;

public class Startup
{
    static Startup()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddConsole());
        DomainBlocks.Core.Logger.SetLoggerFactory(loggerFactory);
        LogProvider.SetCurrentLogProvider(new SqlStreamStoreLogProvider(loggerFactory));
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

        services.AddHostedEventCatchUpSubscription((sp, options, model) =>
        {
            options.UseSqlStreamStore(o =>
            {
                var connectionString = Configuration.GetValue<string>("SqlStreamStore:ConnectionString");
                var settings = new PostgresStreamStoreSettings(connectionString);
                o.UsePostgresStreamStore(settings);
            });

            model.Projection<ShoppingCartDbContext>(projection =>
            {
                projection.WithStateFactory(
                    _ => sp.GetRequiredService<IDbContextFactory<ShoppingCartDbContext>>().CreateDbContext());

                projection
                    .OnInitializing(async (state, ct) =>
                    {
                        // We could run migrations here
                        await state.Database.EnsureDeletedAsync(ct);
                        await state.Database.EnsureCreatedAsync(ct);
                    })
                    .OnSubscribing(async (state, ct) =>
                    {
                        var bookmark = await state.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, ct);

                        var position = bookmark == null
                            ? StreamPosition.Empty
                            : StreamPosition.FromJsonString(bookmark.Position);

                        return position;
                    })
                    .OnSave(async (state, position, ct) =>
                    {
                        // TODO (DS): Consider a checkpoint hook, where the frequency can be specified,
                        // e.g. bookmark every 100 events.
                        var bookmark = await state.Bookmarks.FindAsync(new object[] { Bookmark.DefaultId }, ct);
                        if (bookmark == null)
                        {
                            bookmark = new Bookmark();
                            state.Bookmarks.Add(bookmark);
                        }

                        bookmark.Position = position.ToJsonString();

                        await state.SaveChangesAsync(ct);
                    })
                    .When<ItemAddedToShoppingCart>((e, state) =>
                    {
                        // Context can expose metadata and cancellation token
                        state.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
                        {
                            CartId = e.CartId,
                            Id = e.Id,
                            ItemDescription = e.Item
                        });
                    })
                    .When<ItemRemovedFromShoppingCart>(async (e, state, ct) =>
                    {
                        var item = await state.ShoppingCartSummaryItems.FindAsync(new object[] { e.Id }, ct);
                        if (item != null)
                        {
                            state.ShoppingCartSummaryItems.Remove(item);
                        }
                    });
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