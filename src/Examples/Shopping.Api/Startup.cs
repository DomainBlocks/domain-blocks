using DomainBlocks.Persistence.AspNetCore;
using DomainBlocks.Persistence.SqlStreamStore.AspNetCore;
using DomainBlocks.Serialization.Json.AspNetCore;
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
        services.AddAggregateRepository<IDomainEvent>(
            _configuration,
            options =>
            {
                options.RawEventDataType<string>()
                    .UseSqlStreamStoreForEventsAndSnapshots()
                    .UseJsonSerialization();
            },
            aggregates =>
            {
                aggregates.Register<ShoppingCartState>(aggregate =>
                {
                    aggregate.InitialState(_ => new ShoppingCartState())
                        .Id(o => o.Id?.ToString())
                        .PersistenceKey(id => $"shoppingCart-{id}")
                        .SnapshotKey(id => $"shoppingCartSnapshot-{id}")
                        .RegisterEvents(ShoppingCartFunctions.RegisterEvents);
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