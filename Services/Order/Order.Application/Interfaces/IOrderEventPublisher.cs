using Order.Domain.Entities;

namespace Order.Application.Interfaces;

public interface IOrderEventPublisher
{
    Task PublishOrderPlacedAsync(OrderEntity order, CancellationToken ct);
}
