using MediatR;

namespace Cart.Application.Commands;

public record RemoveCartItemCommand(
    Guid UserId,
    Guid ProductId
) : IRequest;
