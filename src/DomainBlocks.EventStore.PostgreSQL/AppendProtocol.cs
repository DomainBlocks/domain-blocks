using System.Diagnostics;
using NpgsqlTypes;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The enums exchanged with <c>append_events</c>. Their labels are part of a wire protocol, so they are spelled out and
/// a C# rename cannot change them.
/// </summary>
internal static class AppendProtocol
{
    /// <summary>
    /// The <c>expected_state_kind</c> enum.
    /// </summary>
    public enum ExpectedKind
    {
        [PgName("any")] Any,
        [PgName("does_not_exist")] DoesNotExist,
        [PgName("exists")] Exists,
        [PgName("at_version")] AtVersion
    }

    /// <summary>
    /// The <c>append_status</c> enum.
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