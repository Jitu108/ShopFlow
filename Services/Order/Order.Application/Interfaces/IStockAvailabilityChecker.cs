using Order.Domain.Entities;

namespace Order.Application.Interfaces;

public interface IStockAvailabilityChecker
{
    Task<StockAvailabilityResult> CheckAsync(IReadOnlyList<OrderItemEntity> items, CancellationToken ct);
}

public record StockAvailabilityResult(bool IsAvailable, IReadOnlyList<Guid> InsufficientProductIds);
