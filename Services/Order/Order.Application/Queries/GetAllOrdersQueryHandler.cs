using MediatR;
using Order.Application.DTOs;
using Order.Application.Interfaces;
using Order.Application.Mapping;

namespace Order.Application.Queries;

public class GetAllOrdersQueryHandler : IRequestHandler<GetAllOrdersQuery, IReadOnlyList<OrderDto>>
{
    private readonly IOrderRepository _orderRepository;

    public GetAllOrdersQueryHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<IReadOnlyList<OrderDto>> Handle(GetAllOrdersQuery query, CancellationToken ct)
    {
        var orders = await _orderRepository.GetAllAsync(ct);
        return orders.Select(o => o.ToDto()).ToList();
    }
}
