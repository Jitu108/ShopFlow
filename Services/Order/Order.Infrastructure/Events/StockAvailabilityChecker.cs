using MassTransit;
using Order.Application.Interfaces;
using Order.Domain.Entities;
using ShopFlow.Shared.Events;

namespace Order.Infrastructure.Events;

public class StockAvailabilityChecker : IStockAvailabilityChecker
{
    private readonly IRequestClient<CheckStockRequest> _requestClient;

    public StockAvailabilityChecker(IRequestClient<CheckStockRequest> requestClient)
    {
        _requestClient = requestClient;
    }

    public async Task<StockAvailabilityResult> CheckAsync(IReadOnlyList<OrderItemEntity> items, CancellationToken ct)
    {
        var request = new CheckStockRequest(items
            .Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity))
            .ToList());

        var response = await _requestClient.GetResponse<CheckStockResponse>(request, ct);

        return new StockAvailabilityResult(response.Message.IsAvailable, response.Message.InsufficientProductIds);
    }
}
