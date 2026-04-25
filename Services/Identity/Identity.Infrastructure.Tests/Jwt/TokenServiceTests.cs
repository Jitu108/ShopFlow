using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Identity.Infrastructure.Jwt;
using Identity.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Identity.Infrastructure.Tests.Jwt;

public class TokenServiceTests
{
    private readonly IRefreshTokenRepository _repo = Substitute.For<IRefreshTokenRepository>();
    private readonly TokenService _tokenService;
    private readonly ApplicationUser _user;

    public TokenServiceTests()
    {
        var settings = Options.Create(new JwtSettings
        {
            Secret         = "super-secret-test-key-that-is-long-enough-for-hmac256",
            Issuer         = "ShopFlow",
            Audience       = "ShopFlow",
            ExpiryMinutes  = 60
        });

        _tokenService = new TokenService(settings, _repo);
        _user = ApplicationUser.Create("john@example.com", "John Doe");
    }

    private static IEnumerable<System.Security.Claims.Claim> ParseClaims(string jwt)
        => new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims;

    [Fact]
    public void GenerateJwtToken_ShouldContain_UserIdClaim()
    {
        var jwt = _tokenService.GenerateJwtToken(_user);

        ParseClaims(jwt).Should().Contain(c => c.Type == "userId" && c.Value == _user.Id.ToString());
    }

    [Fact]
    public void GenerateJwtToken_ShouldContain_EmailClaim()
    {
        var jwt = _tokenService.GenerateJwtToken(_user);

        ParseClaims(jwt).Should().Contain(c => c.Value == "john@example.com");
    }

    [Fact]
    public void GenerateJwtToken_ShouldContain_RoleClaim()
    {
        var jwt = _tokenService.GenerateJwtToken(_user);

        ParseClaims(jwt).Should().Contain(c => c.Value == "Customer");
    }

    [Fact]
    public void GenerateJwtToken_WhenEmailNotVerified_ShouldContain_EmailVerifiedFalse()
    {
        var jwt = _tokenService.GenerateJwtToken(_user);

        ParseClaims(jwt).Should().Contain(c => c.Type == "emailVerified" && c.Value == "false");
    }

    [Fact]
    public void GenerateJwtToken_WhenEmailVerified_ShouldContain_EmailVerifiedTrue()
    {
        _user.VerifyEmail();

        var jwt = _tokenService.GenerateJwtToken(_user);

        ParseClaims(jwt).Should().Contain(c => c.Type == "emailVerified" && c.Value == "true");
    }

    [Fact]
    public void GenerateJwtToken_ShouldReturn_ValidThreePartJwt()
    {
        var jwt = _tokenService.GenerateJwtToken(_user);

        jwt.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public async Task GenerateRefreshTokenAsync_ShouldCall_SaveAsync_Once()
    {
        await _tokenService.GenerateRefreshTokenAsync(_user.Id, default);

        await _repo.Received(1).SaveAsync(Arg.Any<RefreshToken>(), default);
    }

    [Fact]
    public async Task GenerateRefreshTokenAsync_ShouldReturn_NonEmptyToken()
    {
        var result = await _tokenService.GenerateRefreshTokenAsync(_user.Id, default);

        result.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerateRefreshTokenAsync_TwoCalls_ShouldReturn_UniqueTokens()
    {
        var first  = await _tokenService.GenerateRefreshTokenAsync(_user.Id, default);
        var second = await _tokenService.GenerateRefreshTokenAsync(_user.Id, default);

        first.Token.Should().NotBe(second.Token);
    }

    [Fact]
    public async Task GenerateRefreshTokenAsync_ShouldReturn_TokenWithCorrectUserId()
    {
        var result = await _tokenService.GenerateRefreshTokenAsync(_user.Id, default);

        result.UserId.Should().Be(_user.Id);
    }
}