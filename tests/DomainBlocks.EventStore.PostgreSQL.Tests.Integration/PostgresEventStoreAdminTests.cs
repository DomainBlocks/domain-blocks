using DomainBlocks.EventStore.TypeMapping;
using DomainBlocks.Testing.Events;
using DomainBlocks.Testing.Integration.EventStore.PostgreSQL;
using Npgsql;
using NUnit.Framework;
using Shouldly;

namespace DomainBlocks.EventStore.PostgreSQL.Tests.Integration;

[TestFixture]
public class PostgresEventStoreAdminTests
{
    private static int _schemaCounter;
    private PostgresEventStoreOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        var schema = $"dbx_admin_tests_{Interlocked.Increment(ref _schemaCounter)}";
        _options = new PostgresEventStoreOptions { Schema = schema };
    }

    [TearDown]
    public async Task TearDown()
    {
        await PostgresEventStoreAdmin.DropAsync(PostgresTestEnvironment.DataSource, _options);
    }

    [Test]
    public async Task EnsureInitializedAsync_CreatesSchemaObjects()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options);

        (await TableExistsAsync("event_log")).ShouldBeTrue();
        (await TableExistsAsync("sequences")).ShouldBeTrue();
        (await PublicationExistsAsync()).ShouldBeTrue();
        (await GetSequenceNextAsync()).ShouldBe(0);
    }

    [Test]
    public async Task EnsureInitializedAsync_CreatesAppendProtocolEnums()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options);

        // The labels are the wire protocol of append_events.
        (await GetEnumLabelsAsync("expected_state_kind")).ShouldBe(["any", "does_not_exist", "exists", "at_version"]);
        (await GetEnumLabelsAsync("append_status")).ShouldBe(["appended", "conflict", "duplicate"]);
    }

    [Test]
    public async Task EnsureInitializedAsync_DataSourceLoadedTypesBeforeInit_StoreCanAppend()
    {
        await using var dataSource =
            PostgresTestEnvironment.CreateDataSource(builder => builder.UsePostgresEventStore(_options));

        // Opening a connection makes the data source load the database's types, before the schema exists.
        await using (await dataSource.OpenConnectionAsync())
        {
        }

        await PostgresEventStoreAdmin.EnsureInitializedAsync(dataSource, _options);

        var eventTypeMap = EventTypeMap.Create(EventTypeMapping.ReadWrite<TestEvent>());
        var codec = TestPostgresEventCodec.Create<object>(eventTypeMap);
        await using var eventStore = PostgresEventStore.Create(dataSource, codec, _options);

        await eventStore.AppendAsync("s1", [AppendableEvent.Create<object>(new TestEvent { Value = "v" })]);

        (await GetSequenceNextAsync()).ShouldBe(1);
    }

    [Test]
    public async Task EnsureInitializedAsync_StreamIdUsesByteWiseCollation()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options);

        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT collation_name FROM information_schema.columns " +
            "WHERE table_schema = $1 AND table_name = 'event_log' AND column_name = 'stream_id'");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = _options.Schema });

        (await command.ExecuteScalarAsync()).ShouldBe("C");
    }

    [Test]
    public async Task EnsureInitializedAsync_CommitIdIndexIsUniqueAndPartialOnFirstEvent()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options);

        var definition = (await GetIndexDefinitionAsync("event_log_commit_id_idx")).ShouldNotBeNull();
        definition.ShouldStartWith("CREATE UNIQUE INDEX");
        definition.ShouldEndWith("WHERE (commit_index = 0)");
    }

    [Test]
    public async Task EnsureInitializedAsync_CommitIdIndexRejectsRepeatedCommitIdFromAnotherWriter()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options);

        var commitId = Guid.NewGuid();
        await InsertRowAsync(position: 0, streamId: "s1", streamPosition: 0, commitId, commitIndex: 0);

        // A second event of the same commit is fine: only the first row of a commit is indexed.
        await InsertRowAsync(position: 1, streamId: "s1", streamPosition: 1, commitId, commitIndex: 1);

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            InsertRowAsync(position: 2, streamId: "s2", streamPosition: 0, commitId, commitIndex: 0));

        ex.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        ex.ConstraintName.ShouldBe("event_log_commit_id_idx");
    }

    [Test]
    public async Task EnsureInitializedAsync_CalledTwice_IsIdempotent()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options);
        await PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options);

        (await TableExistsAsync("event_log")).ShouldBeTrue();
        (await PublicationExistsAsync()).ShouldBeTrue();
    }

    [Test]
    public async Task EnsureInitializedAsync_ConcurrentCalls_AllSucceed()
    {
        var tasks = Enumerable
            .Range(0, 8)
            .Select(_ => PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options));

        await Task.WhenAll(tasks);

        (await TableExistsAsync("event_log")).ShouldBeTrue();
        (await PublicationExistsAsync()).ShouldBeTrue();
        (await GetSequenceNextAsync()).ShouldBe(0);
    }

    [Test]
    public async Task EnsureInitializedAsync_SeedsSequenceRowOnce()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options);

        await using (var command = PostgresTestEnvironment.DataSource.CreateCommand(
                         $"UPDATE {_options.Schema}.sequences SET next = 42 WHERE name = 'event_log'"))
        {
            await command.ExecuteNonQueryAsync();
        }

        await PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options);

        (await GetSequenceNextAsync()).ShouldBe(42);
    }

    [Test]
    public async Task EnsureInitializedAsync_CreatePublicationDisabled_SkipsPublication()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(
            PostgresTestEnvironment.DataSource,
            _options,
            new PostgresEventStoreAdminOptions { CreatePublication = false });

        (await TableExistsAsync("event_log")).ShouldBeTrue();
        (await PublicationExistsAsync()).ShouldBeFalse();
    }

    [Test]
    public void EnsureInitializedAsync_InvalidSchemaName_Throws()
    {
        var options = new PostgresEventStoreOptions { Schema = "Not-Valid" };

        Should.ThrowAsync<ArgumentException>(() =>
            PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, options));
    }

    [Test]
    public async Task DropAsync_RemovesSchemaAndPublication()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(PostgresTestEnvironment.DataSource, _options);

        await PostgresEventStoreAdmin.DropAsync(PostgresTestEnvironment.DataSource, _options);

        (await TableExistsAsync("event_log")).ShouldBeFalse();
        (await PublicationExistsAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task DropAsync_WhenNothingExists_Succeeds()
    {
        await PostgresEventStoreAdmin.DropAsync(PostgresTestEnvironment.DataSource, _options);
    }

    private async Task<bool> TableExistsAsync(string table)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = $1 AND table_name = $2)");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = _options.Schema });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = table });

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<bool> PublicationExistsAsync()
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_publication WHERE pubname = $1)");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"{_options.Schema}_event_log_pub" });

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task InsertRowAsync(
        long position,
        string streamId,
        long streamPosition,
        Guid commitId,
        int commitIndex)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            $"INSERT INTO {_options.Schema}.event_log " +
            "(position, stream_id, stream_position, commit_id, commit_index, event_name, event_data, created_at) " +
            "VALUES ($1, $2, $3, $4, $5, 'e', '{}'::jsonb, now())");

        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = position });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = streamId });
        command.Parameters.Add(new NpgsqlParameter<long> { TypedValue = streamPosition });
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = commitId });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = commitIndex });

        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> GetEnumLabelsAsync(string type)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT e.enumlabel FROM pg_enum AS e WHERE e.enumtypid = to_regtype($1) ORDER BY e.enumsortorder");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"{_options.Schema}.{type}" });

        var labels = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
            labels.Add(reader.GetString(0));

        return labels;
    }

    private async Task<string?> GetIndexDefinitionAsync(string index)
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = $1 AND indexname = $2");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = _options.Schema });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = index });

        return (string?)await command.ExecuteScalarAsync();
    }

    private async Task<long> GetSequenceNextAsync()
    {
        await using var command = PostgresTestEnvironment.DataSource.CreateCommand(
            $"SELECT next FROM {_options.Schema}.sequences WHERE name = 'event_log'");

        return (long)(await command.ExecuteScalarAsync())!;
    }
}