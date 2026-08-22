using MediatR;
using Order.Application.DTOs;
using Order.Application.Interfaces;
using Order.Application.Mapping;

namespace Order.Application.Queries;

public class GetMyOrdersQueryHandler : IRequestHandler<GetMyOrdersQuery, IReadOnlyList<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;

    public GetMyOrdersQueryHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<IReadOnlyList<OrderDto>> Handle(GetMyOrdersQuery query, CancellationToken ct)
    {
        var orders = await _orderRepository.GetByCustomerIdAsync(query.CustomerId, ct);
        return orders.Select(o => o.ToDto()).ToList();
    }
}
