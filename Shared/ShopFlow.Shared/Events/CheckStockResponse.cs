namespace ShopFlow.Shared.Events;

public record CheckStockResponse(bool IsAvailable, List<Guid> InsufficientProductIds);
