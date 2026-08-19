using FluentValidation.TestHelper;
using Identity.Application.Commands;
using Identity.Application.Validators;

namespace Identity.Application.Tests.Validators;

public class ResetPasswordCommandValidatorTests
{
    private readonly ResetPasswordCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldHaveNoErrors()
    {
        var command = new ResetPasswordCommand(Guid.NewGuid(), "NewStrongP@ss1");

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyUserId_ShouldHaveError()
    {
        var command = new ResetPasswordCommand(Guid.Empty, "NewStrongP@ss1");

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WithBlankPassword_ShouldHaveError(string? password)
    {
        var command = new ResetPasswordCommand(Guid.NewGuid(), password!);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }

    [Theory]
    [InlineData("alllowercase1!")]
    [InlineData("ALLUPPERCASE1!")]
    [InlineData("NoSpecialChar1")]
    [InlineData("NoNumber!Abc")]
    [InlineData("Ab1!")]
    public void Validate_WithWeakPassword_ShouldHaveError(string password)
    {
        var command = new ResetPasswordCommand(Guid.NewGuid(), password);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.NewPassword);
    }
}
