namespace Identity.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid UserId { get; private set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, DateTime expiresAt)
    {
        return new RefreshToken
        {
            Id        = Guid.NewGuid(),
            Token     = $"{Convert.ToBase64String(Guid.NewGuid().ToByteArray())}{Convert.ToBase64String(Guid.NewGuid().ToByteArray())}",
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            UserId    = userId
        };
    }
}
