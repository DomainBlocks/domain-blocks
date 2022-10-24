using System.Threading.Tasks;
using DomainBlocks.Projections.AspNetCore;
using DomainBlocks.Projections.EntityFramework.AspNetCore;
using DomainBlocks.Projections.EntityFramework.New;
using DomainBlocks.Projections.SqlStreamStore;
using DomainBlocks.Projections.SqlStreamStore.AspNetCore;
using DomainBlocks.Projections.SqlStreamStore.New;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Shopping.Domain.Events;
using Shopping.ReadModel.Db;
using Shopping.ReadModel.Db.Model;
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
        services.AddDbContext<ShoppingCartDbContext>(options =>
            options.UseNpgsql(Configuration.GetConnectionString("Default")));

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
            var connectionString = Configuration.GetValue<string>("SqlStreamStore:ConnectionString");
            subscriptionOptions.UseSqlStreamStore(connectionString);

            subscriptionOptions
                .Using(sp.CreateScope)
                .WithDbContext(x => x.ServiceProvider.GetRequiredService<ShoppingCartDbContext>())
                .AddProjection(projection =>
                {
                    projection.WithCatchUpMode(DbContextProjectionCatchUpMode.UseTransaction);

                    projection.OnInitializing(async (dbContext, ct) =>
                    {
                        await dbContext.Database.EnsureDeletedAsync(ct);
                        await dbContext.Database.EnsureCreatedAsync(ct);
                    });
                    
                    projection.When<ItemAddedToShoppingCart>((e, dbContext) =>
                    {
                        dbContext.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
                        {
                            CartId = e.CartId,
                            Id = e.Id,
                            ItemDescription = e.Item
                        });

                        return Task.CompletedTask;
                    });

                    projection.When<ItemRemovedFromShoppingCart>(async (e, dbContext) =>
                    {
                        var item = await dbContext.ShoppingCartSummaryItems.FindAsync(e.Id);
                        if (item != null)
                        {
                            dbContext.ShoppingCartSummaryItems.Remove(item);
                        }
                    });
                });

            // subscriptionOptions.AddProjection(projection =>
            // {
            //     projection.OnInitializing(async ct =>
            //     {
            //         using var scope = sp.CreateScope();
            //         await using var dbContext = scope.ServiceProvider.GetRequiredService<ShoppingCartDbContext>();
            //         await dbContext.Database.EnsureDeletedAsync(ct);
            //         await dbContext.Database.EnsureCreatedAsync(ct);
            //     });
            //
            //     // State
            //     var hasCaughtUp = false;
            //     IServiceScope scope = null;
            //     ShoppingCartDbContext dbContext = null;
            //     IDbContextTransaction transaction = null;
            //
            //     projection.OnCatchingUp(_ =>
            //     {
            //         scope = sp.CreateScope();
            //         dbContext = scope.ServiceProvider.GetRequiredService<ShoppingCartDbContext>();
            //         transaction = dbContext.Database.BeginTransaction();
            //         hasCaughtUp = false;
            //         return Task.CompletedTask;
            //     });
            //
            //     projection.OnCaughtUp(async ct =>
            //     {
            //         await dbContext.SaveChangesAsync(ct);
            //         await transaction.CommitAsync(ct);
            //         scope.Dispose();
            //         hasCaughtUp = true;
            //     });
            //
            //     projection.OnEventDispatching(_ =>
            //     {
            //         if (!hasCaughtUp) return Task.CompletedTask;
            //         scope = sp.CreateScope();
            //         dbContext = scope.ServiceProvider.GetRequiredService<ShoppingCartDbContext>();
            //         return Task.CompletedTask;
            //     });
            //
            //     projection.OnEventHandled(async ct =>
            //     {
            //         if (!hasCaughtUp) return;
            //         await dbContext.SaveChangesAsync(ct);
            //         scope.Dispose();
            //     });
            //
            //     projection.When<ItemAddedToShoppingCart>(e =>
            //     {
            //         dbContext.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
            //         {
            //             CartId = e.CartId,
            //             Id = e.Id,
            //             ItemDescription = e.Item
            //         });
            //
            //         return Task.CompletedTask;
            //     });
            //
            //     projection.When<ItemRemovedFromShoppingCart>(async e =>
            //     {
            //         var item = await dbContext.ShoppingCartSummaryItems.FindAsync(e.Id);
            //         if (item != null)
            //         {
            //             dbContext.ShoppingCartSummaryItems.Remove(item);
            //         }
            //     });
            // });
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

public class SqlStreamStoreLogProvider : LogProviderBase
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