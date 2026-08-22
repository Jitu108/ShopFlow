using FluentAssertions;
using Notification.Application.Interfaces;
using Notification.Application.Templates;

namespace Notification.Application.Tests.Templates;

public class OrderConfirmationEmailTemplateTests
{
    private static readonly Guid OrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static List<OrderLineItem> SampleItems() =>
    [
        new OrderLineItem("Widget", 10.00m, 2),
        new OrderLineItem("Gadget", 25.00m, 1)
    ];

    [Fact]
    public void Subject_ShouldContainOrderId()
    {
        var subject = OrderConfirmationEmailTemplate.Subject(OrderId);

        subject.Should().Contain(OrderId.ToString());
    }

    [Fact]
    public void Body_ShouldContainOrderId()
    {
        var body = OrderConfirmationEmailTemplate.Body(OrderId, SampleItems(), 45.00m);

        body.Should().Contain(OrderId.ToString());
    }

    [Fact]
    public void Body_ShouldContainEachItemProductNameAndQuantity()
    {
        var body = OrderConfirmationEmailTemplate.Body(OrderId, SampleItems(), 45.00m);

        body.Should().Contain("Widget");
        body.Should().Contain("Gadget");
        body.Should().Contain("2");
    }

    [Fact]
    public void Body_ShouldContainFormattedTotal()
    {
        var body = OrderConfirmationEmailTemplate.Body(OrderId, SampleItems(), 45.00m);

        body.Should().Contain("45.00");
    }

    [Fact]
    public void Body_WithEmptyItems_ShouldNotThrowAndShouldStillContainTotal()
    {
        var body = OrderConfirmationEmailTemplate.Body(OrderId, [], 0.00m);

        body.Should().Contain("0.00");
    }
}
