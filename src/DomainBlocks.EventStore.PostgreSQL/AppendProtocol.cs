using System.Diagnostics;
using DomainBlocks.EventStore.Abstractions;
using NpgsqlTypes;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The enums exchanged with the <c>append_events</c> function, mapped to the PostgreSQL enums of the same names by
/// <see cref="NpgsqlDataSourceBuilderExtensions.UsePostgresEventStore"/>. The labels are a wire protocol, so they
/// are spelled out rather than derived from the C# member names: a rename cannot change them.
/// </summary>
internal static class AppendProtocol
{
    /// <summary>
    /// The <c>expected_state_kind</c> enum: what a request expects of its stream.
    /// </summary>
    public enum ExpectedKind
    {
        [PgName("any")] Any,
        [PgName("does_not_exist")] DoesNotExist,
        [PgName("exists")] Exists,
        [PgName("at_version")] AtVersion
    }

    /// <summary>
    /// The <c>append_status</c> enum: the outcome of a request.
    /// </summary>
    public enum Status
    {
        [PgName("appended")] Appended,
        [PgName("conflict")] Conflict,
        [PgName("duplicate")] Duplicate
    }

    public static ExpectedKind ToExpectedKind(ExpectedStreamStateKind kind) => kind switch
    {
        ExpectedStreamStateKind.Any => ExpectedKind.Any,
        ExpectedStreamStateKind.DoesNotExist => ExpectedKind.DoesNotExist,
        ExpectedStreamStateKind.Exists => ExpectedKind.Exists,
        ExpectedStreamStateKind.AtVersion => ExpectedKind.AtVersion,
        _ => throw new UnreachableException($"Unexpected expected stream state kind '{kind}'.")
    };
}