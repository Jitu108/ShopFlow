using FluentValidation.TestHelper;
using Product.Application.Commands;
using Product.Application.Validators;

namespace Product.Application.Tests.Validators;

public class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new();

    private static CreateProductCommand ValidCommand() => new(
        Guid.NewGuid(), "Widget", "A useful widget", 9.99m, 10, Guid.NewGuid());

    [Fact]
    public void Validate_WithValidCommand_ShouldHaveNoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WithBlankName_ShouldHaveError(string? name)
    {
        var command = ValidCommand() with { Name = name! };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_WithNegativePrice_ShouldHaveError()
    {
        var command = ValidCommand() with { Price = -1m };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Price);
    }

    [Fact]
    public void Validate_WithNegativeStockQuantity_ShouldHaveError()
    {
        var command = ValidCommand() with { StockQuantity = -1 };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.StockQuantity);
    }

    [Fact]
    public void Validate_WithEmptyCategoryId_ShouldHaveError()
    {
        var command = ValidCommand() with { CategoryId = Guid.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.CategoryId);
    }

    [Fact]
    public void Validate_WithEmptyVendorId_ShouldHaveError()
    {
        var command = ValidCommand() with { VendorId = Guid.Empty };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.VendorId);
    }
}
