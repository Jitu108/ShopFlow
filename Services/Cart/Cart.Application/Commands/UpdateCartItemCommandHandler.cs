using Cart.Application.DTOs;
using Cart.Application.Interfaces;
using Cart.Domain.Exceptions;
using MediatR;

namespace Cart.Application.Commands;

public class UpdateCartItemCommandHandler : IRequestHandler<UpdateCartItemCommand, CartItemDto>
{
    private readonly ICartRepository _cartRepository;
    private readonly ICartEventPublisher _cartEventPublisher;

    public UpdateCartItemCommandHandler(ICartRepository cartRepository, ICartEventPublisher cartEventPublisher)
    {
        _cartRepository = cartRepository;
        _cartEventPublisher = cartEventPublisher;
    }

    public async Task<CartItemDto> Handle(UpdateCartItemCommand command, CancellationToken ct)
    {
        var cart = await _cartRepository.GetCartAsync(command.UserId, ct);

        if (!cart.TryGetValue(command.ProductId, out var existing))
            throw new NotFoundException(nameof(CartItemDto), command.ProductId);

        var item = existing with { Quantity = command.Quantity };
        await _cartRepository.UpsertItemAsync(command.UserId, item, ct);

        var delta = command.Quantity - existing.Quantity;
        if (delta != 0)
        {
            await _cartEventPublisher.PublishStockAdjustedAsync(command.ProductId, delta, ct);
        }

        return item;
    }
}
