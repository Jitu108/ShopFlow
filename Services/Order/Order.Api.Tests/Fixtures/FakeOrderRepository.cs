using Order.Application.Interfaces;
using Order.Domain.Entities;

namespace Order.Api.Tests.Fixtures;

public class FakeOrderRepository : IOrderRepository
{
    private readonly Dictionary<Guid, OrderEntity> _store = new();

    public Task<OrderEntity?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var order);
        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<OrderEntity>> GetByCustomerIdAsync(Guid customerId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<OrderEntity>>(_store.Values.Where(o => o.CustomerId == customerId).ToList());

    public Task<IReadOnlyList<OrderEntity>> GetAllAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<OrderEntity>>(_store.Values.ToList());

    public Task AddAsync(OrderEntity order, CancellationToken ct)
    {
        _store[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(OrderEntity order, CancellationToken ct)
    {
        _store[order.Id] = order;
        return Task.CompletedTask;
    }

    public void Seed(OrderEntity order) => _store[order.Id] = order;
}
