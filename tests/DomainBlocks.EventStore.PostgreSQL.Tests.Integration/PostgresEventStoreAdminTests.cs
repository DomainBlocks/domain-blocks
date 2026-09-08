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
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, _options);
    }

    [Test]
    public async Task EnsureInitializedAsync_CreatesSchemaObjects()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, _options);

        (await TableExistsAsync("event_log")).ShouldBeTrue();
        (await TableExistsAsync("sequences")).ShouldBeTrue();
        (await PublicationExistsAsync()).ShouldBeTrue();
        (await GetSequenceNextAsync()).ShouldBe(0);
    }

    [Test]
    public async Task EnsureInitializedAsync_CalledTwice_IsIdempotent()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, _options);
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, _options);

        (await TableExistsAsync("event_log")).ShouldBeTrue();
        (await PublicationExistsAsync()).ShouldBeTrue();
    }

    [Test]
    public async Task EnsureInitializedAsync_ConcurrentCalls_AllSucceed()
    {
        var tasks = Enumerable
            .Range(0, 8)
            .Select(_ => PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, _options));

        await Task.WhenAll(tasks);

        (await TableExistsAsync("event_log")).ShouldBeTrue();
        (await PublicationExistsAsync()).ShouldBeTrue();
        (await GetSequenceNextAsync()).ShouldBe(0);
    }

    [Test]
    public async Task EnsureInitializedAsync_SeedsSequenceRowOnce()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, _options);

        await using (var command = SetUpFixture.DataSource.CreateCommand(
                         $"UPDATE {_options.Schema}.sequences SET next = 42 WHERE name = 'event_log'"))
        {
            await command.ExecuteNonQueryAsync();
        }

        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, _options);

        (await GetSequenceNextAsync()).ShouldBe(42);
    }

    [Test]
    public async Task EnsureInitializedAsync_CreatePublicationDisabled_SkipsPublication()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(
            SetUpFixture.DataSource,
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
            PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, options));
    }

    [Test]
    public async Task DropAsync_RemovesSchemaAndPublication()
    {
        await PostgresEventStoreAdmin.EnsureInitializedAsync(SetUpFixture.DataSource, _options);

        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, _options);

        (await TableExistsAsync("event_log")).ShouldBeFalse();
        (await PublicationExistsAsync()).ShouldBeFalse();
    }

    [Test]
    public async Task DropAsync_WhenNothingExists_Succeeds()
    {
        await PostgresEventStoreAdmin.DropAsync(SetUpFixture.DataSource, _options);
    }

    private async Task<bool> TableExistsAsync(string table)
    {
        await using var command = SetUpFixture.DataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = $1 AND table_name = $2)");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = _options.Schema });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = table });

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<bool> PublicationExistsAsync()
    {
        await using var command = SetUpFixture.DataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_publication WHERE pubname = $1)");

        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = $"{_options.Schema}_event_log_pub" });

        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> GetSequenceNextAsync()
    {
        await using var command = SetUpFixture.DataSource.CreateCommand(
            $"SELECT next FROM {_options.Schema}.sequences WHERE name = 'event_log'");

        return (long)(await command.ExecuteScalarAsync())!;
    }
}
