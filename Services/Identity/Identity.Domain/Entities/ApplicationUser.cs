using Identity.Domain.Enums;

namespace Identity.Domain.Entities;

public class ApplicationUser
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public bool IsEmailVerified { get; private set; }

    private readonly List<RefreshToken> _refreshTokens = new();
    public IReadOnlyList<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private ApplicationUser() { }

    public static ApplicationUser Create(string email, string displayName)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email cannot be empty.", "Email");

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName cannot be empty.", "DisplayName");

        return new ApplicationUser
        {
            Id              = Guid.NewGuid(),
            Email           = email.Trim(),
            DisplayName     = displayName.Trim(),
            Role            = UserRole.Customer,
            IsEmailVerified = false
        };
    }

    public void VerifyEmail() => IsEmailVerified = true;

    public void UpdateProfile(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName cannot be empty.", "DisplayName");

        DisplayName = displayName.Trim();
    }

    public void AssignRole(UserRole role) => Role = role;
}
