namespace DomainBlocks.Core;

public class UserCredentials
{
    public UserCredentials(string userName, string password)
    {
        UserName = userName ?? throw new ArgumentNullException(nameof(userName));
        Password = password ?? throw new ArgumentNullException(nameof(password));
    }

    public UserCredentials(string authToken)
    {
        AuthToken = authToken ?? throw new ArgumentNullException(nameof(authToken));
    }

    public string? AuthToken { get; }
    public string? UserName { get; }
    public string? Password { get; }
}