using MediatR;
using Order.Application.DTOs;

namespace Order.Application.Commands;

public record ConfirmOrderCommand(
    Guid OrderId,
    Guid CustomerId
) : IRequest<OrderDto>;
