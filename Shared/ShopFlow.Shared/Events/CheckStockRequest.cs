namespace ShopFlow.Shared.Events;

public record CheckStockRequest(List<OrderItemDto> Items);
