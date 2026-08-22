using FluentAssertions;
using NSubstitute;
using Order.Application.Interfaces;
using Order.Application.Queries;
using Order.Domain.Entities;

namespace Order.Application.Tests.Queries;

public class GetAllOrdersQueryHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly GetAllOrdersQueryHandler _handler;

    public GetAllOrdersQueryHandlerTests()
    {
        _handler = new GetAllOrdersQueryHandler(_orderRepository);
    }

    [Fact]
    public async Task Handle_ShouldReturnAllOrders_AcrossCustomers()
    {
        var orderA = OrderEntity.Create(Guid.NewGuid(), "a@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10.00m, 1)]);
        var orderB = OrderEntity.Create(Guid.NewGuid(), "b@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Gadget", 20.00m, 1)]);
        _orderRepository.GetAllAsync(default).Returns([orderA, orderB]);

        var result = await _handler.Handle(new GetAllOrdersQuery(), default);

        result.Should().HaveCount(2);
    }
}
