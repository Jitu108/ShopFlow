using FluentAssertions;
using FluentValidation;
using Identity.Application.Behaviors;
using Identity.Application.Commands;
using Identity.Application.DTOs;
using Identity.Application.Validators;
using MediatR;
using NSubstitute;

namespace Identity.Application.Tests.Behaviors;

public class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WithValidRequest_ShouldCallNext()
    {
        var validator = new RegisterUserCommandValidator();
        var behavior = new ValidationBehavior<RegisterUserCommand, AuthResponse>([validator]);
        var next = Substitute.For<RequestHandlerDelegate<AuthResponse>>();
        var command = new RegisterUserCommand("john@example.com", "StrongP@ss1", "John Doe");

        next.Invoke().Returns(new AuthResponse("token", "refresh", "john@example.com", "John Doe", "Customer"));

        await behavior.Handle(command, next, default);

        await next.Received(1).Invoke();
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ShouldThrowValidationException()
    {
        var validator = new RegisterUserCommandValidator();
        var behavior = new ValidationBehavior<RegisterUserCommand, AuthResponse>([validator]);
        var next = Substitute.For<RequestHandlerDelegate<AuthResponse>>();
        var command = new RegisterUserCommand("not-an-email", "weak", "");

        var act = async () => await behavior.Handle(command, next, default);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ShouldNotCallNext()
    {
        var validator = new RegisterUserCommandValidator();
        var behavior = new ValidationBehavior<RegisterUserCommand, AuthResponse>([validator]);
        var next = Substitute.For<RequestHandlerDelegate<AuthResponse>>();
        var command = new RegisterUserCommand("not-an-email", "weak", "");

        try { await behavior.Handle(command, next, default); } catch { }

        await next.DidNotReceive().Invoke();
    }
}