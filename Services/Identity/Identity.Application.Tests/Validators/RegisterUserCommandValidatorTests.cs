using FluentAssertions;
using FluentValidation.TestHelper;
using Identity.Application.Commands;
using Identity.Application.Validators;

namespace Identity.Application.Tests.Validators;

public class RegisterUserCommandValidatorTests
{
    private readonly RegisterUserCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldHaveNoErrors()
    {
        var command = new RegisterUserCommand("john@example.com", "StrongP@ss1", "John Doe");

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WithBlankEmail_ShouldHaveError(string? email)
    {
        var command = new RegisterUserCommand(email!, "StrongP@ss1", "John Doe");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Validate_WithInvalidEmailFormat_ShouldHaveError()
    {
        var command = new RegisterUserCommand("not-an-email", "StrongP@ss1", "John Doe");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WithBlankPassword_ShouldHaveError(string? password)
    {
        var command = new RegisterUserCommand("john@example.com", password!, "John Doe");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Validate_WithShortPassword_ShouldHaveError()
    {
        var command = new RegisterUserCommand("john@example.com", "Ab1!", "John Doe");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Theory]
    [InlineData("alllowercase1!")]
    [InlineData("ALLUPPERCASE1!")]
    [InlineData("NoSpecialChar1")]
    [InlineData("NoNumber!Abc")]
    public void Validate_WithWeakPassword_ShouldHaveError(string password)
    {
        var command = new RegisterUserCommand("john@example.com", password, "John Doe");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WithBlankDisplayName_ShouldHaveError(string? displayName)
    {
        var command = new RegisterUserCommand("john@example.com", "StrongP@ss1", displayName!);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }

    [Fact]
    public void Validate_WithTooLongDisplayName_ShouldHaveError()
    {
        var command = new RegisterUserCommand("john@example.com", "StrongP@ss1", new string('A', 101));

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.DisplayName);
    }
}