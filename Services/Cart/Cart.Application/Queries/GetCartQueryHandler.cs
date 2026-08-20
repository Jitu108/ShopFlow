using Cart.Application.DTOs;
using Cart.Application.Interfaces;
using MediatR;

namespace Cart.Application.Queries;

public class GetCartQueryHandler : IRequestHandler<GetCartQuery, IReadOnlyList<CartItemDto>>
{
    private readonly ICartRepository _cartRepository;

    public GetCartQueryHandler(ICartRepository cartRepository)
    {
        _cartRepository = cartRepository;
    }

    public async Task<IReadOnlyList<CartItemDto>> Handle(GetCartQuery query, CancellationToken ct)
    {
        var cart = await _cartRepository.GetCartAsync(query.UserId, ct);
        return cart.Values.ToList();
    }
}
