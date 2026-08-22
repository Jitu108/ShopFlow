using FluentAssertions;
using NSubstitute;
using Order.Application.Interfaces;
using Order.Application.Queries;
using Order.Domain.Entities;

namespace Order.Application.Tests.Queries;

public class GetMyOrdersQueryHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly GetMyOrdersQueryHandler _handler;

    public GetMyOrdersQueryHandlerTests()
    {
        _handler = new GetMyOrdersQueryHandler(_orderRepository);
    }

    [Fact]
    public async Task Handle_WithOrders_ShouldReturnMappedDtos()
    {
        var customerId = Guid.NewGuid();
        var order = OrderEntity.Create(customerId, "customer@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10.00m, 1)]);
        _orderRepository.GetByCustomerIdAsync(customerId, default).Returns([order]);

        var result = await _handler.Handle(new GetMyOrdersQuery(customerId), default);

        result.Should().ContainSingle(o => o.Id == order.Id);
    }

    [Fact]
    public async Task Handle_WithNoOrders_ShouldReturnEmptyList()
    {
        var customerId = Guid.NewGuid();
        _orderRepository.GetByCustomerIdAsync(customerId, default).Returns([]);

        var result = await _handler.Handle(new GetMyOrdersQuery(customerId), default);

        result.Should().BeEmpty();
    }
}
