using Product.Domain.Entities;

namespace Product.Application.Interfaces;

public interface IProductRepository
{
    Task<ProductEntity?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ProductEntity>> GetAllActiveAsync(CancellationToken ct);
    Task<IReadOnlyList<ProductEntity>> GetByVendorIdAsync(Guid vendorId, CancellationToken ct);
    Task AddAsync(ProductEntity product, CancellationToken ct);
    Task UpdateAsync(ProductEntity product, CancellationToken ct);
}
