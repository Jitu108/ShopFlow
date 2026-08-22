using Microsoft.EntityFrameworkCore;
using Order.Application.Interfaces;
using Order.Domain.Entities;

namespace Order.Infrastructure.Persistence.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public OrderRepository(AppDbContext context) => _context = context;

    public async Task<OrderEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _context.Orders
            .Include(o => o.OrderItems)
            .SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<OrderEntity>> GetByCustomerIdAsync(Guid customerId, CancellationToken ct)
        => await _context.Orders
            .Include(o => o.OrderItems)
            .Where(x => x.CustomerId == customerId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OrderEntity>> GetAllAsync(CancellationToken ct)
        => await _context.Orders
            .Include(o => o.OrderItems)
            .ToListAsync(ct);

    public async Task AddAsync(OrderEntity order, CancellationToken ct)
    {
        await _context.Orders.AddAsync(order, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(OrderEntity order, CancellationToken ct)
    {
        _context.Orders.Update(order);
        await _context.SaveChangesAsync(ct);
    }
}
