using Cart.Application.Interfaces;
using MassTransit;
using ShopFlow.Shared.Events;

namespace Cart.Infrastructure.Events;

public class CartEventPublisher : ICartEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public CartEventPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task PublishStockAdjustedAsync(Guid productId, int quantityDelta, CancellationToken ct)
        => await _publishEndpoint.Publish(new CartStockAdjustedEvent(productId, quantityDelta), ct);
}
