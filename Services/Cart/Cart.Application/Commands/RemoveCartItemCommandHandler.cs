using Cart.Application.Interfaces;
using MediatR;

namespace Cart.Application.Commands;

public class RemoveCartItemCommandHandler : IRequestHandler<RemoveCartItemCommand>
{
    private readonly ICartRepository _cartRepository;

    public RemoveCartItemCommandHandler(ICartRepository cartRepository)
    {
        _cartRepository = cartRepository;
    }

    public async Task Handle(RemoveCartItemCommand command, CancellationToken ct)
        => await _cartRepository.RemoveItemAsync(command.UserId, command.ProductId, ct);
}
