using MediatR;
using Order.Application.DTOs;

namespace Order.Application.Queries;

public record GetAllOrdersQuery : IRequest<IReadOnlyList<OrderDto>>;
