using Cart.Application.DTOs;
using Cart.Application.Interfaces;
using MediatR;

namespace Cart.Application.Commands;

public class AddCartItemCommandHandler : IRequestHandler<AddCartItemCommand, CartItemDto>
{
    private readonly ICartRepository _cartRepository;
    private readonly ICartEventPublisher _cartEventPublisher;

    public AddCartItemCommandHandler(ICartRepository cartRepository, ICartEventPublisher cartEventPublisher)
    {
        _cartRepository = cartRepository;
        _cartEventPublisher = cartEventPublisher;
    }

    public async Task<CartItemDto> Handle(AddCartItemCommand command, CancellationToken ct)
    {
        var cart = await _cartRepository.GetCartAsync(command.UserId, ct);

        var quantity = command.Quantity;
        if (cart.TryGetValue(command.ProductId, out var existing))
        {
            quantity += existing.Quantity;
        }

        var item = new CartItemDto(command.ProductId, command.ProductName, command.UnitPrice, quantity);
        await _cartRepository.UpsertItemAsync(command.UserId, item, ct);
        await _cartEventPublisher.PublishStockAdjustedAsync(command.ProductId, command.Quantity, ct);

        return item;
    }
}
