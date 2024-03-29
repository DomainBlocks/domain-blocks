using DomainBlocks.Logging;
using DomainBlocks.Persistence.DependencyInjection;
using DomainBlocks.Persistence.EventStoreDb;
using Shopping.Api;
using Shopping.Domain.Events;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// DomainBlocks configuration.
builder.Services.AddEntityStore(config => config
    .UseEventStoreDb("esdb://shopping.eventstore:2113?tls=false")
    .AddEntityAdapters(x => x.AddGenericFactoryFor(typeof(AggregateAdapter<>)))
    .MapEvents(x => x.MapAll<IDomainEvent>()));

var app = builder.Build();

// Configure DomainBlocks logging.
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
LogProvider.SetLoggerFactory(loggerFactory);

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