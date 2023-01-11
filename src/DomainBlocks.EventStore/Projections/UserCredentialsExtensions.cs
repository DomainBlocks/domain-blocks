using EventStore.Client;

namespace DomainBlocks.EventStore.Projections;

public static class UserCredentialsExtensions
{
    public static UserCredentials ToEsUserCredentials(this Core.UserCredentials userCredentials)
    {
        return !string.IsNullOrWhiteSpace(userCredentials.AuthToken)
            ? new UserCredentials(userCredentials.AuthToken)
            : new UserCredentials(userCredentials.UserName!, userCredentials.Password!);
    }
}