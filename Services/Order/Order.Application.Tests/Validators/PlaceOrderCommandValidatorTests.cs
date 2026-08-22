using FluentValidation.TestHelper;
using Order.Application.Commands;
using Order.Application.DTOs;
using Order.Application.Validators;

namespace Order.Application.Tests.Validators;

public class PlaceOrderCommandValidatorTests
{
    private readonly PlaceOrderCommandValidator _validator = new();

    private static List<OrderItemRequestDto> ValidItems() =>
    [
        new(Guid.NewGuid(), "Widget", 9.99m, 2)
    ];

    private static PlaceOrderCommand ValidCommand() =>
        new(Guid.NewGuid(), "customer@example.com", ValidItems());

    [Fact]
    public void Validate_WithValidCommand_ShouldHaveNoErrors()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithNoItems_ShouldHaveError()
    {
        var command = ValidCommand() with { Items = [] };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_WithBlankItemProductName_ShouldHaveError(string name)
    {
        var command = ValidCommand() with
        {
            Items = [new OrderItemRequestDto(Guid.NewGuid(), name, 9.99m, 2)]
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].ProductName");
    }

    [Fact]
    public void Validate_WithNegativeItemUnitPrice_ShouldHaveError()
    {
        var command = ValidCommand() with
        {
            Items = [new OrderItemRequestDto(Guid.NewGuid(), "Widget", -1m, 2)]
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].UnitPrice");
    }

    [Fact]
    public void Validate_WithZeroItemQuantity_ShouldHaveError()
    {
        var command = ValidCommand() with
        {
            Items = [new OrderItemRequestDto(Guid.NewGuid(), "Widget", 9.99m, 0)]
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor("Items[0].Quantity");
    }

    [Fact]
    public void Validate_WithBlankCustomerEmail_ShouldHaveError()
    {
        var command = ValidCommand() with { CustomerEmail = "" };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.CustomerEmail);
    }
}
