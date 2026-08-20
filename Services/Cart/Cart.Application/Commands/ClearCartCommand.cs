using MediatR;

namespace Cart.Application.Commands;

public record ClearCartCommand(
    Guid UserId
) : IRequest;
