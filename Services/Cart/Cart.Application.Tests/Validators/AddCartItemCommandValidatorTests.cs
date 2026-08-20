using Cart.Application.Commands;
using Cart.Application.Validators;
using FluentValidation.TestHelper;

namespace Cart.Application.Tests.Validators;

public class AddCartItemCommandValidatorTests
{
    private readonly AddCartItemCommandValidator _validator = new();

    private static AddCartItemCommand ValidCommand() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Widget", 9.99m, 1);

    [Fact]
    public void Validate_WithValidCommand_ShouldHaveNoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyUserId_ShouldHaveError()
    {
        var command = ValidCommand() with { UserId = Guid.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Validate_WithEmptyProductId_ShouldHaveError()
    {
        var command = ValidCommand() with { ProductId = Guid.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ProductId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WithBlankProductName_ShouldHaveError(string? name)
    {
        var command = ValidCommand() with { ProductName = name! };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ProductName);
    }

    [Fact]
    public void Validate_WithNegativeUnitPrice_ShouldHaveError()
    {
        var command = ValidCommand() with { UnitPrice = -1m };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.UnitPrice);
    }

    [Fact]
    public void Validate_WithZeroQuantity_ShouldHaveError()
    {
        var command = ValidCommand() with { Quantity = 0 };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Quantity);
    }
}
