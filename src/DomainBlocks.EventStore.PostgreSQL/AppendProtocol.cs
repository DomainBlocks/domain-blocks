using System.Diagnostics;
using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The labels of the <c>expected_state_kind</c> and <c>append_status</c> enums exchanged with the <c>append_events</c>
/// function. They are a wire protocol and must not be derived from C# enum names. Kinds are sent as text and cast to
/// the enum in SQL, and the status is read back as text, so no Npgsql type mapping is needed.
/// </summary>
internal static class AppendProtocol
{
    public const string ExpectedAny = "any";
    public const string ExpectedDoesNotExist = "does_not_exist";
    public const string ExpectedExists = "exists";
    public const string ExpectedAtVersion = "at_version";

    public const string StatusAppended = "appended";
    public const string StatusConflict = "conflict";
    public const string StatusDuplicate = "duplicate";

    public static string ToExpectedKind(ExpectedStreamStateKind kind) => kind switch
    {
        ExpectedStreamStateKind.Any => ExpectedAny,
        ExpectedStreamStateKind.DoesNotExist => ExpectedDoesNotExist,
        ExpectedStreamStateKind.Exists => ExpectedExists,
        ExpectedStreamStateKind.AtVersion => ExpectedAtVersion,
        _ => throw new UnreachableException($"Unexpected expected stream state kind '{kind}'.")
    };
}