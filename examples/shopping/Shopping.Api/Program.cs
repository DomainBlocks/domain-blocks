using DomainBlocks.Logging;
using DomainBlocks.ThirdParty.SqlStreamStore.Postgres;
using Npgsql;
using Polly;
using Shopping.ReadModel;
using Shopping.WriteModel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var eventStore = builder.Configuration.GetValue<string>("EventStore");
if (eventStore == "SqlStreamStore")
{
    // Still using the pre-Npgsql 6.0 timestamp behaviour.
    // See: https://www.npgsql.org/doc/types/datetime.html#timestamps-and-timezones
    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    await EnsureSqlStreamStoreCreatedAsync();
}

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

return;

async Task EnsureSqlStreamStoreCreatedAsync()
{
    var connectionString = builder.Configuration.GetConnectionString("SqlStreamStore");
    var streamStore = new PostgresStreamStore(new PostgresStreamStoreSettings(connectionString));

    const int retryCount = 3;
    var delay = TimeSpan.FromSeconds(3);

    // Wait before the initial attempt to allow the container some time to start.
    await Task.Delay(delay);

    await Policy
        .Handle<NpgsqlException>()
        .WaitAndRetryAsync(retryCount, _ => delay)
        .ExecuteAsync(() => streamStore.CreateSchemaIfNotExists());
}