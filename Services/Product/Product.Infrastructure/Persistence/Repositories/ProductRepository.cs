using Microsoft.EntityFrameworkCore;
using Product.Application.Interfaces;
using Product.Domain.Entities;

namespace Product.Infrastructure.Persistence.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _context;

    public ProductRepository(AppDbContext context) => _context = context;

    public async Task<ProductEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _context.Products.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<ProductEntity>> GetAllActiveAsync(CancellationToken ct)
        => await _context.Products
            .Where(x => x.IsActive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProductEntity>> GetByVendorIdAsync(Guid vendorId, CancellationToken ct)
        => await _context.Products
            .Where(x => x.VendorId == vendorId)
            .ToListAsync(ct);

    public async Task AddAsync(ProductEntity product, CancellationToken ct)
    {
        await _context.Products.AddAsync(product, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ProductEntity product, CancellationToken ct)
    {
        _context.Products.Update(product);
        await _context.SaveChangesAsync(ct);
    }
}
