using MediatR;
using Order.Application.DTOs;

namespace Order.Application.Queries;

public record GetMyOrdersQuery(Guid CustomerId) : IRequest<IReadOnlyList<OrderDto>>;
