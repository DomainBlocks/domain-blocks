using System.Threading.Tasks;
using DomainBlocks.Projections.AspNetCore;
using DomainBlocks.Projections.EntityFramework.AspNetCore;
using DomainBlocks.Projections.New;
using DomainBlocks.Projections.SqlStreamStore;
using DomainBlocks.Projections.SqlStreamStore.AspNetCore;
using DomainBlocks.Projections.SqlStreamStore.New;
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
using SqlStreamStore.Logging.LogProviders;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Shopping.ReadModel;

public class Startup
{
    static Startup()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddConsole());
        DomainBlocks.Common.Logger.SetLoggerFactory(loggerFactory);
        LogProvider.SetCurrentLogProvider(new SqlStreamStoreLogProvider(loggerFactory));
    }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        // services.AddDbContext<ShoppingCartDbContext>(options =>
        //     options.UseNpgsql(Configuration.GetConnectionString("Default")));

        services.AddDbContextFactory<ShoppingCartDbContext>(
            options => options.UseNpgsql(Configuration.GetConnectionString("Default")));

        // TODO (DS): Remove this example once we can support multiple projections with the new approach.
        // services.AddReadModel(Configuration,
        //     options =>
        //     {
        //         options.UseSqlStreamStorePublishedEvents()
        //             .UseEntityFramework<ShoppingCartDbContext, StreamMessageWrapper>()
        //             .WithProjections((builder, dbContext) =>
        //             {
        //                 ShoppingClassSummaryEfProjection.Register(builder, dbContext);
        //                 ShoppingCartHistoryEfProjection.Register(builder, dbContext);
        //             });
        //     });

        // **** New approach ****
        services.AddHostedEventCatchUpSubscription((sp, subscriptionOptions) =>
        {
            subscriptionOptions
                .UseSqlStreamStore(o =>
                {
                    var connectionString = Configuration.GetValue<string>("SqlStreamStore:ConnectionString");
                    var settings = new PostgresStreamStoreSettings(connectionString);
                    o.UsePostgres(settings).UseJsonSerialization();
                });

            subscriptionOptions
                .Using(() =>
                {
                    var dbContextFactory = sp.GetRequiredService<IDbContextFactory<ShoppingCartDbContext>>();
                    return dbContextFactory.CreateDbContext();
                })
                .OnInitializing(async (resource, ct) =>
                {
                    // This could run migrations
                    await resource.Database.EnsureDeletedAsync(ct);
                    await resource.Database.EnsureCreatedAsync(ct);
                })
                .OnSubscribing(async (resource, ct) =>
                {
                    var bookmark = await resource.Bookmark.FirstOrDefaultAsync(ct);

                    var position = bookmark == null
                        ? StreamPosition.Empty
                        : StreamPosition.FromJsonString(bookmark.Position);

                    return position;
                })
                .OnSave(async (resource, position, ct) =>
                {
                    resource.Bookmark.RemoveRange(resource.Bookmark);
                    resource.Bookmark.Add(new Bookmark
                    {
                        Position = position.ToJsonString()
                    });

                    await resource.SaveChangesAsync(ct);
                })
                .When<ItemAddedToShoppingCart>((e, context) =>
                {
                    // Context can expose metadata and cancellation token
                    context.Resource.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
                    {
                        CartId = e.CartId,
                        Id = e.Id,
                        ItemDescription = e.Item
                    });
                })
                .When<ItemRemovedFromShoppingCart>(async (e, context) =>
                {
                    var item = await context.Resource.ShoppingCartSummaryItems.FindAsync(e.Id);
                    if (item != null)
                    {
                        context.Resource.ShoppingCartSummaryItems.Remove(item);
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

    // TODO (DS): Move this somewhere common in a future PR.
    private class SqlStreamStoreLogProvider : LogProviderBase
    {
        private readonly ILoggerFactory _loggerFactory;

        public SqlStreamStoreLogProvider(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public override Logger GetLogger(string name)
        {
            var logger = _loggerFactory.CreateLogger(name);

            return (level, func, exception, parameters) =>
            {
                var message = func?.Invoke();

                var logLevel = level switch
                {
                    SqlStreamStore.Logging.LogLevel.Trace => LogLevel.Trace,
                    SqlStreamStore.Logging.LogLevel.Debug => LogLevel.Debug,
                    SqlStreamStore.Logging.LogLevel.Info => LogLevel.Information,
                    SqlStreamStore.Logging.LogLevel.Warn => LogLevel.Warning,
                    SqlStreamStore.Logging.LogLevel.Error => LogLevel.Error,
                    SqlStreamStore.Logging.LogLevel.Fatal => LogLevel.Critical,
                    _ => LogLevel.Information
                };

                logger.Log(logLevel, exception, message, parameters);

                return true;
            };
        }
    }
}