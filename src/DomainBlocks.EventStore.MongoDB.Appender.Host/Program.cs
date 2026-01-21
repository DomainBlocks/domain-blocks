using DomainBlocks.EventStore.MongoDB.Appender.Host;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var connectionString = builder.Configuration.GetConnectionString("Mongo");
builder.Services.AddSingleton<IMongoClient>(new MongoClient(connectionString));

builder.Services
    .AddOptions<EventAppenderOptions>()
    .Bind(builder.Configuration.GetSection(EventAppenderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<EventAppender>();

var app = builder.Build();

app.MapGrpcService<GrpcAppenderService>();

app.Run();