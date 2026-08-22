using FluentAssertions;
using Order.Domain.Entities;
using Order.Domain.Enums;
using Order.Domain.Exceptions;

namespace Order.Domain.Tests.Entities;

public class OrderEntityTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();
    private const string CustomerEmail = "customer@example.com";

    private static List<OrderItemEntity> ValidItems() =>
    [
        OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10.00m, 2),
        OrderItemEntity.Create(Guid.NewGuid(), "Gadget", 25.00m, 1)
    ];

    [Fact]
    public void Create_WithValidData_ShouldSetProperties()
    {
        var items = ValidItems();

        var order = OrderEntity.Create(CustomerId, CustomerEmail, items);

        order.CustomerId.Should().Be(CustomerId);
        order.CustomerEmail.Should().Be(CustomerEmail);
        order.OrderItems.Should().HaveCount(2);
        order.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_ShouldSetStatusToPending()
    {
        var order = OrderEntity.Create(CustomerId, CustomerEmail, ValidItems());

        order.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void Create_ShouldCalculateTotalAmount_FromItems()
    {
        var order = OrderEntity.Create(CustomerId, CustomerEmail, ValidItems());

        order.TotalAmount.Should().Be(45.00m);
    }

    [Fact]
    public void Create_ShouldSet_CreatedAtAndUpdatedAtToNow()
    {
        var before = DateTime.UtcNow;

        var order = OrderEntity.Create(CustomerId, CustomerEmail, ValidItems());

        order.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        order.UpdatedAt.Should().Be(order.CreatedAt);
    }

    [Fact]
    public void Create_WithEmptyCustomerId_ShouldThrowDomainException()
    {
        var act = () => OrderEntity.Create(Guid.Empty, CustomerEmail, ValidItems());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithBlankCustomerEmail_ShouldThrowDomainException()
    {
        var act = () => OrderEntity.Create(CustomerId, "  ", ValidItems());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_WithNoItems_ShouldThrowDomainException()
    {
        var act = () => OrderEntity.Create(CustomerId, CustomerEmail, []);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Confirm_WhenPending_ShouldSetStatusToConfirmed_AndBumpUpdatedAt()
    {
        var order = OrderEntity.Create(CustomerId, CustomerEmail, ValidItems());

        order.Confirm();

        order.Status.Should().Be(OrderStatus.Confirmed);
        order.UpdatedAt.Should().BeOnOrAfter(order.CreatedAt);
    }

    [Fact]
    public void Confirm_WhenAlreadyConfirmed_ShouldThrowDomainException()
    {
        var order = OrderEntity.Create(CustomerId, CustomerEmail, ValidItems());
        order.Confirm();

        var act = order.Confirm;

        act.Should().Throw<DomainException>();
    }
}
