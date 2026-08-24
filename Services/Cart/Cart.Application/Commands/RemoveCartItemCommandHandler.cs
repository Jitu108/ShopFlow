using Cart.Application.Interfaces;
using MediatR;

namespace Cart.Application.Commands;

public class RemoveCartItemCommandHandler : IRequestHandler<RemoveCartItemCommand>
{
    private readonly ICartRepository _cartRepository;
    private readonly ICartEventPublisher _cartEventPublisher;

    public RemoveCartItemCommandHandler(ICartRepository cartRepository, ICartEventPublisher cartEventPublisher)
    {
        _cartRepository = cartRepository;
        _cartEventPublisher = cartEventPublisher;
    }

    public async Task Handle(RemoveCartItemCommand command, CancellationToken ct)
    {
        var cart = await _cartRepository.GetCartAsync(command.UserId, ct);
        await _cartRepository.RemoveItemAsync(command.UserId, command.ProductId, ct);

        if (cart.TryGetValue(command.ProductId, out var existing))
        {
            await _cartEventPublisher.PublishStockAdjustedAsync(command.ProductId, -existing.Quantity, ct);
        }
    }
}
