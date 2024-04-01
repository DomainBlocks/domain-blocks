using System.Text;

namespace DomainBlocks.V1.Abstractions.Exceptions;

public static class ErrorMessages
{
    public static string WrongExpectedVersion(
        string streamName, StreamVersion expectedVersion, StreamVersion? actualVersion = null)
    {
        return WrongExpectedVersionInternal(
            streamName, sb => sb.Append($"Expected Version: {expectedVersion}"), actualVersion);
    }

    public static string WrongExpectedVersion(
        string streamName, ExpectedStreamState expectedState, StreamVersion? actualVersion = null)
    {
        return WrongExpectedVersionInternal(
            streamName, sb => sb.Append($"Expected State: {expectedState}"), actualVersion);
    }

    private static string WrongExpectedVersionInternal(
        string streamName, Action<StringBuilder> expectedAction, StreamVersion? actualVersion)
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