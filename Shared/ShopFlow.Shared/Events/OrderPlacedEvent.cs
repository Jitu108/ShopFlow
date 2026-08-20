namespace ShopFlow.Shared.Events;

public record OrderPlacedEvent(
    Guid OrderId,
    Guid CustomerId,
    string CustomerEmail,
    List<OrderItemDto> Items,
    decimal Total,
    DateTime PlacedAt
);
