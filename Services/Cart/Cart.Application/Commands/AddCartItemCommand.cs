using Cart.Application.DTOs;
using MediatR;

namespace Cart.Application.Commands;

public record AddCartItemCommand(
    Guid UserId,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity
) : IRequest<CartItemDto>;
