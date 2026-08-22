using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Order.Domain.Entities;
using Order.Infrastructure.Events;
using ShopFlow.Shared.Events;

namespace Order.Infrastructure.Tests.Events;

public class OrderEventPublisherTests
{
    [Fact]
    public async Task PublishOrderPlacedAsync_ShouldPublish_OrderPlacedEvent_WithOrderData()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var order = OrderEntity.Create(Guid.NewGuid(), "customer@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10.00m, 2)]);
        order.Confirm();

        var publisher = new OrderEventPublisher(harness.Bus);
        await publisher.PublishOrderPlacedAsync(order, default);

        (await harness.Published.Any<OrderPlacedEvent>(x =>
            x.Context.Message.OrderId == order.Id &&
            x.Context.Message.CustomerEmail == "customer@example.com" &&
            x.Context.Message.Total == order.TotalAmount)).Should().BeTrue();
    }
}
