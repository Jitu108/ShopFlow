using MediatR;
using Order.Application.DTOs;
using Order.Application.Interfaces;
using Order.Application.Mapping;
using Order.Domain.Entities;

namespace Order.Application.Commands;

public class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;

    public PlaceOrderCommandHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<OrderDto> Handle(PlaceOrderCommand command, CancellationToken ct)
    {
        var items = command.Items
            .Select(i => OrderItemEntity.Create(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity))
            .ToList();

        var order = OrderEntity.Create(command.CustomerId, command.CustomerEmail, items);

        await _orderRepository.AddAsync(order, ct);

        return order.ToDto();
    }
}
