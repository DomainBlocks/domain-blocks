using DomainBlocks.Persistence.DependencyInjection;
using DomainBlocks.Persistence.EventStoreDb;
using Shopping.Api2;
using Shopping.Domain.Events.New;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// DomainBlocks configuration.
builder.Services.AddEntityStore(config => config
    .UseEventStoreDb("esdb://localhost:2113?tls=false")
    .AddEntityAdapters(x => x.AddGenericFactoryFor(typeof(AggregateAdapter<>)))
    .MapEvents(x => x.MapAll<IDomainEvent>()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();