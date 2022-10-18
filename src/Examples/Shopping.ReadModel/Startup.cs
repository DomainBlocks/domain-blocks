using System.Threading.Tasks;
using DomainBlocks.Projections.AspNetCore;
using DomainBlocks.Projections.EntityFramework.AspNetCore;
using DomainBlocks.Projections.SqlStreamStore;
using DomainBlocks.Projections.SqlStreamStore.AspNetCore;
using DomainBlocks.Projections.SqlStreamStore.New.Extensions;
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
using SqlStreamStore.Logging;
using SqlStreamStore.Logging.LogProviders;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Shopping.ReadModel;

public class Startup
{
    static Startup()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddConsole());
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
        services.AddHostedEventSubscription((sp, options) =>
        {
            var connectionString = Configuration.GetValue<string>("SqlStreamStore:ConnectionString");
            options.UseSqlStreamStore(connectionString);

            options.AddProjection<DbSet<ShoppingCartSummaryItem>>(projection =>
            {
                projection.OnInitializing(async ct =>
                {
                    using var scope = sp.CreateScope();
                    await using var dbContext = scope.ServiceProvider.GetRequiredService<ShoppingCartDbContext>();
                    await dbContext.Database.EnsureDeletedAsync(ct);
                    await dbContext.Database.EnsureCreatedAsync(ct);
                });

                projection
                    .OnCatchingUp(_ =>
                    {
                        var scope = sp.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<ShoppingCartDbContext>();
                        var transaction = dbContext.Database.BeginTransaction();
                        return Task.FromResult((scope, dbContext, transaction));
                    })
                    .OnCaughtUp(async (context, ct) =>
                    {
                        await context.dbContext.SaveChangesAsync(ct);
                        await context.transaction.CommitAsync(ct);
                        context.scope.Dispose();
                    })
                    .WithState(x => x.dbContext.ShoppingCartSummaryItems);

                projection
                    .OnEventDispatching(_ =>
                    {
                        var scope = sp.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<ShoppingCartDbContext>();
                        return Task.FromResult((scope, dbContext));
                    })
                    .OnEventHandled(async (context, ct) =>
                    {
                        await context.dbContext.SaveChangesAsync(ct);
                        context.scope.Dispose();
                    })
                    .WithState(x => x.dbContext.ShoppingCartSummaryItems);

                projection.When<ItemAddedToShoppingCart>((e, state) =>
                {
                    state.Add(new ShoppingCartSummaryItem
                    {
                        CartId = e.CartId,
                        Id = e.Id,
                        ItemDescription = e.Item
                    });
                });

                projection.WhenAsync<ItemRemovedFromShoppingCart>(async (e, state) =>
                {
                    var item = await state.FindAsync(e.Id);
                    if (item != null)
                    {
                        state.Remove(item);
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

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
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