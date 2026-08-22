using MediatR;
using Order.Application.DTOs;

namespace Order.Application.Queries;

public record GetOrderByIdQuery(
    Guid OrderId,
    Guid RequesterId,
    bool IsAdmin
) : IRequest<OrderDto>;
