using DomainBlocks.Logging;
using Shopping.ReadModel;
using Shopping.WriteModel;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add write and read models for shopping example.
builder.Services.AddShoppingWriteModel(builder.Configuration);
builder.Services.AddShoppingReadModel(builder.Configuration);

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