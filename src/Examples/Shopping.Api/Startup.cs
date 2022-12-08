using DomainBlocks.Persistence.AspNetCore;
using DomainBlocks.Persistence.SqlStreamStore.New;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shopping.Domain.Aggregates;
using Shopping.Domain.Events;
using SqlStreamStore;

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

        services.AddAggregateRepository((options, model) =>
        {
            options.UseSqlStreamStore(o =>
            {
                var connectionString = _configuration.GetValue<string>("SqlStreamStore:ConnectionString");
                var settings = new PostgresStreamStoreSettings(connectionString);
                o.UsePostgres(settings).UseJsonSerialization();
            });

            model.ImmutableAggregate<ShoppingCartState, IDomainEvent>(aggregate =>
            {
                aggregate.WithKeyPrefix("shoppingCart");
                aggregate.ApplyEventsWith(ShoppingCartFunctions.Apply);
                aggregate.UseEventTypesFrom(typeof(IDomainEvent).Assembly);
            });
        });
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