using DomainLib.Projections.AspNetCore;
using DomainLib.Projections.EventStore.AspNetCore;
using DomainLib.Projections.Serialization.Json.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Shopping.Domain.Events;
using Shopping.ReadModel.Db;

namespace Shopping.ReadModel
{
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
                                                             options.UseNpgsql(Configuration
                                                                                   .GetConnectionString("Default")));

            services.AddReadModel<IDomainEvent, ShoppingCartDbContext>(Configuration,
                                                                       options =>
                                                                       {
                                                                           options.UseEventStorePublishedEvents();
                                                                           options.UseJsonDeserialization();
                                                                       },
                                                                       (builder, dbContext) =>
                                                                       {
                                                                           ShoppingClassSummaryEfProjection
                                                                               .Register(builder, dbContext);
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
}
