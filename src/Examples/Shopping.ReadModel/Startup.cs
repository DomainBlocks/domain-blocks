using DomainBlocks.Projections.AspNetCore;
using DomainBlocks.Projections.SqlStreamStore;
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
        services.AddDbContext<ShoppingCartDbContext>(options =>
            options.UseNpgsql(Configuration.GetConnectionString("Default")));

        services.AddHostedEventCatchUpSubscription((sp, subscriptionOptions) =>
        {
            subscriptionOptions
                .UseSqlStreamStore(o =>
                {
                    var connectionString = Configuration.GetValue<string>("SqlStreamStore:ConnectionString");
                    var settings = new PostgresStreamStoreSettings(connectionString);
                    o.UsePostgres(settings).UseJsonSerialization();
                })
                .Using(sp.CreateScope)
                .WithService(x => x.ServiceProvider.GetRequiredService<ShoppingCartDbContext>())
                .AddProjection(projection =>
                {
                    projection.OnInitializing(async (dbContext, ct) =>
                    {
                        await dbContext.Database.EnsureDeletedAsync(ct);
                        await dbContext.Database.EnsureCreatedAsync(ct);
                    });

                    projection.OnCaughtUp(async (dbContext, ct) => await dbContext.SaveChangesAsync(ct));
                    projection.OnEventHandled(async (dbContext, ct) => await dbContext.SaveChangesAsync(ct));

                    projection.When<ItemAddedToShoppingCart>((e, dbContext) =>
                    {
                        dbContext.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
                        {
                            CartId = e.CartId,
                            Id = e.Id,
                            ItemDescription = e.Item
                        });
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