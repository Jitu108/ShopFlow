using FluentAssertions;
using Identity.Application.Commands;
using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Identity.Domain.Exceptions;
using NSubstitute;

namespace Identity.Application.Tests.Commands;

public class LoginCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _handler = new LoginCommandHandler(_userRepository, _tokenService);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_ShouldReturnAuthResponse()
    {
        var command = new LoginCommand("john@example.com", "StrongP@ss1");
        var user = ApplicationUser.Create("john@example.com", "John Doe");

        _userRepository.FindByEmailAsync(command.Email, default).Returns(user);
        _userRepository.CheckPasswordAsync(user, command.Password, default).Returns(true);
        _tokenService.GenerateJwtToken(user).Returns("jwt-token");
        _tokenService.GenerateRefreshTokenAsync(user.Id, default).Returns(new RefreshToken("refresh-token", DateTime.UtcNow.AddDays(7), user.Id));

        var result = await _handler.Handle(command, default);

        result.AccessToken.Should().Be("jwt-token");
        result.RefreshToken.Should().Be("refresh-token");
    }

    [Fact]
    public async Task Handle_WithUnknownEmail_ShouldThrowInvalidCredentialsException()
    {
        var command = new LoginCommand("ghost@example.com", "StrongP@ss1");

        _userRepository.FindByEmailAsync(command.Email, default).Returns((ApplicationUser?)null);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }

    [Fact]
    public async Task Handle_WithWrongPassword_ShouldThrowInvalidCredentialsException()
    {
        var command = new LoginCommand("john@example.com", "WrongPass1!");
        var user = ApplicationUser.Create("john@example.com", "John Doe");

        _userRepository.FindByEmailAsync(command.Email, default).Returns(user);
        _userRepository.CheckPasswordAsync(user, command.Password, default).Returns(false);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
    }
}