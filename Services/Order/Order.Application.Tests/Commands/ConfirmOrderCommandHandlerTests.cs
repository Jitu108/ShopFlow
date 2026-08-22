using FluentAssertions;
using NSubstitute;
using Order.Application.Commands;
using Order.Application.Interfaces;
using Order.Domain.Entities;
using Order.Domain.Enums;
using Order.Domain.Exceptions;

namespace Order.Application.Tests.Commands;

public class ConfirmOrderCommandHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IOrderEventPublisher _orderEventPublisher = Substitute.For<IOrderEventPublisher>();
    private readonly ConfirmOrderCommandHandler _handler;

    public ConfirmOrderCommandHandlerTests()
    {
        _handler = new ConfirmOrderCommandHandler(_orderRepository, _orderEventPublisher);
    }

    private static OrderEntity ValidOrder(Guid customerId) => OrderEntity.Create(
        customerId, "customer@example.com",
        [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10.00m, 2)]);

    [Fact]
    public async Task Handle_WithPendingOrder_ShouldConfirm_AndReturnDto()
    {
        var customerId = Guid.NewGuid();
        var order = ValidOrder(customerId);
        var command = new ConfirmOrderCommand(order.Id, customerId);

        _orderRepository.GetByIdAsync(order.Id, default).Returns(order);

        var result = await _handler.Handle(command, default);

        result.Status.Should().Be(OrderStatus.Confirmed.ToString());
        await _orderRepository.Received(1).UpdateAsync(order, default);
    }

    [Fact]
    public async Task Handle_WithPendingOrder_ShouldPublishOrderPlacedEvent()
    {
        var customerId = Guid.NewGuid();
        var order = ValidOrder(customerId);
        var command = new ConfirmOrderCommand(order.Id, customerId);

        _orderRepository.GetByIdAsync(order.Id, default).Returns(order);

        await _handler.Handle(command, default);

        await _orderEventPublisher.Received(1).PublishOrderPlacedAsync(order, default);
    }

    [Fact]
    public async Task Handle_WhenOrderNotFound_ShouldThrowNotFoundException()
    {
        var command = new ConfirmOrderCommand(Guid.NewGuid(), Guid.NewGuid());

        _orderRepository.GetByIdAsync(command.OrderId, default).Returns((OrderEntity?)null);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenCustomerDoesNotOwnOrder_ShouldThrowNotFoundException()
    {
        var order = ValidOrder(Guid.NewGuid());
        var command = new ConfirmOrderCommand(order.Id, Guid.NewGuid());

        _orderRepository.GetByIdAsync(order.Id, default).Returns(order);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenOrderAlreadyConfirmed_ShouldThrowDomainException()
    {
        var customerId = Guid.NewGuid();
        var order = ValidOrder(customerId);
        order.Confirm();
        var command = new ConfirmOrderCommand(order.Id, customerId);

        _orderRepository.GetByIdAsync(order.Id, default).Returns(order);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<DomainException>();
    }
}
