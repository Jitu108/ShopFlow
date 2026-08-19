using FluentValidation.TestHelper;
using Product.Application.Commands;
using Product.Application.Validators;

namespace Product.Application.Tests.Validators;

public class CreateCategoryCommandValidatorTests
{
    private readonly CreateCategoryCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_ShouldHaveNoErrors()
    {
        var result = _validator.TestValidate(new CreateCategoryCommand("Electronics"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WithBlankName_ShouldHaveError(string? name)
    {
        var result = _validator.TestValidate(new CreateCategoryCommand(name!));

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithTooLongName_ShouldHaveError()
    {
        var result = _validator.TestValidate(new CreateCategoryCommand(new string('A', 101)));

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }
}
