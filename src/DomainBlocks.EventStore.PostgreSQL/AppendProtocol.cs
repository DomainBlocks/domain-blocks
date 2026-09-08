using System.Diagnostics;
using DomainBlocks.EventStore.Abstractions;

namespace DomainBlocks.EventStore.PostgreSQL;

/// <summary>
/// The codes exchanged with the <c>append_events</c> function. These are a wire protocol and must not be derived from
/// C# enum values.
/// </summary>
internal static class AppendProtocol
{
    public const short ExpectedAny = 0;
    public const short ExpectedDoesNotExist = 1;
    public const short ExpectedExists = 2;
    public const short ExpectedAtVersion = 3;

    public const short StatusAppended = 0;
    public const short StatusConflict = 1;
    public const short StatusDuplicate = 2;

    public const short ObservedDoesNotExist = 0;
    public const short ObservedAtVersion = 1;

    public static short ToExpectedKind(ExpectedStreamStateKind kind)
    {
        return kind switch
        {
            ExpectedStreamStateKind.Any => ExpectedAny,
            ExpectedStreamStateKind.DoesNotExist => ExpectedDoesNotExist,
            ExpectedStreamStateKind.Exists => ExpectedExists,
            ExpectedStreamStateKind.AtVersion => ExpectedAtVersion,
            _ => throw new UnreachableException($"Unexpected expected stream state kind '{kind}'.")
        };
    }
}
