using FluentAssertions;
using Order.Domain.Entities;
using Order.Domain.Exceptions;

namespace Order.Domain.Tests.Entities;

public class OrderItemEntityTests
{
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public void Create_WithValidData_ShouldSetProperties()
    {
        var item = OrderItemEntity.Create(ProductId, "Widget", 9.99m, 2);

        item.ProductId.Should().Be(ProductId);
        item.ProductName.Should().Be("Widget");
        item.UnitPrice.Should().Be(9.99m);
        item.Quantity.Should().Be(2);
        item.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_WithBlankProductName_ShouldThrowDomainException()
    {
        var act = () => OrderItemEntity.Create(ProductId, "  ", 9.99m, 2);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNegativeUnitPrice_ShouldThrowDomainException()
    {
        var act = () => OrderItemEntity.Create(ProductId, "Widget", -1m, 2);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithZeroQuantity_ShouldThrowDomainException()
    {
        var act = () => OrderItemEntity.Create(ProductId, "Widget", 9.99m, 0);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNegativeQuantity_ShouldThrowDomainException()
    {
        var act = () => OrderItemEntity.Create(ProductId, "Widget", 9.99m, -1);

        act.Should().Throw<DomainException>();
    }
}
