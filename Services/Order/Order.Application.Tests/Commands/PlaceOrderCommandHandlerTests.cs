using FluentAssertions;
using NSubstitute;
using Order.Application.Commands;
using Order.Application.DTOs;
using Order.Application.Interfaces;
using Order.Domain.Enums;

namespace Order.Application.Tests.Commands;

public class PlaceOrderCommandHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly PlaceOrderCommandHandler _handler;

    public PlaceOrderCommandHandlerTests()
    {
        _handler = new PlaceOrderCommandHandler(_orderRepository);
    }

    private static PlaceOrderCommand ValidCommand() => new(
        Guid.NewGuid(),
        "customer@example.com",
        [
            new OrderItemRequestDto(Guid.NewGuid(), "Widget", 10.00m, 2),
            new OrderItemRequestDto(Guid.NewGuid(), "Gadget", 25.00m, 1)
        ]);

    [Fact]
    public async Task Handle_WithValidCommand_ShouldReturnDto_WithPendingStatus()
    {
        var result = await _handler.Handle(ValidCommand(), default);

        result.Status.Should().Be(OrderStatus.Pending.ToString());
        result.CustomerEmail.Should().Be("customer@example.com");
        result.OrderItems.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ShouldCalculateTotalAmount_FromItems()
    {
        var result = await _handler.Handle(ValidCommand(), default);

        result.TotalAmount.Should().Be(45.00m);
    }

    [Fact]
    public async Task Handle_ShouldPersistOrder_ViaRepository()
    {
        var command = ValidCommand();

        var result = await _handler.Handle(command, default);

        await _orderRepository.Received(1).AddAsync(
            Arg.Is<Domain.Entities.OrderEntity>(o => o.Id == result.Id), default);
    }
}
