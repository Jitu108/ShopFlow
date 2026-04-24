using FluentAssertions;
using Identity.Application.Commands;
using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Identity.Domain.Exceptions;
using NSubstitute;
using NSubstitute.Core.Arguments;

namespace Identity.Application.Tests.Commands;

public class RefreshTokenCommandHandlerTests
{
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly RefreshTokenCommandHandler _handler;

    public RefreshTokenCommandHandlerTests()
    {
        _handler = new RefreshTokenCommandHandler(_refreshTokenRepository, _userRepository, _tokenService);
    }

    [Fact]
    public async Task Handle_WithValidToken_ShouldReturnNewAuthResponse()
    {
        var userId = Guid.NewGuid();
        var user = ApplicationUser.Create("john@example.com", "John Doe");
        var existingToken = new RefreshToken("old-token", DateTime.UtcNow.AddDays(7), userId);
        var command = new RefreshTokenCommand("old-token");

        _refreshTokenRepository.GetByTokenAsync("old-token", default).Returns(existingToken);
        _userRepository.GetByIdAsync(userId, default).Returns(user);
        _tokenService.GenerateJwtToken(user).Returns("new-jwt-token");
        _tokenService.GenerateRefreshTokenAsync(Arg.Any<Guid>(), default).Returns(new RefreshToken("new-refresh-token", DateTime.UtcNow.AddDays(7), userId));

        var result = await _handler.Handle(command, default);

        result.AccessToken.Should().Be("new-jwt-token");
        result.RefreshToken.Should().Be("new-refresh-token");
    }

    [Fact]
    public async Task Handle_WithValidToken_ShouldRevoke_OldToken()
    {
        var userId = Guid.NewGuid();
        var user = ApplicationUser.Create("john@example.com", "John Doe");
        var existingToken = new RefreshToken("old-token", DateTime.UtcNow.AddDays(7), userId);
        var command = new RefreshTokenCommand("old-token");

        _refreshTokenRepository.GetByTokenAsync("old-token", default).Returns(existingToken);
        _userRepository.GetByIdAsync(userId, default).Returns(user);
        _tokenService.GenerateJwtToken(user).Returns("new-jwt-token");
        _tokenService.GenerateRefreshTokenAsync(Arg.Any<Guid>(), default).Returns(new RefreshToken("new-refresh-token", DateTime.UtcNow.AddDays(7), userId));

        await _handler.Handle(command, default);

        await _refreshTokenRepository.Received(1).RevokeAsync("old-token", default);
    }

    [Fact]
    public async Task Handle_WithExpiredToken_ShouldThrowInvalidCredentialsException()
    {
        var expiredToken = new RefreshToken("expired-token", DateTime.UtcNow.AddDays(-1), Guid.NewGuid());
        var command = new RefreshTokenCommand("expired-token");

        _refreshTokenRepository.GetByTokenAsync("expired-token", default).Returns(expiredToken);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }

    [Fact]
    public async Task Handle_WithUnknownToken_ShouldThrowInvalidCredentialsException()
    {
        var command = new RefreshTokenCommand("unknown-token");

        _refreshTokenRepository.GetByTokenAsync("unknown-token", default).Returns((RefreshToken?)null);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }
}