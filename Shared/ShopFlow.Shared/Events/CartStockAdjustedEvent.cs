namespace ShopFlow.Shared.Events;

public record CartStockAdjustedEvent(Guid ProductId, int QuantityDelta);
