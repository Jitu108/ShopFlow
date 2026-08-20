using Cart.Application.Commands;
using Cart.Application.Validators;
using FluentValidation.TestHelper;

namespace Cart.Application.Tests.Validators;

public class UpdateCartItemCommandValidatorTests
{
    private readonly UpdateCartItemCommandValidator _validator = new();

    private static UpdateCartItemCommand ValidCommand() => new(Guid.NewGuid(), Guid.NewGuid(), 3);

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

    [Fact]
    public void Validate_WithZeroQuantity_ShouldHaveError()
    {
        var command = ValidCommand() with { Quantity = 0 };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Quantity);
    }
}
