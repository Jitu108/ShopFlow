using Cart.Application.Interfaces;
using MassTransit;
using ShopFlow.Shared.Events;

namespace Cart.Infrastructure.Events;

public class OrderPlacedConsumer : IConsumer<OrderPlacedEvent>
{
    private readonly ICartRepository _cartRepository;

    public OrderPlacedConsumer(ICartRepository cartRepository)
    {
        _cartRepository = cartRepository;
    }

    public async Task Consume(ConsumeContext<OrderPlacedEvent> context)
        => await _cartRepository.ClearCartAsync(context.Message.CustomerId, context.CancellationToken);
}
