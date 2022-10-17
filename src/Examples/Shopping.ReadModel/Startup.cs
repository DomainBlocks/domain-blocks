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

            options.AddProjection<(IServiceScope scope, ShoppingCartDbContext dbContext)>(b =>
            {
                b.OnSubscribing(async (state, ct) =>
                {
                    await state.dbContext.Database.EnsureDeletedAsync(ct);
                    await state.dbContext.Database.EnsureCreatedAsync(ct);
                });

                b.OnUpdating(_ =>
                {
                    var scope = sp.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<ShoppingCartDbContext>();
                    return Task.FromResult((scope, dbContext));
                });

                b.OnUpdated(async (state, ct) =>
                {
                    await state.dbContext.SaveChangesAsync(ct);
                    state.scope.Dispose();
                });

                b.When<ItemAddedToShoppingCart>((e, state) =>
                {
                    state.dbContext.ShoppingCartSummaryItems.Add(new ShoppingCartSummaryItem
                    {
                        CartId = e.CartId,
                        Id = e.Id,
                        ItemDescription = e.Item
                    });
                });

                b.WhenAsync<ItemRemovedFromShoppingCart>(async (e, state) =>
                {
                    var item = await state.dbContext.ShoppingCartSummaryItems.FindAsync(e.Id);
                    if (item != null)
                    {
                        state.dbContext.ShoppingCartSummaryItems.Remove(item);
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