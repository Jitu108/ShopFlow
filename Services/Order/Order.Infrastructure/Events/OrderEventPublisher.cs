using MassTransit;
using Order.Application.Interfaces;
using Order.Domain.Entities;
using ShopFlow.Shared.Events;

namespace Order.Infrastructure.Events;

public class OrderEventPublisher : IOrderEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public OrderEventPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task PublishOrderPlacedAsync(OrderEntity order, CancellationToken ct)
    {
        var items = order.OrderItems
            .Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity))
            .ToList();

        await _publishEndpoint.Publish(new OrderPlacedEvent(
            order.Id,
            order.CustomerId,
            order.CustomerEmail,
            items,
            order.TotalAmount,
            DateTime.UtcNow
        ), ct);
    }
}
