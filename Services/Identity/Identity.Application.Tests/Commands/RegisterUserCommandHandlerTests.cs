using FluentAssertions;
using Identity.Application.Commands;
using Identity.Application.DTOs;
using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Identity.Application.Tests.Commands;

public class RegisterUserCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly RegisterUserCommandHandler _handler;

    public RegisterUserCommandHandlerTests()
    {
        _handler = new RegisterUserCommandHandler(_userRepository, _tokenService);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldReturnAuthResponse()
    {
        var command = new RegisterUserCommand("john@example.com", "StrongP@ss1", "John Doe");
        var user = ApplicationUser.Create("john@example.com", "John Doe");
        var expectedTokens = new AuthResponse("jwt-token", "refresh-token", user.Email, user.DisplayName, user.Role.ToString());

        _userRepository.ExistsByEmailAsync(command.Email, default).Returns(false);
        _userRepository.CreateAsync(Arg.Any<ApplicationUser>(), command.Password, default).Returns(user);
        _tokenService.GenerateJwtToken(user).Returns("jwt-token");
        _tokenService.GenerateRefreshTokenAsync(user.Id, default).Returns(new RefreshToken("refresh-token", DateTime.UtcNow.AddDays(7), user.Id));

        var result = await _handler.Handle(command, default);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("jwt-token");
        result.RefreshToken.Should().Be("refresh-token");
        result.Email.Should().Be("john@example.com");
    }

    [Fact]
    public async Task Handle_WithDuplicateEmail_ShouldThrowInvalidOperationException()
    {
        var command = new RegisterUserCommand("john@example.com", "StrongP@ss1", "John Doe");

        _userRepository.ExistsByEmailAsync(command.Email, default).Returns(true);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public async Task Handle_ShouldCall_CreateAsync_Once()
    {
        var command = new RegisterUserCommand("john@example.com", "StrongP@ss1", "John Doe");
        var user = ApplicationUser.Create("john@example.com", "John Doe");

        _userRepository.ExistsByEmailAsync(command.Email, default).Returns(false);
        _userRepository.CreateAsync(Arg.Any<ApplicationUser>(), command.Password, default).Returns(user);
        _tokenService.GenerateJwtToken(user).Returns("jwt-token");
        _tokenService.GenerateRefreshTokenAsync(user.Id, default).Returns(new RefreshToken("refresh-token", DateTime.UtcNow.AddDays(7), user.Id));

        await _handler.Handle(command, default);

        await _userRepository.Received(1).CreateAsync(Arg.Any<ApplicationUser>(), command.Password, default);
    }
}