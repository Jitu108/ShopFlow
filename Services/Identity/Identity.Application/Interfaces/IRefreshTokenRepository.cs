using Identity.Domain.Entities;

namespace Identity.Application.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct);
    Task SaveAsync(RefreshToken token, CancellationToken ct);
    Task RevokeAsync(string token, CancellationToken ct);
}