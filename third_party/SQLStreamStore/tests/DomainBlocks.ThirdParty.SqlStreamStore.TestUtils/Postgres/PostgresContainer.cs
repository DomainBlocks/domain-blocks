namespace DomainBlocks.ThirdParty.SqlStreamStore.TestUtils.Postgres
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Ductus.FluentDocker.Builders;
    using Ductus.FluentDocker.Services;
    using Npgsql;
    using Polly;

    // TODO: Consider stopping the use of a container here and use an external running Postgres instance to test.
    // This would reduce dependencies for the tests (i.e.Docker.DotNet)  and would potentially facilitate some setup. 
    public class PostgresContainer : PostgresDatabaseManager
    {
        private readonly IContainerService _containerService;
        private const string Image = "postgres:15.3-alpine";
        private const string ContainerName = "sql-stream-store-tests-postgres";
        private const int Port = 5432;
        private const string Password = "password";

        public override string ConnectionString => ConnectionStringBuilder.ConnectionString;

        public PostgresContainer(string databaseName)
            : base(databaseName)
        {
            _containerService = new Builder()
                .UseContainer()
                .WithName(ContainerName)
                .UseImage(Image)
                .KeepRunning()
                .ReuseIfExists()
                .ExposePort(Port, Port)
                .Command("-N", "500")
                .WithEnvironment($"POSTGRES_PASSWORD={Password}")
                .Build();
        }

        public async Task Start(CancellationToken cancellationToken = default)
        {
            _containerService.Start();

            await Policy
                .Handle<NpgsqlException>()
                .WaitAndRetryAsync(30, _ => TimeSpan.FromMilliseconds(500))
                .ExecuteAsync(async () =>
                {
                    using(var connection = new NpgsqlConnection(DefaultConnectionString))
                    {
                        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    }
                });
        }

        private NpgsqlConnectionStringBuilder ConnectionStringBuilder => new NpgsqlConnectionStringBuilder
        {
            Database = DatabaseName,
            Password = Password,
            Port = Port,
            Username = "postgres",
            Host = "localhost",
            Pooling = true,
            MaxPoolSize = 1024
        };
    }
}
