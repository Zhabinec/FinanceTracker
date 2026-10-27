namespace Finance.Domain.Users;

public sealed class GoogleAccountLink
{
    private GoogleAccountLink() { }
    public Guid UserId { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public static GoogleAccountLink Create(Guid userId, string subject)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
            throw new ArgumentException("Invalid Google identity.");
        return new() { UserId = userId, Subject = subject };
    }
}
