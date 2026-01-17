using DomainBlocks.EventStore.MongoDB.Appender.Host;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.Services.AddSingleton<IMongoClient>(new MongoClient("mongodb://localhost:27017"));
builder.Services.AddSingleton<EventAppender>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(50051, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
});

var app = builder.Build();

app.MapGrpcService<AppenderServiceImpl>();

app.Run();