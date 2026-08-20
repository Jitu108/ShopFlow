using Cart.Application.DTOs;
using MediatR;

namespace Cart.Application.Queries;

public record GetCartQuery(Guid UserId) : IRequest<IReadOnlyList<CartItemDto>>;
