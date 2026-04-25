using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Identity.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Jwt;

public class TokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly IRefreshTokenRepository _refreshTokenRepository;

    public TokenService(IOptions<JwtSettings> settings, IRefreshTokenRepository refreshTokenRepository)
    {
        _settings                = settings.Value;
        _refreshTokenRepository  = refreshTokenRepository;
    }

    public string GenerateJwtToken(ApplicationUser user)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("userId",         user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role,  user.Role.ToString()),
            new("emailVerified",  user.IsEmailVerified.ToString().ToLower())
        };

        var token = new JwtSecurityToken(
            issuer:             _settings.Issuer,
            audience:           _settings.Audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<RefreshToken> GenerateRefreshTokenAsync(Guid userId, CancellationToken ct)
    {
        var token = RefreshToken.Create(userId, DateTime.UtcNow.AddDays(7));
        await _refreshTokenRepository.SaveAsync(token, ct);
        return token;
    }
}