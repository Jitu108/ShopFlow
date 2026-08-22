using Order.Domain.Entities;

namespace Order.Application.Interfaces;

public interface IOrderRepository
{
    Task<OrderEntity?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<OrderEntity>> GetByCustomerIdAsync(Guid customerId, CancellationToken ct);
    Task<IReadOnlyList<OrderEntity>> GetAllAsync(CancellationToken ct);
    Task AddAsync(OrderEntity order, CancellationToken ct);
    Task UpdateAsync(OrderEntity order, CancellationToken ct);
}
