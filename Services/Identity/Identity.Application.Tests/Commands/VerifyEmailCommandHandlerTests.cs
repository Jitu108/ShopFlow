using FluentAssertions;
using Identity.Application.Commands;
using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Identity.Domain.Exceptions;
using NSubstitute;

namespace Identity.Application.Tests.Commands;

public class VerifyEmailCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly VerifyEmailCommandHandler _handler;

    public VerifyEmailCommandHandlerTests()
    {
        _handler = new VerifyEmailCommandHandler(_userRepository);
    }

    [Fact]
    public async Task Handle_WithExistingUser_ShouldSetIsEmailVerifiedTrue()
    {
        var user = ApplicationUser.Create("target@example.com", "Target User");
        var command = new VerifyEmailCommand(user.Id);

        _userRepository.GetByIdAsync(user.Id, default).Returns(user);

        await _handler.Handle(command, default);

        user.IsEmailVerified.Should().BeTrue();
        await _userRepository.Received(1).UpdateAsync(user, default);
    }

    [Fact]
    public async Task Handle_WithUnknownUser_ShouldThrowNotFoundException()
    {
        var userId = Guid.NewGuid();
        var command = new VerifyEmailCommand(userId);

        _userRepository.GetByIdAsync(userId, default).Returns((ApplicationUser?)null);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
