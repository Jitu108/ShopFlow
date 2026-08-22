using MediatR;
using Order.Application.DTOs;
using Order.Application.Interfaces;
using Order.Application.Mapping;
using Order.Domain.Entities;
using Order.Domain.Exceptions;

namespace Order.Application.Queries;

public class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto>
{
    private readonly IOrderRepository _orderRepository;

    public GetOrderByIdQueryHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<OrderDto> Handle(GetOrderByIdQuery query, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(query.OrderId, ct)
            ?? throw new NotFoundException(nameof(OrderEntity), query.OrderId);

        if (!query.IsAdmin && order.CustomerId != query.RequesterId)
            throw new NotFoundException(nameof(OrderEntity), query.OrderId);

        return order.ToDto();
    }
}
