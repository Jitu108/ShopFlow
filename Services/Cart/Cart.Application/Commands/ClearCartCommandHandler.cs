using Cart.Application.Interfaces;
using MediatR;

namespace Cart.Application.Commands;

public class ClearCartCommandHandler : IRequestHandler<ClearCartCommand>
{
    private readonly ICartRepository _cartRepository;

    public ClearCartCommandHandler(ICartRepository cartRepository)
    {
        _cartRepository = cartRepository;
    }

    public async Task Handle(ClearCartCommand command, CancellationToken ct)
        => await _cartRepository.ClearCartAsync(command.UserId, ct);
}
