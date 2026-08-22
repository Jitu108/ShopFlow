using MediatR;
using Order.Application.DTOs;

namespace Order.Application.Commands;

public record PlaceOrderCommand(
    Guid CustomerId,
    string CustomerEmail,
    List<OrderItemRequestDto> Items
) : IRequest<OrderDto>;
