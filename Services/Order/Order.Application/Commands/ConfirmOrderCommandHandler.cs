using MediatR;
using Order.Application.DTOs;
using Order.Application.Interfaces;
using Order.Application.Mapping;
using Order.Domain.Entities;
using Order.Domain.Exceptions;

namespace Order.Application.Commands;

public class ConfirmOrderCommandHandler : IRequestHandler<ConfirmOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderEventPublisher _orderEventPublisher;
    private readonly IStockAvailabilityChecker _stockAvailabilityChecker;

    public ConfirmOrderCommandHandler(
        IOrderRepository orderRepository,
        IOrderEventPublisher orderEventPublisher,
        IStockAvailabilityChecker stockAvailabilityChecker)
    {
        _orderRepository = orderRepository;
        _orderEventPublisher = orderEventPublisher;
        _stockAvailabilityChecker = stockAvailabilityChecker;
    }

    public async Task<OrderDto> Handle(ConfirmOrderCommand command, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(command.OrderId, ct)
            ?? throw new NotFoundException(nameof(OrderEntity), command.OrderId);

        if (order.CustomerId != command.CustomerId)
            throw new NotFoundException(nameof(OrderEntity), command.OrderId);

        var stockAvailability = await _stockAvailabilityChecker.CheckAsync(order.OrderItems, ct);
        if (!stockAvailability.IsAvailable)
        {
            throw new DomainException(
                $"Insufficient stock for product(s): {string.Join(", ", stockAvailability.InsufficientProductIds)}.");
        }

        order.Confirm();

        await _orderRepository.UpdateAsync(order, ct);
        await _orderEventPublisher.PublishOrderPlacedAsync(order, ct);

        return order.ToDto();
    }
}
