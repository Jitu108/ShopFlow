using FluentAssertions;
using NSubstitute;
using Order.Application.Interfaces;
using Order.Application.Queries;
using Order.Domain.Entities;
using Order.Domain.Exceptions;

namespace Order.Application.Tests.Queries;

public class GetOrderByIdQueryHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly GetOrderByIdQueryHandler _handler;

    public GetOrderByIdQueryHandlerTests()
    {
        _handler = new GetOrderByIdQueryHandler(_orderRepository);
    }

    private static OrderEntity ValidOrder(Guid customerId) => OrderEntity.Create(
        customerId, "customer@example.com",
        [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10.00m, 2)]);

    [Fact]
    public async Task Handle_AsOwner_ShouldReturnDto()
    {
        var customerId = Guid.NewGuid();
        var order = ValidOrder(customerId);
        _orderRepository.GetByIdAsync(order.Id, default).Returns(order);

        var result = await _handler.Handle(new GetOrderByIdQuery(order.Id, customerId, IsAdmin: false), default);

        result.Id.Should().Be(order.Id);
    }

    [Fact]
    public async Task Handle_AsAdmin_ForSomeoneElsesOrder_ShouldReturnDto()
    {
        var order = ValidOrder(Guid.NewGuid());
        _orderRepository.GetByIdAsync(order.Id, default).Returns(order);

        var result = await _handler.Handle(
            new GetOrderByIdQuery(order.Id, Guid.NewGuid(), IsAdmin: true), default);

        result.Id.Should().Be(order.Id);
    }

    [Fact]
    public async Task Handle_WhenOrderNotFound_ShouldThrowNotFoundException()
    {
        var orderId = Guid.NewGuid();
        _orderRepository.GetByIdAsync(orderId, default).Returns((OrderEntity?)null);

        var act = async () => await _handler.Handle(
            new GetOrderByIdQuery(orderId, Guid.NewGuid(), IsAdmin: false), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_AsNonOwnerNonAdmin_ShouldThrowNotFoundException()
    {
        var order = ValidOrder(Guid.NewGuid());
        _orderRepository.GetByIdAsync(order.Id, default).Returns(order);

        var act = async () => await _handler.Handle(
            new GetOrderByIdQuery(order.Id, Guid.NewGuid(), IsAdmin: false), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
