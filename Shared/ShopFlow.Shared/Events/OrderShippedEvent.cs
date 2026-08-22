namespace ShopFlow.Shared.Events;

public record OrderShippedEvent(
    Guid OrderId,
    string CustomerEmail,
    string TrackingNumber,
    DateTime ShippedAt
);
