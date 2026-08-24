namespace Cart.Application.Interfaces;

public interface ICartEventPublisher
{
    Task PublishStockAdjustedAsync(Guid productId, int quantityDelta, CancellationToken ct);
}
