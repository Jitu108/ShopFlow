using Identity.Domain.Entities;

namespace Identity.Application.Interfaces;

public interface ITokenService
{
    string GenerateJwtToken(ApplicationUser user);
    Task<RefreshToken> GenerateRefreshTokenAsync(Guid userId, CancellationToken ct);
}