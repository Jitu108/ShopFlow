namespace ShopFlow.Shared.Events;

public record OrderShippedEvent(
    Guid OrderId,
    string TrackingNumber,
    DateTime ShippedAt
);
