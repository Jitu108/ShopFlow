using FluentAssertions;
using Identity.Application.Commands;
using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Identity.Domain.Exceptions;
using NSubstitute;

namespace Identity.Application.Tests.Commands;

public class ResetPasswordCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ResetPasswordCommandHandler _handler;

    public ResetPasswordCommandHandlerTests()
    {
        _handler = new ResetPasswordCommandHandler(_userRepository);
    }

    [Fact]
    public async Task Handle_WithExistingUser_ShouldResetPassword()
    {
        var user = ApplicationUser.Create("target@example.com", "Target User");
        var command = new ResetPasswordCommand(user.Id, "NewStrongP@ss1");

        _userRepository.GetByIdAsync(user.Id, default).Returns(user);

        await _handler.Handle(command, default);

        await _userRepository.Received(1).ResetPasswordAsync(user, "NewStrongP@ss1", default);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ShouldThrowNotFoundException()
    {
        var userId = Guid.NewGuid();
        var command = new ResetPasswordCommand(userId, "NewStrongP@ss1");

        _userRepository.GetByIdAsync(userId, default).Returns((ApplicationUser?)null);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
