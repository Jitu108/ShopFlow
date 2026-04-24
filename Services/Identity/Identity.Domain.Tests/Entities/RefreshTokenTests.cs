using FluentAssertions;
using Identity.Domain.Entities;

namespace Identity.Domain.Tests.Entities;

public class RefreshTokenTests
{
    [Fact]
    public void IsExpired_WhenExpiryIsInThePast_ShouldReturnTrue()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), expiresAt: DateTime.UtcNow.AddMinutes(-1));

        token.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_WhenExpiryIsInTheFuture_ShouldReturnFalse()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), expiresAt: DateTime.UtcNow.AddDays(7));

        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void Create_ShouldSet_UserId()
    {
        var userId = Guid.NewGuid();

        var token = RefreshToken.Create(userId, expiresAt: DateTime.UtcNow.AddDays(7));

        token.UserId.Should().Be(userId);
    }

    [Fact]
    public void Create_ShouldGenerate_NonEmptyToken()
    {
        var token = RefreshToken.Create(Guid.NewGuid(), expiresAt: DateTime.UtcNow.AddDays(7));

        token.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_TwoCalls_ShouldGenerate_UniqueTokens()
    {
        var userId = Guid.NewGuid();

        var token1 = RefreshToken.Create(userId, expiresAt: DateTime.UtcNow.AddDays(7));
        var token2 = RefreshToken.Create(userId, expiresAt: DateTime.UtcNow.AddDays(7));

        token1.Token.Should().NotBe(token2.Token);
    }

    [Fact]
    public void Create_ShouldSet_CreatedAtToNow()
    {
        var before = DateTime.UtcNow;

        var token = RefreshToken.Create(Guid.NewGuid(), expiresAt: DateTime.UtcNow.AddDays(7));

        token.CreatedAt.Should().BeOnOrAfter(before)
            .And.BeOnOrBefore(DateTime.UtcNow);
    }
}
