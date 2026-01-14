using DomainBlocks.EventStore.MongoDB.Appender.Host;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(50051, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
});

var app = builder.Build();

app.MapGrpcService<AppenderServiceImpl>();

app.Run();