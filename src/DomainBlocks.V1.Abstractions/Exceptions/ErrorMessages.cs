using System.Text;

namespace DomainBlocks.V1.Abstractions.Exceptions;

public static class ErrorMessages
{
    public static string WrongExpectedVersion(
        string streamName, StreamPosition expectedVersion, StreamPosition? actualVersion = null)
    {
        return WrongExpectedVersionInternal(
            streamName, sb => sb.Append($"Expected Version: {expectedVersion}"), actualVersion);
    }

    public static string WrongExpectedVersion(
        string streamName, ExpectedStreamState expectedState, StreamPosition? actualVersion = null)
    {
        return WrongExpectedVersionInternal(
            streamName, sb => sb.Append($"Expected State: {expectedState}"), actualVersion);
    }

    private static string WrongExpectedVersionInternal(
        string streamName, Action<StringBuilder> expectedAction, StreamPosition? actualVersion)
    {
        var sb = new StringBuilder();
        sb.Append("Append failed due to wrong expected version. ");
        sb.Append($"Stream: {streamName}, ");
        expectedAction(sb);

        if (actualVersion.HasValue)
        {
            sb.Append($", Actual Version: {actualVersion}");
        }

        return sb.ToString();
    }
}