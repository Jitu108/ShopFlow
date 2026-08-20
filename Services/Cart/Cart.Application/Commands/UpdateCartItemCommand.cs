using Cart.Application.DTOs;
using MediatR;

namespace Cart.Application.Commands;

public record UpdateCartItemCommand(
    Guid UserId,
    Guid ProductId,
    int Quantity
) : IRequest<CartItemDto>;
