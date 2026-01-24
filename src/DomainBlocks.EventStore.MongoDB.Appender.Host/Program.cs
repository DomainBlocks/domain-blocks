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
builder.Services.AddHostedService<EventAppenderHostedService>();

var app = builder.Build();

app.MapGrpcService<GrpcAppenderService>();

await app.RunAsync();