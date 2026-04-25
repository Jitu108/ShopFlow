using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Tests.Fixtures;

public static class JwtTokenHelper
{
    public const string TestSecret   = "super-secret-test-key-that-is-long-enough-for-hmac256";
    public const string TestIssuer   = "ShopFlow";
    public const string TestAudience = "ShopFlow";

    public static string GenerateToken(
        Guid userId,
        string email,
        string role,
        bool emailVerified = false)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("userId",         userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role,  role),
            new Claim("emailVerified",  emailVerified.ToString().ToLower())
        };

        var token = new JwtSecurityToken(
            issuer:             TestIssuer,
            audience:           TestAudience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}