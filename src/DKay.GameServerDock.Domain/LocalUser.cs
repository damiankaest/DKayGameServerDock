namespace DKay.GameServerDock.Domain;

public sealed class LocalUser
{
    private LocalUser()
    {
    }

    public Guid Id { get; private set; }
    public string UserName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static LocalUser Create(string userName, string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new LocalUser
        {
            Id = Guid.NewGuid(),
            UserName = userName.Trim(),
            PasswordHash = passwordHash,
            CreatedAt = now
        };
    }
}

