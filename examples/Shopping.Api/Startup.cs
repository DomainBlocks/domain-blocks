using DomainBlocks.DependencyInjection;
using DomainBlocks.SqlStreamStore.Persistence;
using DomainBlocks.SqlStreamStore.Postgres;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;

namespace Shopping.Api;

public class Startup
{
    private readonly IConfiguration _configuration;

    public Startup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddGrpc();
        services.AddMediatR(typeof(Startup));

        var connectionString = _configuration.GetValue<string>("SqlStreamStore:ConnectionString");
        var settings = new PostgresStreamStoreSettings(connectionString);

        services.AddAggregateRepository((options, model) =>
        {
            options.UseSqlStreamStore(o => o.UsePostgresStreamStore(settings));

            model.ImmutableAggregate<ShoppingCartState, IDomainEvent>(aggregate =>
            {
                aggregate.WithKeyPrefix("shoppingCart");
                aggregate.ApplyEventsWith(ShoppingCartFunctions.Apply);
                aggregate.UseEventTypesFrom(typeof(IDomainEvent).Assembly);
            });
        });

        new PostgresStreamStore(settings).CreateSchemaIfNotExists().Wait();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGrpcService<Services.ShoppingService>();

            endpoints.MapGet("/",
                async context =>
                {
                    await context.Response.WriteAsync(
                        "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
                });
        });
    }
}